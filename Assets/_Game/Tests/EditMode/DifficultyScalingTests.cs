using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The bounded-DDA guarantee.
    ///
    /// The criterion is "time-to-kill never exceeds the ceiling across a
    /// PlayerPowerLevel sweep" -- a claim about every point on the curve, not
    /// about a few. So it is swept, and swept across combinations of DPS and
    /// enemy health too, because the clamp depends on all three.
    /// </summary>
    public class DifficultyScalingTests
    {
        private DifficultyCurve curve;
        private EnemyDef basic;
        private EnemyDef heavy;

        [SetUp]
        public void SetUp()
        {
            curve = ScriptableObject.CreateInstance<DifficultyCurve>();
            curve.k = 0.5f;
            curve.timeToKillCeilingSeconds = 8f;
            curve.minimumMultiplier = 1f;

            basic = ScriptableObject.CreateInstance<EnemyDef>();
            basic.maxHealth = 10f;
            heavy = ScriptableObject.CreateInstance<EnemyDef>();
            heavy.maxHealth = 60f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(curve);
            Object.DestroyImmediate(basic);
            Object.DestroyImmediate(heavy);
        }

        [Test]
        public void TimeToKillNeverExceedsTheCeilingAcrossThePowerSweep()
        {
            // The criterion, verbatim, and the reason the curve is a pure
            // function: a scene test can only ever visit a few points.
            // Chosen so the unscaled fight is already inside the ceiling:
            // 40 health at 5 dps is 8 seconds exactly at multiplier 1.
            const float dps = 5f;
            const float health = 40f;
            for (float power = 0f; power <= 100f; power += 0.05f)
            {
                float multiplier = DifficultyManager.ComputeStatMultiplier(curve, power, dps, health);
                float ttk = DifficultyManager.TimeToKill(health, multiplier, dps);
                Assert.LessOrEqual(ttk, curve.timeToKillCeilingSeconds + 1e-3f,
                    $"unwinnable at power {power}: time-to-kill {ttk}s");
            }
        }

        [Test]
        public void ScalingNeverMakesTheFightWorseThanItAlreadyWas()
        {
            // The exact guarantee, swept over all three inputs -- the clamp
            // depends on all of them, so sweeping power alone would pass on a
            // formula that ignored the other two.
            //
            // ttk(scaled) <= max(ceiling, ttk(unscaled)). The second term is
            // the honest boundary: an enemy whose authored health already
            // outlasts the ceiling at this DPS cannot be rescued by a
            // multiplier whose floor is 1. That is a content problem, and
            // ExceedsCeilingUnscaled is how the spawner sees it.
            foreach (float dps in new[] { 0.5f, 2f, 5f, 25f, 200f })
            {
                foreach (float health in new[] { 1f, 10f, 60f, 500f })
                {
                    float unscaled = DifficultyManager.TimeToKill(health, 1f, dps);
                    float bound = Mathf.Max(curve.timeToKillCeilingSeconds, unscaled);
                    for (float power = 0f; power <= 100f; power += 0.25f)
                    {
                        float m = DifficultyManager.ComputeStatMultiplier(curve, power, dps, health);
                        Assert.LessOrEqual(DifficultyManager.TimeToKill(health, m, dps), bound + 1e-3f,
                            $"scaling made it worse: dps={dps} health={health} power={power}");
                    }
                }
            }
        }

        [Test]
        public void AWinnableFightStaysInsideTheCeilingAtEveryPowerLevel()
        {
            // The criterion as the player experiences it, for every enemy the
            // player can already kill inside the bound.
            foreach (float dps in new[] { 2f, 5f, 25f, 200f })
            {
                foreach (float health in new[] { 1f, 10f, 40f })
                {
                    if (DifficultyManager.ExceedsCeilingUnscaled(curve, dps, health))
                    {
                        continue;
                    }
                    for (float power = 0f; power <= 100f; power += 0.05f)
                    {
                        float m = DifficultyManager.ComputeStatMultiplier(curve, power, dps, health);
                        Assert.LessOrEqual(DifficultyManager.TimeToKill(health, m, dps),
                            curve.timeToKillCeilingSeconds + 1e-3f,
                            $"unwinnable: dps={dps} health={health} power={power}");
                    }
                }
            }
        }

        [Test]
        public void AnEnemyTooToughBeforeScalingIsReportedRatherThanHidden()
        {
            // 500 health at 0.5 dps is 1000 seconds before any multiplier.
            // The curve cannot fix that -- its floor is 1 -- so it says so.
            Assert.IsTrue(DifficultyManager.ExceedsCeilingUnscaled(curve, 0.5f, 500f));
            Assert.IsFalse(DifficultyManager.ExceedsCeilingUnscaled(curve, 25f, 40f));
            Assert.AreEqual(1f, DifficultyManager.ComputeStatMultiplier(curve, 50f, 0.5f, 500f), 1e-4f,
                "scaling added difficulty to a fight that was already past the ceiling");
        }

        [Test]
        public void APlayerWhoCannotDamageAnythingIsReportedToo()
        {
            Assert.IsTrue(DifficultyManager.ExceedsCeilingUnscaled(curve, 0f, 10f));
        }

        [Test]
        public void ScalingIsPowerRelativeNotTimeRelative()
        {
            // Roadmap section 4. Time-relative scaling punishes the player who
            // is surviving without getting stronger -- exactly the player
            // least able to absorb it.
            float weak = DifficultyManager.ComputeStatMultiplier(curve, 0f, 50f, 10f);
            float strong = DifficultyManager.ComputeStatMultiplier(curve, 4f, 50f, 10f);
            Assert.AreEqual(1f, weak, 1e-4f);
            Assert.Greater(strong, weak);
        }

        [Test]
        public void TheCurveFollowsTheFormulaWhileTheCeilingIsSlack()
        {
            // 1 + k * power, with plenty of headroom so the clamp is inactive.
            float m = DifficultyManager.ComputeStatMultiplier(curve, 4f, 1000f, 1f);
            Assert.AreEqual(3f, m, 1e-4f);
        }

        [Test]
        public void DifficultyFallsBackWhenPowerUpsExpire()
        {
            // ADR-003's consequence, at the difficulty end: a player who loses
            // their stacks must not stay in the deep end.
            float peak = DifficultyManager.ComputeStatMultiplier(curve, 6f, 1000f, 1f);
            float after = DifficultyManager.ComputeStatMultiplier(curve, 1f, 1000f, 1f);
            Assert.Less(after, peak);
        }

        [Test]
        public void EnemiesNeverFallBelowTheirAuthoredStats()
        {
            // A weak player should face the enemy as designed, not a weakened
            // one -- the curve is a difficulty ramp, not a rubber band in both
            // directions.
            Assert.AreEqual(1f, DifficultyManager.ComputeStatMultiplier(curve, 0f, 1000f, 1f), 1e-4f);
            Assert.AreEqual(1f, DifficultyManager.ComputeStatMultiplier(curve, -50f, 1000f, 1f), 1e-4f);
        }

        [Test]
        public void ALowDpsPlayerFacesUnscaledEnemiesRatherThanImpossibleOnes()
        {
            // With tiny DPS the ceiling multiplier is below 1. Clamping to the
            // floor keeps enemies at their authored strength instead of
            // weakening them into nothing -- and never above it.
            float m = DifficultyManager.ComputeStatMultiplier(curve, 20f, 0.01f, 500f);
            Assert.AreEqual(1f, m, 1e-4f);
        }

        [Test]
        public void ANonDamagingPlayerDoesNotGetAnUnboundedCurve()
        {
            // The one guarantee this class offers must not become conditional
            // on data it does not control.
            Assert.AreEqual(1f, DifficultyManager.ComputeStatMultiplier(curve, 50f, 0f, 10f), 1e-4f);
            Assert.AreEqual(1f, DifficultyManager.ComputeStatMultiplier(curve, 50f, 5f, 0f), 1e-4f);
        }

        [Test]
        public void RetuningKChangesTheRampWithNoCodeChange()
        {
            curve.k = 0.1f;
            float gentle = DifficultyManager.ComputeStatMultiplier(curve, 5f, 1000f, 1f);
            curve.k = 1f;
            float steep = DifficultyManager.ComputeStatMultiplier(curve, 5f, 1000f, 1f);
            Assert.AreEqual(1.5f, gentle, 1e-4f);
            Assert.AreEqual(6f, steep, 1e-4f);
        }

        [Test]
        public void RetuningTheCeilingChangesTheBound()
        {
            // Unscaled time to kill here is 40 / 5 = 8s. A ceiling only binds
            // where reaching it does not require pushing the multiplier under
            // minimumMultiplier: an enemy whose authored health already
            // outlasts the ceiling at this DPS is a content problem the curve
            // does not get to fix by weakening it, which is exactly what
            // ALowDpsPlayerFacesUnscaledEnemiesRatherThanImpossibleOnes pins
            // down. So the ceilings that move this bound are the ones above 8s.
            foreach (float ceiling in new[] { 10f, 12f, 20f })
            {
                curve.timeToKillCeilingSeconds = ceiling;
                for (float power = 0f; power <= 50f; power += 0.5f)
                {
                    float m = DifficultyManager.ComputeStatMultiplier(curve, power, 5f, 40f);
                    Assert.LessOrEqual(DifficultyManager.TimeToKill(40f, m, 5f), ceiling + 1e-3f);
                }
            }

            // And the bound genuinely tracks the ceiling rather than sitting at
            // some fixed value: the same power reaches a later cap when the
            // ceiling is raised.
            curve.timeToKillCeilingSeconds = 20f;
            float high = DifficultyManager.TimeToKill(
                40f, DifficultyManager.ComputeStatMultiplier(curve, 50f, 5f, 40f), 5f);
            curve.timeToKillCeilingSeconds = 10f;
            float low = DifficultyManager.TimeToKill(
                40f, DifficultyManager.ComputeStatMultiplier(curve, 50f, 5f, 40f), 5f);
            Assert.AreEqual(20f, high, 1e-3f);
            Assert.AreEqual(10f, low, 1e-3f);
        }

        [Test]
        public void ANullCurveIsNeutralRatherThanFatal()
        {
            Assert.AreEqual(1f, DifficultyManager.ComputeStatMultiplier(null, 50f, 5f, 10f));
            Assert.IsEmpty(DifficultyManager.GetUnlockedArchetypes(null, 10f));
        }

        // --- archetype variety ---------------------------------------------

        [Test]
        public void ArchetypesUnlockAtTheirThresholdNotPastIt()
        {
            // A designer typing 2.0 means "from 2.0". Off-by-one here is
            // invisible until someone wonders why an archetype never appears.
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = basic, powerLevelThreshold = 0f });
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = heavy, powerLevelThreshold = 2f });

            CollectionAssert.AreEquivalent(new[] { basic },
                DifficultyManager.GetUnlockedArchetypes(curve, 1.99f));
            CollectionAssert.AreEquivalent(new[] { basic, heavy },
                DifficultyManager.GetUnlockedArchetypes(curve, 2f));
        }

        [Test]
        public void VarietyGrowsWithPowerRatherThanReplacing()
        {
            // New archetypes read as "getting stronger" without bullet-sponge
            // inflation -- but the earlier ones must keep spawning, or the
            // difficulty jumps rather than broadens.
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = basic, powerLevelThreshold = 0f });
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = heavy, powerLevelThreshold = 5f });
            Assert.AreEqual(2, DifficultyManager.GetUnlockedArchetypes(curve, 9f).Count);
        }

        [Test]
        public void AnUnlockWithNoArchetypeIsSkipped()
        {
            // A cleared inspector slot must not spawn a null enemy.
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = null, powerLevelThreshold = 0f });
            Assert.IsEmpty(DifficultyManager.GetUnlockedArchetypes(curve, 10f));
        }

        [Test]
        public void NothingIsUnlockedBelowEveryThreshold()
        {
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = heavy, powerLevelThreshold = 3f });
            Assert.IsEmpty(DifficultyManager.GetUnlockedArchetypes(curve, 0f));
        }
    }
}
