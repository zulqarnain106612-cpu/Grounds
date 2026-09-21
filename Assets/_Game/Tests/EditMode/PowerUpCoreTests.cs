using NUnit.Framework;
using UnityEngine;
using JetFighter.Player;
using JetFighter.PowerUp;
using JetFighter.Weapon;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// Stacking, the hard cap, and expiry.
    ///
    /// The criterion is that a fire-rate pickup changes the fire interval
    /// within one frame and that stacking respects the cap under repeated
    /// pickups. Both are arithmetic, so both run in simulated time -- a cap
    /// test that needs a real minute of pickups is a cap test nobody runs.
    /// </summary>
    public class PowerUpCoreTests
    {
        private GameObject playerObject;
        private PlayerStatsRuntime stats;
        private PowerUpController powerUps;
        private PrimaryGunController gun;
        private JetFlightConfig flightConfig;
        private WeaponBase weapon;
        private GameObject prefab;
        private PowerUpDef fireRateUp;
        private PowerUpDef speedUp;

        [SetUp]
        public void SetUp()
        {
            flightConfig = ScriptableObject.CreateInstance<JetFlightConfig>();
            flightConfig.maxSpeed = 10f;
            flightConfig.acceleration = 40f;

            prefab = new GameObject("BulletPrefab");
            prefab.AddComponent<Rigidbody>().isKinematic = true;
            prefab.SetActive(false);

            weapon = ScriptableObject.CreateInstance<WeaponBase>();
            weapon.fireRatePerSecond = 1f;
            weapon.damage = 5f;
            weapon.projectilePrefab = prefab;
            weapon.poolCapacity = 4;

            fireRateUp = ScriptableObject.CreateInstance<PowerUpDef>();
            fireRateUp.type = PowerUpDef.PowerUpType.FireRate;
            fireRateUp.magnitude = 1.5f;
            fireRateUp.duration = 10f;

            speedUp = ScriptableObject.CreateInstance<PowerUpDef>();
            speedUp.type = PowerUpDef.PowerUpType.Speed;
            speedUp.magnitude = 2f;
            speedUp.duration = 0f;   // permanent for the run, per ADR-003

            playerObject = new GameObject("Player");
            stats = playerObject.AddComponent<PlayerStatsRuntime>();
            stats.FlightConfig = flightConfig;
            stats.PrimaryWeapon = weapon;
            stats.ResetToBaseline();

            gun = playerObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;
            gun.AutoFire = false;
            gun.Stats = stats;

            powerUps = playerObject.AddComponent<PowerUpController>();
            powerUps.MaxMultiplier = 3f;
            powerUps.Stats = stats;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(prefab);
            foreach (var asset in new Object[] { flightConfig, weapon, fireRateUp, speedUp })
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AFireRatePickupChangesTheFireIntervalImmediately()
        {
            // The criterion: "within one frame". Nothing here waits.
            float before = gun.EffectiveCooldownSeconds;
            Assert.IsTrue(powerUps.Apply(fireRateUp));
            Assert.Less(gun.EffectiveCooldownSeconds, before);
            Assert.AreEqual(before / 1.5f, gun.EffectiveCooldownSeconds, 1e-4f);
        }

        [Test]
        public void StackingIsMultiplicativeNotAdditive()
        {
            // Additive stacking makes the tenth pickup worth as much as the
            // first, which is the opposite of how a power fantasy should read.
            powerUps.Apply(fireRateUp);
            powerUps.Apply(fireRateUp);
            Assert.AreEqual(2.25f, powerUps.MultiplierFor(PowerUpDef.PowerUpType.FireRate), 1e-4f);
        }

        [Test]
        public void StackingNeverExceedsTheHardCap()
        {
            // ADR-003 and the spec's "fire rate can't exceed 3x base". Without
            // it the 1/sec baseline the difficulty curve is calibrated against
            // stops meaning anything within a minute.
            for (int i = 0; i < 40; i++)
            {
                powerUps.Apply(fireRateUp);
            }
            Assert.AreEqual(3f, powerUps.MultiplierFor(PowerUpDef.PowerUpType.FireRate), 1e-4f);
            Assert.AreEqual(3f, stats.FireRateMultiplier, 1e-4f);
        }

        [Test]
        public void ACappedPickupIsStillCollected()
        {
            // A refused pickup is invisible: the player collected something
            // and saw nothing happen.
            for (int i = 0; i < 40; i++)
            {
                powerUps.Apply(fireRateUp);
            }
            Assert.IsTrue(powerUps.Apply(fireRateUp));
        }

        [Test]
        public void DifferentStatsCapIndependently()
        {
            for (int i = 0; i < 10; i++)
            {
                powerUps.Apply(fireRateUp);
            }
            powerUps.Apply(speedUp);
            Assert.AreEqual(3f, powerUps.MultiplierFor(PowerUpDef.PowerUpType.FireRate), 1e-4f);
            Assert.AreEqual(2f, powerUps.MultiplierFor(PowerUpDef.PowerUpType.Speed), 1e-4f);
        }

        [Test]
        public void ATimedPowerUpExpires()
        {
            powerUps.Apply(fireRateUp);
            Assert.AreEqual(1.5f, stats.FireRateMultiplier, 1e-4f);

            for (int i = 0; i < 660; i++)
            {
                powerUps.Tick(1f / 60f);
            }
            Assert.AreEqual(0, powerUps.ActiveCount);
            Assert.AreEqual(1f, stats.FireRateMultiplier, 1e-4f);
        }

        [Test]
        public void APermanentPowerUpNeverExpires()
        {
            // ADR-003's sentinel: duration 0 is permanent for the run.
            powerUps.Apply(speedUp);
            for (int i = 0; i < 36000; i++)
            {
                powerUps.Tick(1f / 60f);
            }
            Assert.AreEqual(1, powerUps.ActiveCount);
            Assert.AreEqual(20f, stats.MaxSpeed, 1e-4f);
        }

        [Test]
        public void ExpiryReturnsTheExactBaselineNotAnApproximationOfIt()
        {
            // Applying and un-applying deltas drifts: dividing out a stack
            // after the cap clamped the product does not return where the
            // player started, and the error accumulates over a run.
            for (int cycle = 0; cycle < 30; cycle++)
            {
                for (int i = 0; i < 6; i++)
                {
                    powerUps.Apply(fireRateUp);
                }
                for (int i = 0; i < 660; i++)
                {
                    powerUps.Tick(1f / 60f);
                }
            }
            Assert.AreEqual(0, powerUps.ActiveCount);
            Assert.AreEqual(1f, stats.FireRateMultiplier, 1e-6f, "the multiplier drifted over 30 cycles");
        }

        [Test]
        public void OnlyTheExpiredStackIsRemoved()
        {
            var shortLived = ScriptableObject.CreateInstance<PowerUpDef>();
            shortLived.type = PowerUpDef.PowerUpType.FireRate;
            shortLived.magnitude = 2f;
            shortLived.duration = 1f;

            powerUps.Apply(fireRateUp);   // 10s
            powerUps.Apply(shortLived);   // 1s
            for (int i = 0; i < 120; i++)
            {
                powerUps.Tick(1f / 60f);
            }
            Assert.AreEqual(1, powerUps.ActiveCount);
            Assert.AreEqual(1.5f, powerUps.MultiplierFor(PowerUpDef.PowerUpType.FireRate), 1e-4f);
            Object.DestroyImmediate(shortLived);
        }

        [Test]
        public void PowerLevelFallsBackAsStacksExpire()
        {
            // ADR-003's stated consequence, and what makes the difficulty
            // scaling power-relative rather than time-relative: a player who
            // loses their stacks must not stay in the deep end.
            powerUps.Apply(fireRateUp);
            float peak = powerUps.PlayerPowerLevel;
            Assert.Greater(peak, 0f);

            for (int i = 0; i < 660; i++)
            {
                powerUps.Tick(1f / 60f);
            }
            Assert.AreEqual(0f, powerUps.PlayerPowerLevel, 1e-4f);
        }

        [Test]
        public void PermanentStacksKeepThePowerLevelUp()
        {
            powerUps.Apply(speedUp);
            for (int i = 0; i < 3600; i++)
            {
                powerUps.Tick(1f / 60f);
            }
            Assert.Greater(powerUps.PlayerPowerLevel, 0f);
        }

        [Test]
        public void AMisAuthoredPowerUpIsIgnoredRatherThanDebuffing()
        {
            var broken = ScriptableObject.CreateInstance<PowerUpDef>();
            broken.type = PowerUpDef.PowerUpType.Speed;
            broken.magnitude = 0.5f;

            Assert.IsFalse(powerUps.Apply(broken));
            Assert.AreEqual(10f, stats.MaxSpeed, 1e-4f, "a mis-authored asset slowed the player");
            Object.DestroyImmediate(broken);
        }

        [Test]
        public void ANullPowerUpIsIgnored()
        {
            Assert.IsFalse(powerUps.Apply(null));
            Assert.AreEqual(0, powerUps.ActiveCount);
        }

        [Test]
        public void ClearingDropsEveryStackIncludingPermanentOnes()
        {
            powerUps.Apply(fireRateUp);
            powerUps.Apply(speedUp);
            powerUps.ClearAll();

            Assert.AreEqual(0, powerUps.ActiveCount);
            Assert.AreEqual(1f, stats.FireRateMultiplier, 1e-4f);
            Assert.AreEqual(10f, stats.MaxSpeed, 1e-4f);
        }

        // --- the pickup -----------------------------------------------------

        [Test]
        public void CollectingAPickupAppliesItExactlyOnce()
        {
            var pickupObject = new GameObject("Pickup");
            pickupObject.AddComponent<SphereCollider>().isTrigger = true;
            var pickup = pickupObject.AddComponent<PowerUpPickup>();
            pickup.Arm(fireRateUp, 15f);

            Assert.IsTrue(pickup.TryCollect(powerUps));
            Assert.IsFalse(pickup.TryCollect(powerUps), "the pickup was collected twice");
            Assert.AreEqual(1, powerUps.ActiveCount);
            Assert.IsFalse(pickupObject.activeSelf);

            Object.DestroyImmediate(pickupObject);
        }

        [Test]
        public void AnUncollectedPickupExpires()
        {
            var pickupObject = new GameObject("Pickup");
            pickupObject.AddComponent<SphereCollider>().isTrigger = true;
            var pickup = pickupObject.AddComponent<PowerUpPickup>();

            int returns = 0;
            pickup.OnCollected = _ => returns++;
            pickup.Arm(fireRateUp, 2f);

            for (int i = 0; i < 300; i++)
            {
                pickup.Step(1f / 60f);
            }
            Assert.AreEqual(1, returns);
            Assert.IsTrue(pickup.IsCollected);

            Object.DestroyImmediate(pickupObject);
        }

        [Test]
        public void ARearmedPickupGetsAFreshLifetime()
        {
            var pickupObject = new GameObject("Pickup");
            pickupObject.AddComponent<SphereCollider>().isTrigger = true;
            var pickup = pickupObject.AddComponent<PowerUpPickup>();

            pickup.Arm(fireRateUp, 0.1f);
            pickup.Step(1f);
            Assert.IsTrue(pickup.IsCollected);

            pickup.Arm(fireRateUp, 10f);
            pickup.Step(1f);
            Assert.IsFalse(pickup.IsCollected, "the re-arm inherited the old countdown");

            Object.DestroyImmediate(pickupObject);
        }

        [Test]
        public void APickupWithNoCollectorIsNotConsumed()
        {
            var pickupObject = new GameObject("Pickup");
            pickupObject.AddComponent<SphereCollider>().isTrigger = true;
            var pickup = pickupObject.AddComponent<PowerUpPickup>();
            pickup.Arm(fireRateUp, 15f);

            Assert.IsFalse(pickup.TryCollect(null));
            Assert.IsFalse(pickup.IsCollected, "an enemy flying through removed the pickup");

            Object.DestroyImmediate(pickupObject);
        }
    }
}
