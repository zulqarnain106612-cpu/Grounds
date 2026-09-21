using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;
using JetFighter.Network;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion: both instances show identical enemy health at all
    /// times, because the guest never simulates independently.
    ///
    /// "At all times" is the part worth testing properly -- a check after a
    /// settled sync passes on a design that diverges for half a second every
    /// time, which is precisely how long a player notices.
    /// </summary>
    public class HostAuthorityTests
    {
        private LocalLoopbackTransport hostLink;
        private LocalLoopbackTransport guestLink;
        private GameObject hostRoot;
        private GameObject guestRoot;
        private NetworkedEnemyState hostState;
        private NetworkedEnemyState guestState;
        private EnemyHealth hostEnemy;
        private EnemyHealth guestEnemy;
        private EnemyDef def;

        [SetUp]
        public void SetUp()
        {
            (hostLink, guestLink) = LocalLoopbackTransport.CreatePair();

            def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 100f;

            hostRoot = new GameObject("Host");
            hostState = hostRoot.AddComponent<NetworkedEnemyState>();
            hostState.SendRateHz = 20f;
            hostState.Bind(hostLink, asHost: true);

            guestRoot = new GameObject("Guest");
            guestState = guestRoot.AddComponent<NetworkedEnemyState>();
            guestState.Bind(guestLink, asHost: false);

            hostEnemy = NewEnemy("HostEnemy");
            guestEnemy = NewEnemy("GuestEnemy");
            int id = hostState.Register(hostEnemy);
            guestState.Register(guestEnemy, id);
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
            Object.DestroyImmediate(hostEnemy.gameObject);
            Object.DestroyImmediate(guestEnemy.gameObject);
            Object.DestroyImmediate(hostRoot);
            Object.DestroyImmediate(guestRoot);
            Object.DestroyImmediate(def);
        }

        private void Run(float seconds, float step = 1f / 60f)
        {
            int steps = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < steps; i++)
            {
                hostState.Tick(step);
                guestLink.Pump();
                guestState.Tick(step);
                hostLink.Pump();
            }
        }

        // --- election -------------------------------------------------------

        [Test]
        public void ExactlyOnePeerIsHost()
        {
            // Both claiming host is a desync with no symptom until the healths
            // diverge; both declining is merely a dead match.
            Assert.IsTrue(HostAuthority.IsHost("alpha", "beta"));
            Assert.IsFalse(HostAuthority.IsHost("beta", "alpha"));
        }

        [Test]
        public void TheElectionNeedsNoMessages()
        {
            // Both peers compute the same answer from the same two ids, so the
            // answer exists before the first packet does.
            Assert.AreNotEqual(HostAuthority.IsHost("a", "b"), HostAuthority.IsHost("b", "a"));
        }

        [Test]
        public void TheElectionIsOrdinalNotCultureAware()
        {
            // The same two ids must resolve identically on every device, and
            // culture-aware ordering of the same strings genuinely differs
            // between locales.
            Assert.IsTrue(HostAuthority.IsHost("Z-peer", "a-peer"));
        }

        [Test]
        public void ASoloSessionHosts()
        {
            // Otherwise enemies never spawn while waiting for a peer.
            Assert.IsTrue(HostAuthority.IsHost("solo", null));
            Assert.IsTrue(HostAuthority.IsHost("solo", ""));
        }

        [Test]
        public void IdenticalIdsProduceNoHostRatherThanTwo()
        {
            Assert.IsFalse(HostAuthority.IsHost("same", "same"));
            Assert.IsFalse(HostAuthority.IsGuest("same", "same"));
        }

        [Test]
        public void HostAndGuestAreComplementary()
        {
            Assert.IsTrue(HostAuthority.IsHost("a", "b"));
            Assert.IsTrue(HostAuthority.IsGuest("b", "a"));
            Assert.IsFalse(HostAuthority.IsGuest("a", "b"));
        }

        // --- convergence ----------------------------------------------------

        [Test]
        public void TheGuestFollowsTheHostsHealth()
        {
            hostEnemy.ApplyDamage(30f);
            Run(0.2f);
            Assert.AreEqual(70f, guestEnemy.CurrentHealth, 1e-3f);
        }

        [Test]
        public void HealthNeverDivergesAcrossAWholeEngagement()
        {
            // "At all times", checked every tick rather than after settling.
            float worst = 0f;
            for (int i = 0; i < 600; i++)
            {
                if (i % 20 == 0 && !hostEnemy.IsDead)
                {
                    hostEnemy.ApplyDamage(3f);
                }
                Run(1f / 60f, 1f / 60f);
                worst = Mathf.Max(worst, Mathf.Abs(hostEnemy.CurrentHealth - guestEnemy.CurrentHealth));
            }
            Assert.LessOrEqual(worst, 3f,
                $"health diverged by {worst}, more than a single unsynced hit");
            Assert.AreEqual(hostEnemy.CurrentHealth, guestEnemy.CurrentHealth, 1e-3f);
        }

        [Test]
        public void HealthIsSentAbsoluteSoLossSelfHeals()
        {
            // A lost delta leaves the guest permanently wrong by that much,
            // and nothing later corrects it.
            hostLink.LossRate = 0.5f;
            hostEnemy.ApplyDamage(40f);
            Run(2f);
            Assert.AreEqual(60f, guestEnemy.CurrentHealth, 1e-3f);
            Assert.Greater(hostLink.DroppedByLoss, 0);
        }

        [Test]
        public void ADeathOnTheHostDeactivatesTheGuestsEnemy()
        {
            hostEnemy.ApplyDamage(1000f);
            Run(0.3f);
            Assert.IsFalse(guestEnemy.gameObject.activeSelf);
        }

        [Test]
        public void TheGuestFollowsTheHostUpwardToo()
        {
            // A host that revived or rescaled an enemy is still the
            // authority; a guest refusing to follow upward is the divergence
            // this exists to stop.
            hostEnemy.ApplyDamage(50f);
            Run(0.2f);
            Assert.AreEqual(50f, guestEnemy.CurrentHealth, 1e-3f);

            hostEnemy.ResetHealth();
            Run(0.2f);
            Assert.AreEqual(100f, guestEnemy.CurrentHealth, 1e-3f);
        }

        // --- the guest has no independent opinion ---------------------------

        [Test]
        public void TheGuestNeverBroadcastsEnemyState()
        {
            // Two peers overwriting each other is the exact failure this
            // design exists to make impossible.
            guestEnemy.ApplyDamage(10f);
            guestState.BroadcastAll();
            Run(0.5f);

            Assert.AreEqual(0, guestState.SentCount);
            Assert.AreEqual(100f, hostEnemy.CurrentHealth, 1e-3f,
                "guest state reached the host");
        }

        [Test]
        public void LocalGuestDamageIsOverwrittenByTheNextPacket()
        {
            // The guest's bullets still hit; the host still owns the result.
            guestEnemy.ApplyDamage(80f);
            Assert.AreEqual(20f, guestEnemy.CurrentHealth, 1e-3f);

            Run(0.2f);
            Assert.AreEqual(100f, guestEnemy.CurrentHealth, 1e-3f,
                "the guest kept its own damage instead of the host's truth");
        }

        [Test]
        public void TheHostIgnoresIncomingEnemyState()
        {
            byte[] forged = NetworkedEnemyState.Encode(new EnemyStatePayload
            {
                enemyId = 1, position = Vector3.zero,
                currentHealth = 1f, maxHealth = 100f, alive = true,
            });
            guestLink.SendState(forged);
            hostLink.Pump();

            Assert.AreEqual(100f, hostEnemy.CurrentHealth, 1e-3f);
            Assert.AreEqual(0, hostState.AppliedCount);
        }

        [Test]
        public void AppliedHealthDoesNotRollADrop()
        {
            // Only the host rolls: a guest rolling its own would produce
            // different loot from the same kill.
            int drops = 0;
            guestEnemy.OnDropped.AddListener(_ => drops++);
            guestState.Apply(new EnemyStatePayload
            {
                enemyId = 1, position = Vector3.zero,
                currentHealth = 0f, maxHealth = 100f, alive = false,
            });
            Assert.AreEqual(0, drops);
        }

        // --- robustness -----------------------------------------------------

        [Test]
        public void APacketForAnUnknownEnemyIsCountedRatherThanIgnoredSilently()
        {
            // A rising count means spawn replication is behind, which would
            // otherwise show up only as enemies appearing late.
            guestState.Apply(new EnemyStatePayload
            {
                enemyId = 999, position = Vector3.zero,
                currentHealth = 5f, maxHealth = 10f, alive = true,
            });
            Assert.AreEqual(1, guestState.UnknownEnemyPackets);
        }

        [Test]
        public void ANaNHealthPacketIsRejected()
        {
            byte[] poison = NetworkedEnemyState.Encode(new EnemyStatePayload
            {
                enemyId = 1, position = Vector3.zero,
                currentHealth = float.NaN, maxHealth = 100f, alive = true,
            });
            Assert.IsFalse(NetworkedEnemyState.TryDecode(poison, out _));
        }

        [Test]
        public void AnInvalidEnemyIdIsRejected()
        {
            byte[] bad = NetworkedEnemyState.Encode(new EnemyStatePayload
            {
                enemyId = 0, position = Vector3.zero,
                currentHealth = 5f, maxHealth = 10f, alive = true,
            });
            Assert.IsFalse(NetworkedEnemyState.TryDecode(bad, out _));
        }

        [Test]
        public void ATruncatedPacketIsRejected()
        {
            Assert.IsFalse(NetworkedEnemyState.TryDecode(
                new byte[] { (byte)MessageType.EnemyState, 1, 2, 3 }, out _));
        }

        [Test]
        public void AnInterruptionDoesNotCrashEitherPeer()
        {
            hostEnemy.ApplyDamage(25f);
            guestLink.Interrupt();
            Assert.DoesNotThrow(() => Run(1f));

            guestLink.Connect();
            Run(0.5f);
            Assert.AreEqual(75f, guestEnemy.CurrentHealth, 1e-3f,
                "the guest did not re-converge after reconnecting");
        }
    }
}
