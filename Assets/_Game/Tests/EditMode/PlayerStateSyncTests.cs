using NUnit.Framework;
using UnityEngine;
using JetFighter.Network;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: two instances over loopback see each other's jet move
    /// correctly at the target sync rate.
    ///
    /// Both peers are built here and driven in simulated time, so "at the
    /// target rate" is an assertion rather than something measured by eye on
    /// two screens.
    /// </summary>
    public class PlayerStateSyncTests
    {
        private LocalLoopbackTransport hostLink;
        private LocalLoopbackTransport guestLink;
        private GameObject hostRoot;
        private GameObject guestRoot;
        private NetworkedPlayerState hostSync;
        private NetworkedPlayerState guestSync;
        private Transform hostJet;
        private Transform guestRemoteJet;

        [SetUp]
        public void SetUp()
        {
            (hostLink, guestLink) = LocalLoopbackTransport.CreatePair();

            hostRoot = new GameObject("HostPeer");
            hostJet = new GameObject("HostJet").transform;
            hostJet.SetParent(hostRoot.transform);
            hostSync = hostRoot.AddComponent<NetworkedPlayerState>();
            hostSync.LocalJet = hostJet;
            hostSync.SendRateHz = 20f;
            hostSync.Bind(hostLink);

            guestRoot = new GameObject("GuestPeer");
            guestRemoteJet = new GameObject("RemoteJet").transform;
            guestRemoteJet.SetParent(guestRoot.transform);
            guestSync = guestRoot.AddComponent<NetworkedPlayerState>();
            guestSync.RemoteJet = guestRemoteJet;
            guestSync.Bind(guestLink);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(hostRoot);
            Object.DestroyImmediate(guestRoot);
        }

        /// <summary>Runs both peers for a span of simulated time, delivering as it goes.</summary>
        private void Run(float seconds, float step = 1f / 60f)
        {
            int steps = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < steps; i++)
            {
                hostSync.Tick(step);
                guestLink.Pump();
                guestSync.Tick(step);
                hostLink.Pump();
            }
        }

        [Test]
        public void TheRemoteJetFollowsTheLocalOne()
        {
            hostJet.position = new Vector3(5f, 3f, 0f);
            Run(2f);
            Assert.AreEqual(5f, guestRemoteJet.position.x, 0.05f);
            Assert.AreEqual(3f, guestRemoteJet.position.y, 0.05f);
        }

        [Test]
        public void TheRemoteJetTracksMovementOverTime()
        {
            for (int i = 0; i < 5; i++)
            {
                hostJet.position = new Vector3(i * 2f, 0f, 0f);
                Run(0.5f);
            }
            Assert.AreEqual(8f, guestRemoteJet.position.x, 0.2f);
        }

        [Test]
        public void BroadcastsHappenAtTheConfiguredRate()
        {
            hostSync.SendRateHz = 20f;
            Run(1f);
            Assert.AreEqual(20, hostSync.SentCount, 1);
        }

        [Test]
        public void ChangingTheRateChangesTheTraffic()
        {
            hostSync.SendRateHz = 10f;
            Run(1f);
            Assert.AreEqual(10, hostSync.SentCount, 1);
        }

        [Test]
        public void NotEveryFrameIsAPacket()
        {
            // 60 frames at 15Hz is 15 packets, not 60. Sending per frame is
            // the easiest accidental way to quadruple traffic.
            hostSync.SendRateHz = 15f;
            Run(1f);
            Assert.Less(hostSync.SentCount, 20);
        }

        [Test]
        public void AMissedBroadcastIsNotMadeUpFor()
        {
            // Unlike the gun's cooldown: sending four packets back to back
            // after a hitch wastes bandwidth to deliver three positions the
            // remote will never render.
            hostSync.SendRateHz = 20f;
            hostSync.Tick(1f);
            Assert.AreEqual(1, hostSync.SentCount);
        }

        [Test]
        public void TheFirstPacketSnapsRatherThanFlyingAcrossTheLevel()
        {
            // The remote jet starts at the origin; easing from there would fly
            // it across the level in front of the player.
            hostJet.position = new Vector3(100f, 50f, 0f);
            hostSync.Broadcast();
            guestLink.Pump();

            Assert.AreEqual(100f, guestRemoteJet.position.x, 1e-3f);
            Assert.AreEqual(50f, guestRemoteJet.position.y, 1e-3f);
        }

        [Test]
        public void AReorderedPacketIsRejectedRatherThanSnappingBackwards()
        {
            // UDP-style transports reorder. Applying an older packet after a
            // newer one reads as rubber-banding and gets blamed on latency.
            hostJet.position = new Vector3(1f, 0f, 0f);
            hostSync.Broadcast();
            hostJet.position = new Vector3(9f, 0f, 0f);
            hostSync.Broadcast();
            guestLink.Pump();
            Run(1f);

            byte[] stale = NetworkMessage.EncodePlayerState(new PlayerStatePayload
            {
                position = new Vector3(1f, 0f, 0f), bankAngle = 0f, sequence = 1,
            });
            hostLink.SendState(stale);
            // Sent from the host's link, so it lands in the guest's inbox.
            guestLink.Pump();
            Run(0.5f);

            Assert.AreEqual(1, guestSync.RejectedAsStale);
            Assert.AreEqual(9f, guestRemoteJet.position.x, 0.2f);
        }

        [Test]
        public void AMalformedPacketIsCountedRatherThanApplied()
        {
            // A truncated send or a build-version mismatch is a normal event
            // on a real link. Throwing would take the run down over one bad
            // packet out of thousands.
            hostLink.SendState(new byte[] { (byte)MessageType.PlayerState, 1, 2 });
            guestLink.Pump();

            Assert.AreEqual(1, guestSync.RejectedAsMalformed);
            Assert.AreEqual(0, guestSync.AppliedCount);
        }

        [Test]
        public void ANaNPositionIsRejectedRatherThanPoisoningTheScene()
        {
            // NaN assigned to a Transform poisons every subsequent physics
            // query on that object. Rejecting costs one frame of position.
            byte[] poison = NetworkMessage.EncodePlayerState(new PlayerStatePayload
            {
                position = new Vector3(float.NaN, 0f, 0f), sequence = 1,
            });
            hostLink.SendState(poison);
            guestLink.Pump();

            Assert.AreEqual(1, guestSync.RejectedAsMalformed);
            Assert.IsFalse(float.IsNaN(guestRemoteJet.position.x));
        }

        [Test]
        public void AnUnknownMessageTypeIsIgnoredRatherThanMisread()
        {
            // A peer on a newer build. Without the type tag the receiver would
            // infer the type from the length and produce garbage state.
            hostLink.SendState(new byte[] { 200, 0, 0, 0, 0 });
            guestLink.Pump();

            Assert.AreEqual(0, guestSync.AppliedCount);
            Assert.AreEqual(0, guestSync.RejectedAsMalformed);
        }

        [Test]
        public void AFireEventReachesTheRemotePeer()
        {
            int fires = 0;
            guestSync.OnRemoteFired += () => fires++;

            hostSync.BroadcastFired();
            guestLink.Pump();

            Assert.AreEqual(1, fires);
        }

        [Test]
        public void PacketLossDegradesSmoothnessRatherThanCorrectness()
        {
            hostLink.LossRate = 0.5f;
            hostJet.position = new Vector3(7f, 0f, 0f);
            Run(3f);

            Assert.Greater(hostLink.DroppedByLoss, 0);
            Assert.AreEqual(7f, guestRemoteJet.position.x, 0.1f,
                "the remote jet did not converge despite later packets arriving");
        }

        [Test]
        public void ADisconnectStopsTrafficWithoutThrowing()
        {
            hostLink.Disconnect();
            hostJet.position = new Vector3(4f, 0f, 0f);
            Assert.DoesNotThrow(() => Run(1f));
            Assert.AreEqual(0, guestSync.AppliedCount);
        }

        [Test]
        public void RebindingDoesNotApplyEveryPacketTwice()
        {
            // A reconnect that left the old subscription would apply each
            // packet twice, and the second application is always stale.
            guestSync.Bind(guestLink);
            guestSync.Bind(guestLink);

            hostJet.position = new Vector3(2f, 0f, 0f);
            hostSync.Broadcast();
            guestLink.Pump();

            Assert.AreEqual(1, guestSync.AppliedCount);
        }

        [Test]
        public void ANodeWithNoTransportIsInertRatherThanThrowing()
        {
            hostSync.Bind(null);
            Assert.DoesNotThrow(() => hostSync.Tick(1f));
            Assert.AreEqual(0, hostSync.SentCount);
        }
    }
}
