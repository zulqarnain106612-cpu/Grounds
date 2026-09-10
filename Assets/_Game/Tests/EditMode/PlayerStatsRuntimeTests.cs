using NUnit.Framework;
using UnityEngine;
using JetFighter.Player;
using JetFighter.Weapon;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion has two halves, and the second is the one that bites:
    /// the flight model and the gun read only effective values, and the
    /// authored ScriptableObjects are never mutated.
    ///
    /// A ScriptableObject edited in play mode keeps its new value in the
    /// editor after the session ends. One speed power-up collected during
    /// testing would silently become the project's authored baseline, and it
    /// would arrive in git as a modified asset nobody remembers touching.
    /// The spec suggests verifying that by asset diff after a play session --
    /// these assert it directly instead.
    /// </summary>
    public class PlayerStatsRuntimeTests
    {
        private GameObject playerObject;
        private PlayerStatsRuntime stats;
        private JetFlightConfig flightConfig;
        private WeaponBase weapon;
        private GameObject prefab;

        [SetUp]
        public void SetUp()
        {
            flightConfig = ScriptableObject.CreateInstance<JetFlightConfig>();
            flightConfig.maxSpeed = 10f;
            flightConfig.acceleration = 40f;
            flightConfig.linearDrag = 2f;
            flightConfig.angularDrag = 4f;
            flightConfig.bankAngleMax = 30f;
            flightConfig.bankResponsiveness = 8f;

            prefab = new GameObject("BulletPrefab");
            prefab.AddComponent<Rigidbody>().isKinematic = true;
            prefab.SetActive(false);

            weapon = ScriptableObject.CreateInstance<WeaponBase>();
            weapon.fireRatePerSecond = 1f;
            weapon.damage = 5f;
            weapon.projectilePrefab = prefab;
            weapon.poolCapacity = 4;

            playerObject = new GameObject("Player");
            stats = playerObject.AddComponent<PlayerStatsRuntime>();
            stats.FlightConfig = flightConfig;
            stats.PrimaryWeapon = weapon;
            stats.ResetToBaseline();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(flightConfig);
            Object.DestroyImmediate(weapon);
        }

        [Test]
        public void UnmodifiedStatsMatchTheAuthoredValues()
        {
            Assert.AreEqual(10f, stats.MaxSpeed, 1e-4f);
            Assert.AreEqual(40f, stats.Acceleration, 1e-4f);
            Assert.AreEqual(1f, stats.FireRateMultiplier);
            Assert.AreEqual(1f, stats.DamageMultiplier);
        }

        [Test]
        public void ScalingSpeedDoesNotTouchTheAsset()
        {
            // The whole point of the cell.
            stats.ScaleSpeed(2f);
            Assert.AreEqual(20f, stats.MaxSpeed, 1e-4f);
            Assert.AreEqual(10f, flightConfig.maxSpeed, 1e-4f,
                "the authored asset was mutated; it would stay mutated after the session");
        }

        [Test]
        public void ScalingFireRateAndDamageDoesNotTouchTheWeaponAsset()
        {
            stats.ScaleFireRate(2f);
            stats.ScaleDamage(3f);
            Assert.AreEqual(1f, weapon.fireRatePerSecond, 1e-4f);
            Assert.AreEqual(5f, weapon.damage, 1e-4f);
        }

        [Test]
        public void MultipliersStackRatherThanReplaceEachOther()
        {
            stats.ScaleFireRate(1.5f);
            stats.ScaleFireRate(2f);
            Assert.AreEqual(3f, stats.FireRateMultiplier, 1e-4f);
        }

        [Test]
        public void NoAmountOfStackingEscapesTheBackstop()
        {
            for (int i = 0; i < 50; i++)
            {
                stats.ScaleFireRate(2f);
            }
            Assert.LessOrEqual(stats.FireRateMultiplier, 5f + 1e-4f);
        }

        [Test]
        public void AZeroOrNegativeFactorCannotStopTheGunOrReverseTheJet()
        {
            // Power-up magnitudes are authored data. A zero typed into an
            // inspector should not be able to ground the player.
            stats.ScaleFireRate(0f);
            stats.ScaleSpeed(-2f);
            Assert.AreEqual(1f, stats.FireRateMultiplier, 1e-4f);
            Assert.AreEqual(10f, stats.MaxSpeed, 1e-4f);
        }

        [Test]
        public void ResettingClearsEveryModifier()
        {
            // These are per-run stats. A component surviving a scene reload
            // would otherwise carry the last run's power-ups into the next.
            stats.ScaleSpeed(2f);
            stats.ScaleFireRate(2f);
            stats.ScaleDamage(2f);
            stats.ResetToBaseline();

            Assert.AreEqual(10f, stats.MaxSpeed, 1e-4f);
            Assert.AreEqual(1f, stats.FireRateMultiplier);
            Assert.AreEqual(1f, stats.DamageMultiplier);
        }

        [Test]
        public void DragIsNotScaledBySpeed()
        {
            // Raising the cap and the drag together leaves the jet feeling
            // identical while the number on the HUD goes up.
            stats.ScaleSpeed(3f);
            Assert.AreEqual(2f, stats.LinearDrag, 1e-4f);
        }

        [Test]
        public void TheBaselineStaysReadableAfterModification()
        {
            stats.ScaleSpeed(2f);
            Assert.AreEqual(10f, stats.Baseline.MaxSpeed, 1e-4f);
        }

        // --- wiring ---------------------------------------------------------

        [Test]
        public void TheJetFliesOnEffectiveValues()
        {
            var jetObject = new GameObject("Jet");
            jetObject.AddComponent<Rigidbody>();
            var jet = jetObject.AddComponent<JetController>();
            jet.Config = flightConfig;

            stats.ApplyTo(jet, null, null);
            stats.ScaleSpeed(2f);

            Assert.AreEqual(20f, jet.Stats.MaxSpeed, 1e-4f,
                "the jet is still flying on the authored cap");
            Object.DestroyImmediate(jetObject);
        }

        [Test]
        public void TheGunFiresOnEffectiveValues()
        {
            var gunObject = new GameObject("Gun");
            var gun = gunObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;
            gun.AutoFire = false;

            stats.ApplyTo(null, gun, null);
            stats.ScaleDamage(3f);

            Assert.AreEqual(15f, gun.EffectiveDamage, 1e-4f);
            Assert.AreEqual(weapon.CooldownSeconds, gun.EffectiveCooldownSeconds, 1e-4f);

            stats.ScaleFireRate(2f);
            Assert.AreEqual(weapon.CooldownSeconds / 2f, gun.EffectiveCooldownSeconds, 1e-4f);
            Object.DestroyImmediate(gunObject);
        }

        [Test]
        public void WiringTheStatsInChangedNoWeaponOrFlightCode()
        {
            // Both seams were declared before this component existed -- one in
            // Phase 1, one in the Phase 2 retrofit. If either had to change to
            // accept it, that early declaration bought nothing.
            var gunObject = new GameObject("Gun");
            var gun = gunObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;

            Assert.IsInstanceOf<IPlayerStats>(stats);
            Assert.IsInstanceOf<IFlightStats>(stats);
            gun.Stats = stats;
            Assert.AreSame(stats, gun.Stats);
            Object.DestroyImmediate(gunObject);
        }

        [Test]
        public void AJetWithNoStatsComponentStillFlies()
        {
            // The fallback snapshot. A missing service must not ground the
            // player.
            var jetObject = new GameObject("Jet");
            jetObject.AddComponent<Rigidbody>();
            var jet = jetObject.AddComponent<JetController>();
            jet.Config = flightConfig;

            Assert.AreEqual(10f, jet.Stats.MaxSpeed, 1e-4f);
            jet.Stats = null;
            Assert.AreEqual(10f, jet.Stats.MaxSpeed, 1e-4f);
            Object.DestroyImmediate(jetObject);
        }
    }
}
