using NUnit.Framework;
using UnityEngine;
using JetFighter.Weapon;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// Fire timing, driven by simulated deltaTime rather than real seconds.
    ///
    /// The criterion is 1.0s +/- 0.02s "regardless of frame rate". Waiting for
    /// real time would make the suite slow and the tolerance meaningless
    /// (a loaded CI runner would fail it), so Tick takes deltaTime and these
    /// drive a hundred simulated seconds instantly.
    /// </summary>
    public class PrimaryGunControllerTests
    {
        private GameObject gunObject;
        private GameObject prefab;
        private PrimaryGunController gun;
        private WeaponBase weapon;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Bullet");
            prefab.SetActive(false);

            weapon = ScriptableObject.CreateInstance<WeaponBase>();
            weapon.fireRatePerSecond = 1f;
            weapon.projectilePrefab = prefab;
            weapon.poolCapacity = 8;

            gunObject = new GameObject("Gun");
            gun = gunObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;
            gun.EnsurePool();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gunObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(weapon);
        }

        private void Simulate(float seconds, float step)
        {
            int steps = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < steps; i++)
            {
                gun.Tick(step);
            }
        }

        [Test]
        public void TheFirstShotIsImmediate()
        {
            gun.Tick(0f);
            Assert.AreEqual(1, gun.ShotsFired, "the player waits a full second before anything happens");
        }

        [Test]
        public void OneShotPerSecondAtSixtyFps()
        {
            Simulate(60f, 1f / 60f);
            Assert.AreEqual(61, gun.ShotsFired, 1);
        }

        [Test]
        public void TheSameRateAtThirtyFps()
        {
            // The low tier caps at 30 (QualityTierManager). A frame-counted
            // cooldown would halve the fire rate here and nothing else would
            // report it.
            Simulate(60f, 1f / 30f);
            Assert.AreEqual(61, gun.ShotsFired, 1);
        }

        [Test]
        public void TheRateHoldsAcrossAWildlyVariableFrameTime()
        {
            float elapsed = 0f;
            var random = new System.Random(1234);
            while (elapsed < 60f)
            {
                float step = 1f / 120f + (float)random.NextDouble() * (1f / 20f);
                gun.Tick(step);
                elapsed += step;
            }
            Assert.AreEqual(61, gun.ShotsFired, 1, "fire rate drifted with frame time");
        }

        [Test]
        public void AHitchOwesEveryShotItSwallowed()
        {
            // A 5 second stall must not cost the player 4 shots.
            gun.Tick(5f);
            Assert.AreEqual(5, gun.ShotsFired, 1);
        }

        [Test]
        public void TheCooldownCarriesItsRemainderRatherThanResetting()
        {
            // Resetting to the full cooldown after a late expiry drifts by up
            // to a frame per shot, which is the whole tolerance.
            Simulate(10f, 0.3f);
            Assert.AreEqual(11, gun.ShotsFired, 1);
        }

        [Test]
        public void AFasterWeaponFiresProportionallyFaster()
        {
            weapon.fireRatePerSecond = 4f;
            Simulate(10f, 1f / 60f);
            Assert.AreEqual(41, gun.ShotsFired, 1);
        }

        [Test]
        public void ThePoolStaysBoundedOverAFiveMinuteSoak()
        {
            // The criterion's soak, compressed. Nothing releases the bullets,
            // so this is the worst case the pool will ever see.
            Simulate(300f, 1f / 60f);
            Assert.AreEqual(weapon.poolCapacity, gun.Pool.Count);
            Assert.Greater(gun.ShotsFired, 290);
        }

        [Test]
        public void AGunWithNoWeaponDefIsInertRatherThanThrowing()
        {
            gun.WeaponDef = null;
            Assert.DoesNotThrow(() => gun.Tick(1f));
            Assert.AreEqual(0, gun.ShotsFired);
        }

        [Test]
        public void AWeaponWithNoPrefabStillTracksItsCadence()
        {
            // A missing prefab is a content bug, not a reason to stop the gun
            // logic -- and silently firing nothing forever would hide it.
            weapon.projectilePrefab = null;
            gun.WeaponDef = weapon;
            Simulate(5f, 1f / 60f);
            Assert.AreEqual(6, gun.ShotsFired, 1);
            Assert.IsNull(gun.Pool);
        }

        [Test]
        public void AZeroFireRateCannotDivideByZero()
        {
            weapon.fireRatePerSecond = 0f;
            Assert.AreEqual(100f, weapon.CooldownSeconds, 1e-3f);
        }

        // --- player stats seam (Phase 2 cross-phase retrofit) ---------------

        private sealed class Stats : JetFighter.Player.IPlayerStats
        {
            public float DamageMultiplier { get; set; } = 1f;
            public float FireRateMultiplier { get; set; } = 1f;
        }

        [Test]
        public void WithNoStatsSourceTheWeaponsOwnNumbersApply()
        {
            weapon.damage = 7f;
            Assert.AreEqual(weapon.CooldownSeconds, gun.EffectiveCooldownSeconds, 1e-4f);
            Assert.AreEqual(7f, gun.EffectiveDamage, 1e-4f);
        }

        [Test]
        public void AFireRateMultiplierChangesTheCadence()
        {
            gun.Stats = new Stats { FireRateMultiplier = 2f };
            Simulate(10f, 1f / 60f);
            Assert.AreEqual(21, gun.ShotsFired, 1, "the fire-rate multiplier did not reach the cooldown");
        }

        [Test]
        public void ADamageMultiplierScalesTheWeaponRatherThanReplacingIt()
        {
            // WeaponBase stays the source of balance; a power-up scales it.
            weapon.damage = 3f;
            gun.Stats = new Stats { DamageMultiplier = 2.5f };
            Assert.AreEqual(7.5f, gun.EffectiveDamage, 1e-4f);
        }

        [Test]
        public void ClearingTheStatsRestoresTheDefaultsRatherThanSilencingTheGun()
        {
            gun.Stats = new Stats { FireRateMultiplier = 4f };
            gun.Stats = null;
            Assert.AreEqual(weapon.CooldownSeconds, gun.EffectiveCooldownSeconds, 1e-4f);
            Simulate(5f, 1f / 60f);
            Assert.Greater(gun.ShotsFired, 0, "a null stats source stopped the gun firing");
        }

        [Test]
        public void AZeroFireRateMultiplierCannotDivideByZero()
        {
            gun.Stats = new Stats { FireRateMultiplier = 0f };
            Assert.IsTrue(float.IsFinite(gun.EffectiveCooldownSeconds));
        }

        [Test]
        public void ANegativeDamageMultiplierCannotHealTheTarget()
        {
            weapon.damage = 5f;
            gun.Stats = new Stats { DamageMultiplier = -3f };
            Assert.AreEqual(0f, gun.EffectiveDamage, 1e-4f);
        }
    }
}
