using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;
using JetFighter.Shared;
using JetFighter.Weapon;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The cell's criterion: the gun kills a test enemy in the number of hits
    /// `damage x maxHealth` predicts.
    ///
    /// Hits are driven through HandleHit rather than through a real collision,
    /// because a real collision needs colliders, layers and a running scene --
    /// none of which the arithmetic depends on. The collision path itself is
    /// covered in PlayMode.
    /// </summary>
    public class BulletDamageTests
    {
        private GameObject bulletObject;
        private Bullet bullet;
        private GameObject enemyObject;
        private EnemyHealth enemy;
        private EnemyDef def;

        [SetUp]
        public void SetUp()
        {
            def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;

            enemyObject = new GameObject("Enemy");
            enemy = enemyObject.AddComponent<EnemyHealth>();
            enemy.Def = def;

            bulletObject = new GameObject("Bullet");
            bulletObject.AddComponent<Rigidbody>();
            bullet = bulletObject.AddComponent<Bullet>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(bulletObject);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void AHitAppliesTheShotsDamage()
        {
            bullet.Launch(2.5f, 40f, 3f);
            bullet.HandleHit(enemy);
            Assert.AreEqual(7.5f, enemy.CurrentHealth, 1e-4f);
        }

        [Test]
        public void TheHitCountMatchesTheArithmetic()
        {
            // 10 health at 2.5 damage is four hits. Not three, not five.
            int hits = 0;
            while (!enemy.IsDead && hits < 50)
            {
                bullet.Launch(2.5f, 40f, 3f);
                bullet.HandleHit(enemy);
                hits++;
            }
            Assert.AreEqual(4, hits);
        }

        [Test]
        public void ABulletIsSpentAfterOneHit()
        {
            // Without this a bullet passing through a formation on one frame
            // deals its damage to every member, and the hit count stops
            // matching the arithmetic.
            bullet.Launch(3f, 40f, 3f);
            bullet.HandleHit(enemy);
            bullet.HandleHit(enemy);
            Assert.AreEqual(7f, enemy.CurrentHealth, 1e-4f);
            Assert.IsTrue(bullet.IsSpent);
        }

        [Test]
        public void ABulletReturnsItselfExactlyOnce()
        {
            int releases = 0;
            bullet.OnFinished = _ => releases++;
            bullet.Launch(3f, 40f, 3f);
            bullet.HandleHit(enemy);
            bullet.HandleHit(enemy);
            bullet.Finish();
            Assert.AreEqual(1, releases, "a double release corrupts the pool's counts");
        }

        [Test]
        public void HittingNothingDamageableStillStopsTheBullet()
        {
            // Terrain takes no damage but does stop the shot. Not stopping
            // would let one bullet chain through a whole formation.
            int releases = 0;
            bullet.OnFinished = _ => releases++;
            bullet.Launch(3f, 40f, 3f);
            bullet.HandleHit(null);
            Assert.AreEqual(1, releases);
            Assert.IsTrue(bullet.IsSpent);
        }

        [Test]
        public void ACorpseTakesNoFurtherDamage()
        {
            enemy.ApplyDamage(100f);
            bullet.Launch(3f, 40f, 3f);
            bullet.HandleHit(enemy);
            Assert.AreEqual(0f, enemy.CurrentHealth);
            Assert.IsTrue(bullet.IsSpent, "the bullet flew on through a dead enemy");
        }

        [Test]
        public void ABulletThatHitsNothingExpiresAndReturns()
        {
            // Without a lifetime, a bullet that misses never comes back, the
            // pool drains, and the gun starts recycling live bullets in front
            // of the player.
            int releases = 0;
            bullet.OnFinished = _ => releases++;
            bullet.Launch(3f, 40f, 2f);

            for (int i = 0; i < 200; i++)
            {
                bullet.Step(1f / 60f);
            }
            Assert.AreEqual(1, releases);
            Assert.IsTrue(bullet.IsSpent);
        }

        [Test]
        public void ARelaunchedBulletGetsAFreshLifetime()
        {
            // The pool hands the same instance back. A recycled bullet still
            // carrying the previous countdown expires mid-screen.
            bullet.Launch(3f, 40f, 0.1f);
            bullet.Step(1f);
            Assert.IsTrue(bullet.IsSpent);

            bullet.Launch(3f, 40f, 5f);
            Assert.IsFalse(bullet.IsSpent);
            bullet.Step(1f);
            Assert.IsFalse(bullet.IsSpent, "the relaunch inherited the old countdown");
        }

        [Test]
        public void ASpentBulletStopsMoving()
        {
            bullet.Launch(3f, 100f, 3f);
            bullet.HandleHit(enemy);
            Vector3 restingPlace = bulletObject.transform.position;
            bullet.Step(1f);
            Assert.AreEqual(restingPlace, bulletObject.transform.position);
        }

        [Test]
        public void TravelIsFrameRateIndependent()
        {
            bullet.Launch(1f, 60f, 10f);
            for (int i = 0; i < 60; i++)
            {
                bullet.Step(1f / 60f);
            }
            float atSixty = bulletObject.transform.position.z;

            bulletObject.transform.position = Vector3.zero;
            bullet.Launch(1f, 60f, 10f);
            for (int i = 0; i < 30; i++)
            {
                bullet.Step(1f / 30f);
            }
            Assert.AreEqual(atSixty, bulletObject.transform.position.z, 1e-3f);
        }

        [Test]
        public void NegativeDamageCannotBeLaunched()
        {
            bullet.Launch(-5f, 40f, 3f);
            Assert.AreEqual(0f, bullet.Damage);
        }

        [Test]
        public void DamageIsFixedAtFireTimeNotAtImpact()
        {
            // The gun applies the player's damage multiplier when it fires. A
            // bullet that re-read the weapon asset on impact would ignore
            // every power-up.
            bullet.Launch(4f, 40f, 3f);
            Assert.AreEqual(4f, bullet.Damage);
            bullet.HandleHit(enemy);
            Assert.AreEqual(6f, enemy.CurrentHealth, 1e-4f);
        }

        [Test]
        public void DamageArrivesThroughTheInterfaceNotTheEnemyType()
        {
            IDamageable target = enemy;
            bullet.Launch(2f, 40f, 3f);
            bullet.HandleHit(target);
            Assert.AreEqual(8f, enemy.CurrentHealth, 1e-4f);
        }
    }
}
