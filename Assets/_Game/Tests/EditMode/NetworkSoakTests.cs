using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;
using JetFighter.Network;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The soak cell's automatable half.
    ///
    /// The criterion is two physical devices completing a co-op run with no
    /// visible desync, and an interruption crashing neither client. Two
    /// devices are a capture, not an assertion -- but the *conditions* that
    /// make a device run fail are latency, jitter, loss and interruption, and
    /// all four can be driven deterministically here for far longer than
    /// anyone will hold two phones.
    ///
    /// A device run visits one arbitrary sample of those conditions. This
    /// visits them on purpose, which is why the manual half is a
    /// confirmation rather than the evidence.
    /// </summary>
    public class NetworkSoakTests
    {
        private LocalLoopbackTransport hostLink;
        private LocalLoopbackTransport guestLink;
        private GameObject hostRoot;
        private GameObject guestRoot;
        private NetworkedEnemyState hostEnemies;
        private NetworkedEnemyState guestEnemies;
        private NetworkedPlayerState hostPlayer;
        private NetworkedPlayerState guestPlayer;
        private EnemyHealth hostEnemy;
        private EnemyHealth guestEnemy;
        private Transform hostJet;
        private Transform guestRemoteJet;
        private EnemyDef def;

        [SetUp]
        public void SetUp()
        {
            (hostLink, guestLink) = LocalLoopbackTransport.CreatePair();

            def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 500f;

            hostRoot = new GameObject("Host");
            hostEnemies = hostRoot.AddComponent<NetworkedEnemyState>();
            hostEnemies.SendRateHz = 20f;
            hostEnemies.Bind(hostLink, asHost: true);
            hostPlayer = hostRoot.AddComponent<NetworkedPlayerState>();
            hostJet = new GameObject("HostJet").transform;
            hostPlayer.LocalJet = hostJet;
            hostPlayer.SendRateHz = 20f;
            hostPlayer.Bind(hostLink);

            guestRoot = new GameObject("Guest");
            guestEnemies = guestRoot.AddComponent<NetworkedEnemyState>();
            guestEnemies.Bind(guestLink, asHost: false);
            guestPlayer = guestRoot.AddComponent<NetworkedPlayerState>();
            guestRemoteJet = new GameObject("RemoteJet").transform;
            guestPlayer.RemoteJet = guestRemoteJet;
            guestPlayer.Bind(guestLink);

            hostEnemy = NewEnemy("HostEnemy");
            guestEnemy = NewEnemy("GuestEnemy");
            int id = hostEnemies.Register(hostEnemy);
            guestEnemies.Register(guestEnemy, id);
        }

        private EnemyHealth NewEnemy(string name)
        {
            var go = new GameObject(name);
            var health = go.AddComponent<EnemyHealth>();
            health.Def = def;
            return health;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(hostJet.gameObject);
            Object.DestroyImmediate(guestRemoteJet.gameObject);
            Object.DestroyImmediate(hostEnemy.gameObject);
            Object.DestroyImmediate(guestEnemy.gameObject);
            Object.DestroyImmediate(hostRoot);
            Object.DestroyImmediate(guestRoot);
            Object.DestroyImmediate(def);
        }

        private void Step(float dt = 1f / 60f)
        {
            hostPlayer.Tick(dt);
            hostEnemies.Tick(dt);
            guestLink.Pump();
            guestPlayer.Tick(dt);
            guestEnemies.Tick(dt);
            hostLink.Pump();
        }

        /// <summary>A run of the given length, with the host playing.</summary>
        private float Soak(int frames, float damagePerHit = 0.5f, int hitEvery = 30)
        {
            float worstDivergence = 0f;
            for (int i = 0; i < frames; i++)
            {
                hostJet.position = new Vector3(Mathf.Sin(i * 0.01f) * 10f, Mathf.Cos(i * 0.013f) * 6f, 0f);
                if (i % hitEvery == 0 && !hostEnemy.IsDead)
                {
                    hostEnemy.ApplyDamage(damagePerHit);
                }
                Step();
                worstDivergence = Mathf.Max(worstDivergence,
                    Mathf.Abs(hostEnemy.CurrentHealth - guestEnemy.CurrentHealth));
            }
            return worstDivergence;
        }

        [Test]
        public void AFiveMinuteRunNeverDivergesBeyondOneUnsyncedHit()
        {
            // 18000 frames is five minutes at 60fps -- longer than anyone will
            // hold two phones, and every frame is checked.
            float worst = Soak(18000);
            Assert.LessOrEqual(worst, 0.5f + 1e-3f, $"worst divergence was {worst}");
            Assert.AreEqual(hostEnemy.CurrentHealth, guestEnemy.CurrentHealth, 1e-3f);
        }

        [Test]
        public void LatencyDoesNotCauseDivergence()
        {
            // Six pumps at 60fps is roughly 100ms each way, which is a poor
            // but entirely normal peer-to-peer link.
            hostLink.LatencyPumps = 6;
            guestLink.LatencyPumps = 6;
            float worst = Soak(6000);
            Assert.LessOrEqual(worst, 0.5f + 1e-3f, $"worst divergence under latency was {worst}");
        }

        [Test]
        public void JitterAndReorderingDoNotCauseDivergence()
        {
            // Jitter is what actually reorders packets. Absolute health means
            // an out-of-order enemy packet is merely stale, not corrupting.
            hostLink.LatencyPumps = 4;
            hostLink.JitterPumps = 5;
            float worst = Soak(6000);
            Assert.LessOrEqual(worst, 0.5f + 1e-3f, $"worst divergence under jitter was {worst}");
        }

        [Test]
        public void HeavyPacketLossDoesNotCauseDivergence()
        {
            hostLink.LossRate = 0.5f;
            float worst = Soak(6000);
            Assert.Greater(hostLink.DroppedByLoss, 100);
            Assert.LessOrEqual(worst, 0.5f + 1e-3f, $"worst divergence under loss was {worst}");
        }

        [Test]
        public void EverythingAtOnceStillConverges()
        {
            // The condition a device run only reaches by bad luck.
            hostLink.LatencyPumps = 5;
            hostLink.JitterPumps = 4;
            hostLink.LossRate = 0.34f;
            guestLink.LatencyPumps = 5;
            Soak(6000);
            Step();
            Step();
            Assert.AreEqual(hostEnemy.CurrentHealth, guestEnemy.CurrentHealth, 1e-3f);
        }

        [Test]
        public void RepeatedInterruptionsCrashNeitherPeer()
        {
            // The criterion's second half. Twenty interruptions, because the
            // first one working proves only that the first one works.
            Assert.DoesNotThrow(() =>
            {
                for (int round = 0; round < 20; round++)
                {
                    Soak(200);
                    guestLink.Interrupt();
                    Soak(60);
                    guestLink.Connect();
                    Soak(120);
                }
            });
        }

        [Test]
        public void StateReconvergesAfterEveryInterruption()
        {
            for (int round = 0; round < 10; round++)
            {
                Soak(200);
                guestLink.Interrupt();
                hostEnemy.ApplyDamage(5f);
                Soak(60);
                Assert.AreNotEqual(hostEnemy.CurrentHealth, guestEnemy.CurrentHealth,
                    "the guest somehow tracked the host while disconnected");

                guestLink.Connect();
                Soak(120);
                Assert.AreEqual(hostEnemy.CurrentHealth, guestEnemy.CurrentHealth, 1e-3f,
                    $"round {round}: the guest never re-converged");
            }
        }

        [Test]
        public void ThePlayerJetKeepsTrackingAcrossInterruptions()
        {
            Soak(300);
            guestLink.Interrupt();
            Soak(120);
            guestLink.Connect();

            hostJet.position = new Vector3(12f, -4f, 0f);
            for (int i = 0; i < 300; i++)
            {
                Step();
            }
            Assert.AreEqual(12f, guestRemoteJet.position.x, 0.2f);
            Assert.AreEqual(-4f, guestRemoteJet.position.y, 0.2f);
        }

        [Test]
        public void ADisconnectedGuestStopsApplyingRatherThanDriftingOnItsOwn()
        {
            // The failure mode that looks like a working game until the
            // players compare screens.
            Soak(200);
            guestLink.Interrupt();
            int applied = guestEnemies.AppliedCount;
            Soak(300);
            Assert.AreEqual(applied, guestEnemies.AppliedCount);
        }

        [Test]
        public void MalformedTrafficDuringASoakIsSurvivable()
        {
            // A build-version mismatch mid-session produces exactly this.
            for (int i = 0; i < 200; i++)
            {
                Soak(20);
                hostLink.SendState(new byte[] { 200, 1, 2, 3 });
                hostLink.SendState(new byte[] { (byte)MessageType.EnemyState, 1 });
            }
            Assert.DoesNotThrow(() => Soak(120));
            Assert.AreEqual(hostEnemy.CurrentHealth, guestEnemy.CurrentHealth, 1e-3f);
        }

        [Test]
        public void TrafficStaysProportionalToTheSendRateOverALongRun()
        {
            // A soak is also where an accidental per-frame send shows up: at
            // 20Hz a 6000-frame run is ~2000 packets, not 6000.
            hostEnemies.SendRateHz = 20f;
            hostPlayer.SendRateHz = 20f;
            Soak(6000);
            Assert.Less(hostEnemies.SentCount + hostPlayer.SentCount, 6000,
                "traffic is closer to per-frame than to the configured rate");
        }
    }
}
