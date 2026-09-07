using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Network;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The abstraction's own acceptance test.
    ///
    /// The criterion is that swapping LocalLoopbackTransport for
    /// GameKitTransport requires no changes outside Network/. The strongest
    /// version of that is not a diff review -- it is running the same
    /// gameplay component against both transports and asserting it behaves
    /// identically, which is what the contract tests below do.
    ///
    /// GameKit itself is behind INativeMatch, so all of this runs with no
    /// plugin, no device and no match.
    /// </summary>
    public class GameKitTransportTests
    {
        /// <summary>A GKMatch stand-in. Everything the transport needs, nothing more.</summary>
        private sealed class FakeMatch : GameKitTransport.INativeMatch
        {
            public readonly List<byte[]> Sent = new List<byte[]>();
            public bool Connected = true;
            public bool ThrowOnSend;

            public bool IsConnected => Connected;

            public string LocalPlayerId { get; set; } = "G:local";

            public List<string> Remotes { get; } = new List<string> { "G:remote" };

            public IReadOnlyList<string> RemotePlayerIds => Remotes;

            public event Action<byte[], string> OnDataReceived;

            public event Action<string, bool> OnPlayerConnectionChanged;

            public void Send(byte[] payload)
            {
                if (ThrowOnSend)
                {
                    throw new InvalidOperationException("match torn down");
                }
                Sent.Add(payload);
            }

            public void Disconnect() => Connected = false;

            public void DeliverToLocal(byte[] payload) => OnDataReceived?.Invoke(payload, "G:remote");

            public void ChangeConnection(bool connected)
            {
                Connected = connected;
                OnPlayerConnectionChanged?.Invoke("G:remote", connected);
            }
        }

        private FakeMatch match;
        private GameKitTransport transport;

        [SetUp]
        public void SetUp()
        {
            match = new FakeMatch();
            transport = new GameKitTransport(match);
        }

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        // --- the contract, held by both implementations ---------------------

        private static void AssertHonoursTheContract(INetworkTransport link, Action<byte[]> deliver)
        {
            // Everything here is written against the interface only. Both
            // transports are passed through it, so a behaviour that differs
            // between them fails regardless of which one is being swapped in.
            Assert.AreEqual(TransportState.Connected, link.State);

            byte[] received = null;
            link.OnStateReceived += p => received = p;

            Assert.IsTrue(link.SendState(Bytes("payload")));
            deliver(Bytes("payload"));
            Assert.AreEqual("payload", Encoding.UTF8.GetString(received));

            Assert.IsFalse(link.SendState(null), "a null payload was accepted");
            Assert.IsNotEmpty(link.LocalPeerId, "peer id is unusable for host election");

            link.Disconnect();
            Assert.AreEqual(TransportState.Disconnected, link.State);
            Assert.IsFalse(link.SendState(Bytes("after")), "sending succeeded after disconnect");
        }

        [Test]
        public void TheLoopbackTransportHonoursTheContract()
        {
            (LocalLoopbackTransport host, LocalLoopbackTransport guest) = LocalLoopbackTransport.CreatePair();
            AssertHonoursTheContract(host, payload =>
            {
                // The loopback delivers to its peer, so "arriving at host"
                // means the peer sent it and host pumped.
                guest.SendState(payload);
                host.Pump();
            });
        }

        [Test]
        public void TheGameKitTransportHonoursTheContract()
        {
            transport.Connect();
            AssertHonoursTheContract(transport, match.DeliverToLocal);
        }

        // --- GameKit-specific behaviour -------------------------------------

        [Test]
        public void ConnectingReflectsTheMatchState()
        {
            match.Connected = false;
            transport.Connect();
            Assert.AreEqual(TransportState.Connecting, transport.State);

            match.ChangeConnection(true);
            Assert.AreEqual(TransportState.Connected, transport.State);
        }

        [Test]
        public void SendingReachesTheNativeMatch()
        {
            transport.Connect();
            Assert.IsTrue(transport.SendState(Bytes("hello")));
            Assert.AreEqual(1, match.Sent.Count);
            Assert.AreEqual("hello", Encoding.UTF8.GetString(match.Sent[0]));
        }

        [Test]
        public void ReceivedDataIsForwardedWithoutTheSenderId()
        {
            // Passing the id upward would let gameplay grow logic keyed on
            // GameKit's identifier format -- the leak this abstraction exists
            // to prevent.
            transport.Connect();
            byte[] seen = null;
            transport.OnStateReceived += p => seen = p;

            match.DeliverToLocal(Bytes("from peer"));
            Assert.AreEqual("from peer", Encoding.UTF8.GetString(seen));
        }

        [Test]
        public void ANativeSendThatThrowsBecomesAFailedSendNotAnException()
        {
            // The match can tear down between the state check and the call.
            // Gameplay already handles a false return; it does not handle an
            // exception from the middle of a frame.
            transport.Connect();
            match.ThrowOnSend = true;

            Assert.DoesNotThrow(() => transport.SendState(Bytes("x")));
            Assert.IsFalse(transport.SendState(Bytes("x")));
            Assert.AreEqual(TransportState.Failed, transport.State);
        }

        [Test]
        public void ADisconnectThatThrowsIsSurvivable()
        {
            transport.Connect();
            Assert.DoesNotThrow(() => transport.Disconnect());
            Assert.AreEqual(TransportState.Disconnected, transport.State);
        }

        [Test]
        public void AMissingLocalPlayerIdReadsEmptyRatherThanNull()
        {
            // HostAuthority treats an empty local id as "not host", which is
            // the safe answer before a match exists. Null would be the same
            // answer by accident rather than by decision.
            match.LocalPlayerId = null;
            Assert.AreEqual(string.Empty, transport.LocalPeerId);
            Assert.IsFalse(HostAuthority.IsHost(transport.LocalPeerId, "G:remote"));
        }

        [Test]
        public void TheRemotePeerIdDrivesTheElection()
        {
            transport.Connect();
            Assert.AreEqual("G:remote", transport.RemotePeerId);
            Assert.IsTrue(HostAuthority.IsHost(transport.LocalPeerId, transport.RemotePeerId));
        }

        [Test]
        public void NoRemoteYetMeansAnEmptyPeerIdNotAnException()
        {
            match.Remotes.Clear();
            Assert.AreEqual(string.Empty, transport.RemotePeerId);
        }

        [Test]
        public void AMissingNativeMatchIsRefusedAtConstruction()
        {
            // Better than a transport that looks fine and fails on first send.
            Assert.Throws<ArgumentNullException>(() => new GameKitTransport(null));
        }

        [Test]
        public void DisconnectingUnsubscribesFromTheNativeMatch()
        {
            transport.Connect();
            transport.Disconnect();

            byte[] seen = null;
            transport.OnStateReceived += p => seen = p;
            match.DeliverToLocal(Bytes("late"));

            Assert.IsNull(seen, "a disconnected transport still delivered native data");
        }

        // --- the swap -------------------------------------------------------

        [Test]
        public void GameplayCodeWorksAgainstEitherTransport()
        {
            // NetworkedPlayerState is gameplay-facing and was written against
            // the loopback. It is given the GameKit transport here with no
            // change of any kind.
            transport.Connect();

            var root = new GameObject("Peer");
            var jet = new GameObject("Jet").transform;
            var sync = root.AddComponent<NetworkedPlayerState>();
            sync.LocalJet = jet;
            sync.SendRateHz = 20f;
            sync.Bind(transport);

            jet.position = new Vector3(3f, 4f, 0f);
            sync.Tick(1f);

            Assert.AreEqual(1, sync.SentCount);
            Assert.AreEqual(1, match.Sent.Count, "the broadcast never reached GameKit");

            Object.DestroyImmediate(jet.gameObject);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void TheMatchmakerPicksTheTransportInOnePlace()
        {
            var root = new GameObject("Matchmaker");
            var matchmaker = root.AddComponent<NetworkMatchmaker>();

            matchmaker.CurrentMode = NetworkMatchmaker.Mode.GameKit;
            matchmaker.GameKitFactory = () => new GameKitTransport(new FakeMatch());
            INetworkTransport made = matchmaker.Begin();
            Assert.IsInstanceOf<GameKitTransport>(made);

            matchmaker.CurrentMode = NetworkMatchmaker.Mode.Loopback;
            Assert.IsInstanceOf<LocalLoopbackTransport>(matchmaker.Begin());

            Object.DestroyImmediate(root);
        }

        [Test]
        public void AMissingPluginFailsTheMatchRatherThanTheGame()
        {
            // A player with no Game Center still gets a working single-player
            // game. The spec flags the plugin itself as a maintenance risk.
            var root = new GameObject("Matchmaker");
            var matchmaker = root.AddComponent<NetworkMatchmaker>();
            matchmaker.CurrentMode = NetworkMatchmaker.Mode.GameKit;
            matchmaker.GameKitFactory = null;

            string reason = null;
            matchmaker.OnMatchFailed += r => reason = r;

            Assert.IsNull(matchmaker.Begin());
            Assert.IsNotNull(reason);

            Object.DestroyImmediate(root);
        }
    }
}
