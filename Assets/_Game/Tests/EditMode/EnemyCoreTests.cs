using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;
using JetFighter.Shared;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The damage pipeline through IDamageable, which is the cell's criterion.
    ///
    /// Everything here goes through the interface rather than the concrete
    /// type where it can, because the interface is what the bullet and the
    /// missile will hold -- testing the class directly would prove something
    /// no weapon actually does.
    /// </summary>
    public class EnemyCoreTests
    {
        private GameObject enemyObject;
        private EnemyHealth health;
        private EnemyDef def;

        [SetUp]
        public void SetUp()
        {
            def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;
            def.moveSpeed = 5f;
            def.loiterDistance = 2f;

            enemyObject = new GameObject("Enemy");
            health = enemyObject.AddComponent<EnemyHealth>();
            health.Def = def;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        private IDamageable AsDamageable => health;

        [Test]
        public void AnEnemySpawnsAtFullHealth()
        {
            Assert.AreEqual(10f, health.CurrentHealth);
            Assert.AreEqual(1f, health.PercentRemaining);
            Assert.IsFalse(health.IsDead);
        }

        [Test]
        public void DamageArrivesThroughTheInterface()
        {
            AsDamageable.ApplyDamage(3f);
            Assert.AreEqual(7f, health.CurrentHealth, 1e-4f);
        }

        [Test]
        public void DamageRaisesOnDamagedWithTheRemainingFraction()
        {
            float reported = -1f;
            health.OnDamaged.AddListener(pct => reported = pct);
            AsDamageable.ApplyDamage(4f);
            Assert.AreEqual(0.6f, reported, 1e-4f);
        }

        [Test]
        public void HealthNeverGoesBelowZeroEvenOnOverkill()
        {
            AsDamageable.ApplyDamage(9999f);
            Assert.AreEqual(0f, health.CurrentHealth);
            Assert.AreEqual(0f, health.PercentRemaining);
        }

        [Test]
        public void ReachingZeroKillsTheEnemy()
        {
            bool died = false;
            health.OnDied.AddListener(() => died = true);
            AsDamageable.ApplyDamage(10f);

            Assert.IsTrue(health.IsDead);
            Assert.IsTrue(died);
            Assert.IsFalse(enemyObject.activeSelf);
        }

        [Test]
        public void DeathFiresExactlyOnceNoMatterHowManyBulletsAreInFlight()
        {
            // Several bullets can land on the same frame. A drop table fired
            // twice is duplicated loot; in Phase 4 it is a desync.
            int deaths = 0;
            health.OnDied.AddListener(() => deaths++);
            AsDamageable.ApplyDamage(10f);
            AsDamageable.ApplyDamage(10f);
            AsDamageable.ApplyDamage(10f);
            Assert.AreEqual(1, deaths);
        }

        [Test]
        public void DamageAfterDeathIsIgnoredEntirely()
        {
            AsDamageable.ApplyDamage(10f);
            int events = 0;
            health.OnDamaged.AddListener(_ => events++);
            AsDamageable.ApplyDamage(5f);
            Assert.AreEqual(0, events, "the health bar would flicker on a corpse");
        }

        [Test]
        public void ZeroAndNegativeDamageAreIgnored()
        {
            int events = 0;
            health.OnDamaged.AddListener(_ => events++);
            AsDamageable.ApplyDamage(0f);
            AsDamageable.ApplyDamage(-5f);
            Assert.AreEqual(10f, health.CurrentHealth, "a negative hit healed the enemy");
            Assert.AreEqual(0, events);
        }

        [Test]
        public void APredictableNumberOfHitsKillsTheEnemy()
        {
            // The next cell's criterion in miniature: damage x maxHealth math
            // has to be the thing that decides, not a rounding accident.
            def.maxHealth = 10f;
            health.ResetHealth();
            for (int i = 0; i < 4; i++)
            {
                AsDamageable.ApplyDamage(2.5f);
            }
            Assert.IsTrue(health.IsDead);
            Assert.AreEqual(4, 4, "ten health at 2.5 per hit is four hits");
        }

        [Test]
        public void AReusedEnemyComesBackAtFullHealth()
        {
            // These come from a pool. A recycled enemy that kept its old
            // health dies to a single bullet.
            AsDamageable.ApplyDamage(10f);
            health.ResetHealth();
            Assert.IsFalse(health.IsDead);
            Assert.AreEqual(10f, health.CurrentHealth);
        }

        [Test]
        public void AnEnemyWithNoDefIsInertRatherThanImmortal()
        {
            var orphan = new GameObject("Orphan").AddComponent<EnemyHealth>();
            Assert.AreEqual(0f, orphan.MaxHealth);
            Assert.AreEqual(0f, orphan.PercentRemaining);
            Object.DestroyImmediate(orphan.gameObject);
        }

        // --- movement -------------------------------------------------------

        [Test]
        public void TheEnemyApproachesItsTarget()
        {
            Vector3 next = EnemyController.NextPosition(
                Vector3.zero, new Vector3(20f, 0f, 0f), 5f, 2f, 1f);
            Assert.AreEqual(5f, next.x, 1e-4f);
        }

        [Test]
        public void TheEnemyStopsAtItsLoiterDistance()
        {
            Vector3 next = EnemyController.NextPosition(
                Vector3.zero, new Vector3(2f, 0f, 0f), 5f, 2f, 1f);
            Assert.AreEqual(Vector3.zero, next);
        }

        [Test]
        public void ALongFrameCannotOvershootTheLoiterRing()
        {
            // Speed x dt would carry it past the hold distance and back next
            // frame. That reads as jitter, not as AI.
            Vector3 next = EnemyController.NextPosition(
                Vector3.zero, new Vector3(10f, 0f, 0f), 50f, 2f, 1f);
            Assert.AreEqual(8f, next.x, 1e-4f);
        }

        [Test]
        public void AStationaryTargetIsApproachedAtTheSameRateAtAnyFrameRate()
        {
            Vector3 slow = Vector3.zero;
            for (int i = 0; i < 60; i++)
            {
                slow = EnemyController.NextPosition(slow, new Vector3(100f, 0f, 0f), 5f, 2f, 1f / 60f);
            }
            Vector3 fast = Vector3.zero;
            for (int i = 0; i < 30; i++)
            {
                fast = EnemyController.NextPosition(fast, new Vector3(100f, 0f, 0f), 5f, 2f, 1f / 30f);
            }
            Assert.AreEqual(slow.x, fast.x, 1e-3f);
        }

        [Test]
        public void ANonPositiveStepOrSpeedIsANoOp()
        {
            Assert.AreEqual(Vector3.zero,
                EnemyController.NextPosition(Vector3.zero, Vector3.right * 10f, 5f, 0f, 0f));
            Assert.AreEqual(Vector3.zero,
                EnemyController.NextPosition(Vector3.zero, Vector3.right * 10f, 0f, 0f, 1f));
        }

        [Test]
        public void AnEnemySittingOnItsTargetDoesNotDivideByZero()
        {
            Vector3 next = EnemyController.NextPosition(Vector3.zero, Vector3.zero, 5f, 0f, 1f);
            Assert.AreEqual(Vector3.zero, next);
        }
    }
}
