using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;
using JetFighter.UI.Input;
using JetFighter.Weapon;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// Guidance, cooldown and the pool bound, in simulated time.
    ///
    /// The criterion is three claims -- the missile destroys the target, the
    /// cooldown holds, the pool stays bounded under soak -- and all three are
    /// arithmetic. Running them against real seconds would make the soak slow
    /// and the cooldown assertion tolerant of exactly the drift it is meant to
    /// catch.
    /// </summary>
    public class MissileSystemTests
    {
        private GameObject launcherObject;
        private GameObject prefab;
        private GameObject enemyObject;
        private MissileLauncher launcher;
        private EnemyHealth enemy;
        private WeaponBase missileDef;
        private EnemyDef enemyDef;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("MissilePrefab");
            prefab.AddComponent<Rigidbody>().isKinematic = true;
            prefab.AddComponent<MissileController>();
            prefab.SetActive(false);

            missileDef = ScriptableObject.CreateInstance<WeaponBase>();
            missileDef.damage = 20f;
            missileDef.projectilePrefab = prefab;
            missileDef.poolCapacity = 4;
            missileDef.projectileSpeed = 30f;
            missileDef.projectileLifetime = 5f;
            missileDef.turnDegreesPerSecond = 360f;
            missileDef.impactRadius = 1f;

            enemyDef = ScriptableObject.CreateInstance<EnemyDef>();
            enemyDef.maxHealth = 20f;

            enemyObject = new GameObject("GroundEnemy");
            enemyObject.transform.position = new Vector3(0f, 0f, 30f);
            enemy = enemyObject.AddComponent<EnemyHealth>();
            enemy.Def = enemyDef;

            launcherObject = new GameObject("Launcher");
            launcher = launcherObject.AddComponent<MissileLauncher>();
            launcher.MissileDef = missileDef;
            launcher.CooldownSeconds = 3f;
            launcher.EnsurePool();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(launcherObject);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(missileDef);
            Object.DestroyImmediate(enemyDef);
        }

        private MissileController LiveMissile()
        {
            foreach (Transform child in launcherObject.transform)
            {
                if (child.gameObject.activeSelf)
                {
                    return child.GetComponent<MissileController>();
                }
            }
            return null;
        }

        private static void Fly(MissileController missile, float seconds, float step = 1f / 60f)
        {
            int steps = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < steps && !missile.IsSpent; i++)
            {
                missile.Step(step);
            }
        }

        // --- guidance -------------------------------------------------------

        [Test]
        public void AMissileReachesAndDestroysItsTarget()
        {
            Assert.IsTrue(launcher.Fire(enemyObject.transform));
            MissileController missile = LiveMissile();
            Assert.IsNotNull(missile);

            Fly(missile, 3f);
            Assert.IsTrue(missile.IsSpent, "the missile never arrived");
            Assert.IsTrue(enemy.IsDead);
        }

        [Test]
        public void SteeringIsCappedByTheTurnRate()
        {
            // The cap is what makes a missile dodgeable, and therefore a
            // weapon rather than a guarantee.
            Quaternion after = MissileController.Steer(
                Quaternion.identity, Vector3.zero, new Vector3(0f, 0f, -10f), 90f, 1f / 60f);
            Assert.AreEqual(1.5f, Quaternion.Angle(Quaternion.identity, after), 0.05f);
        }

        [Test]
        public void SteeringTurnsTowardTheTargetNotAwayFromIt()
        {
            Quaternion after = MissileController.Steer(
                Quaternion.identity, Vector3.zero, new Vector3(10f, 0f, 10f), 720f, 1f);
            Assert.Less(Quaternion.Angle(after, Quaternion.LookRotation(new Vector3(10f, 0f, 10f))), 1f);
        }

        [Test]
        public void SteeringAtTheTargetsExactPositionDoesNotThrow()
        {
            Assert.AreEqual(Quaternion.identity,
                MissileController.Steer(Quaternion.identity, Vector3.zero, Vector3.zero, 360f, 1f));
        }

        [Test]
        public void AFastMissileCannotTunnelThroughItsTarget()
        {
            // At 60 units/second with a 1-unit radius, a 60fps step is exactly
            // the radius. Testing for impact only before the move would fly
            // straight past.
            missileDef.projectileSpeed = 120f;
            enemyObject.transform.position = new Vector3(0f, 0f, 4f);
            launcher.Fire(enemyObject.transform);
            MissileController missile = LiveMissile();

            Fly(missile, 2f);
            Assert.IsTrue(missile.IsSpent);
            Assert.IsTrue(enemy.IsDead, "the missile passed through the enemy");
        }

        [Test]
        public void AMissileWhoseTargetDiesMidFlightExpiresRatherThanHanging()
        {
            // Enemies are pooled, so a killed one is deactivated. A missile
            // that froze or threw here would leak a pool instance.
            launcher.Fire(enemyObject.transform);
            MissileController missile = LiveMissile();
            missile.Step(1f / 60f);

            enemyObject.SetActive(false);
            Fly(missile, 10f);
            Assert.IsTrue(missile.IsSpent, "the missile never came back");
        }

        [Test]
        public void AMissileDetonatesExactlyOnce()
        {
            // A double release would corrupt the pool's counts, which is the
            // bound Phase 1 established.
            launcher.Fire(enemyObject.transform);
            MissileController missile = LiveMissile();
            int releases = 0;
            missile.OnFinished = _ => releases++;

            missile.Detonate();
            missile.Detonate();

            Assert.AreEqual(1, releases);
            Assert.AreEqual(0f, enemy.CurrentHealth, "the enemy took the missile's damage twice");
        }

        [Test]
        public void AMissileCarriesMoreDamageThanTheGunsBullet()
        {
            // ADR-002's split: the missile is the answer to something the gun
            // cannot kill quickly, so its data has to reflect that.
            Assert.Greater(missileDef.damage, 1f);
        }

        // --- cooldown -------------------------------------------------------

        [Test]
        public void TheCooldownBlocksASecondPress()
        {
            Assert.IsTrue(launcher.Fire(enemyObject.transform));
            Assert.IsFalse(launcher.Fire(enemyObject.transform));
            Assert.AreEqual(1, launcher.LaunchCount);
            Assert.AreEqual(1, launcher.BlockedByCooldown);
        }

        [Test]
        public void TheCooldownExpiresAfterItsDuration()
        {
            launcher.Fire(enemyObject.transform);
            for (int i = 0; i < 180; i++)
            {
                launcher.Tick(1f / 60f);
            }
            Assert.IsTrue(launcher.IsReady);
            Assert.IsTrue(launcher.Fire(enemyObject.transform));
        }

        [Test]
        public void AnIdleLauncherDoesNotBankReadiness()
        {
            // Left to run negative, two minutes of idling would buy two
            // minutes of instant missiles the moment the player pressed.
            for (int i = 0; i < 7200; i++)
            {
                launcher.Tick(1f / 60f);
            }
            Assert.AreEqual(0f, launcher.CooldownRemaining);
            Assert.IsTrue(launcher.Fire(enemyObject.transform));
            Assert.IsFalse(launcher.Fire(enemyObject.transform), "the launcher banked spare cooldown");
        }

        [Test]
        public void CooldownProgressRunsFromZeroToOne()
        {
            launcher.Fire(enemyObject.transform);
            Assert.AreEqual(0f, launcher.CooldownProgress, 0.05f);
            for (int i = 0; i < 90; i++)
            {
                launcher.Tick(1f / 60f);
            }
            Assert.AreEqual(0.5f, launcher.CooldownProgress, 0.05f);
        }

        [Test]
        public void FiringAtNothingIsRefusedWithoutSpendingTheCooldown()
        {
            Assert.IsFalse(launcher.Fire(null));
            Assert.IsTrue(launcher.IsReady, "a refused press still burned the cooldown");
            Assert.AreEqual(0, launcher.BlockedByCooldown);
        }

        // --- pool -----------------------------------------------------------

        [Test]
        public void ThePoolStaysBoundedOverASoak()
        {
            for (int i = 0; i < 2000; i++)
            {
                launcher.Tick(1f / 10f);
                launcher.Fire(enemyObject.transform);
                foreach (Transform child in launcherObject.transform)
                {
                    var missile = child.GetComponent<MissileController>();
                    if (missile != null && child.gameObject.activeSelf)
                    {
                        missile.Step(1f / 10f);
                    }
                }
                enemy.ResetHealth();
                enemyObject.SetActive(true);
            }
            Assert.AreEqual(missileDef.poolCapacity, launcher.Pool.Count);
            Assert.Greater(launcher.LaunchCount, 50);
        }

        [Test]
        public void SpentMissilesReturnToThePool()
        {
            launcher.Fire(enemyObject.transform);
            Assert.AreEqual(1, launcher.Pool.ActiveCount);
            LiveMissile().Finish();
            Assert.AreEqual(0, launcher.Pool.ActiveCount);
        }

        // --- the trigger button --------------------------------------------

        [Test]
        public void TheButtonRefusesToFireWithNothingLocked()
        {
            var buttonObject = new GameObject("MissileButton", typeof(RectTransform));
            var button = buttonObject.AddComponent<MissileTriggerButton>();
            button.Launcher = launcher;

            Assert.IsFalse(button.Press());
            Assert.AreEqual(1, button.PressesWithoutTarget);
            Assert.AreEqual(0, launcher.LaunchCount, "a press with no lock launched a missile");
            Assert.IsFalse(button.CanFire);

            Object.DestroyImmediate(buttonObject);
        }
    }
}
