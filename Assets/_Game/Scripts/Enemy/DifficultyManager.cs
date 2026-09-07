using System.Collections.Generic;
using UnityEngine;
using JetFighter.Analytics;

namespace JetFighter.Enemy
{
    /// <summary>
    /// Bounded dynamic difficulty.
    ///
    /// Enemies scale with the player's *power*, not with elapsed time
    /// (roadmap section 4). Time-relative scaling punishes a player who is
    /// surviving without getting stronger, which is exactly the player least
    /// able to absorb it.
    ///
    /// The clamp is the part that matters. `1 + k * power` alone is unbounded,
    /// so at high power levels the enemy's health outruns the player's DPS and
    /// the run becomes mathematically unwinnable -- not hard, unwinnable, and
    /// with no feedback distinguishing the two. The ceiling turns the curve
    /// into the classic bounded-DDA shape: scale freely until time-to-kill
    /// would exceed the bound, then stop.
    ///
    /// The exact guarantee is that scaling never pushes time-to-kill past the
    /// ceiling and never past where it already was:
    /// ttk(scaled) &lt;= max(ceiling, ttk(unscaled)).
    ///
    /// The second clause is the honest boundary, not a weakening. If an
    /// enemy's authored health already outlasts the ceiling at the player's
    /// current DPS, no multiplier fixes it: the floor is 1, because dropping
    /// enemies below their authored stats would make this a rubber band in
    /// both directions and hand a struggling player weakened enemies they
    /// never earned. That case is a content or pacing problem rather than a
    /// scaling one, so ExceedsCeilingUnscaled surfaces it instead of the
    /// curve silently papering over it.
    /// </summary>
    public class DifficultyManager : MonoBehaviour
    {
        [SerializeField] private DifficultyCurve curve;

        public DifficultyCurve Curve
        {
            get => curve;
            set => curve = value;
        }

        private int highestTierReported = -1;

        /// <summary>
        /// Reports a newly reached difficulty tier, once each.
        ///
        /// Instance-level rather than inside the static curve function: the
        /// curve is called every spawn, and an event per spawn would be tens
        /// of thousands per session -- past Firebase's per-day limits and
        /// useless besides. What answers a question is the tier a player
        /// reached, once.
        ///
        /// Only ever climbs. A tier re-reported when power falls and rises
        /// again would make "reached tier 3" mean "was at tier 3 at some
        /// point, repeatedly", which no funnel can use.
        /// </summary>
        public void ReportTierIfNew(float playerPowerLevel)
        {
            if (curve == null)
            {
                return;
            }
            int tier = GetUnlockedArchetypes(curve, playerPowerLevel).Count;
            if (tier <= highestTierReported)
            {
                return;
            }
            highestTierReported = tier;
            AnalyticsService.LogEvent(AnalyticsEvents.DifficultyTierReached,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsEvents.ParamTier, tier },
                    { AnalyticsEvents.ParamPowerLevel, playerPowerLevel },
                });
        }

        /// <summary>Highest tier reported this run. Reset between runs.</summary>
        public int HighestTierReported => highestTierReported;

        /// <summary>Clears the reported tier so a new run starts from nothing.</summary>
        public void ResetTierReporting()
        {
            highestTierReported = -1;
        }

        /// <summary>
        /// Enemy stat multiplier for a power level and the player's current
        /// DPS, clamped so the fight stays winnable inside the ceiling.
        /// </summary>
        public float ComputeStatMultiplier(float playerPowerLevel, float playerDps, float baseEnemyHealth)
        {
            return ComputeStatMultiplier(curve, playerPowerLevel, playerDps, baseEnemyHealth);
        }

        /// <summary>
        /// The bounded curve, as a pure function.
        ///
        /// Static so the guarantee can be swept across the whole power range
        /// in a unit test rather than spot-checked in a scene -- "never
        /// becomes unwinnable" is a claim about every point on the curve, and
        /// a scene test can only ever visit a few.
        /// </summary>
        public static float ComputeStatMultiplier(DifficultyCurve curve,
            float playerPowerLevel, float playerDps, float baseEnemyHealth)
        {
            if (curve == null)
            {
                return 1f;
            }

            float raw = 1f + curve.k * Mathf.Max(0f, playerPowerLevel);
            float floor = Mathf.Max(0.1f, curve.minimumMultiplier);
            raw = Mathf.Max(floor, raw);

            // No DPS or no health means the ceiling cannot be evaluated. A
            // player who cannot damage anything is a broken state, and
            // silently letting the curve run unbounded there would make the
            // one guarantee this class offers conditional on data it does not
            // control.
            if (playerDps <= 0f || baseEnemyHealth <= 0f)
            {
                return floor;
            }

            // enemyEffectiveHealth / playerDps <= ceiling
            //   => multiplier <= ceiling * playerDps / baseEnemyHealth
            float ceilingMultiplier = curve.timeToKillCeilingSeconds * playerDps / baseEnemyHealth;
            return Mathf.Clamp(raw, floor, Mathf.Max(floor, ceilingMultiplier));
        }

        /// <summary>
        /// Time to kill one enemy at this multiplier. The quantity the
        /// criterion is stated in, exposed so a test asserts the actual thing
        /// rather than re-deriving it.
        /// </summary>
        public static float TimeToKill(float baseEnemyHealth, float statMultiplier, float playerDps)
        {
            if (playerDps <= 0f)
            {
                return float.PositiveInfinity;
            }
            return baseEnemyHealth * statMultiplier / playerDps;
        }

        /// <summary>
        /// Whether this enemy already breaks the ceiling before any scaling.
        ///
        /// The one case the clamp cannot rescue: the multiplier floor is 1, so
        /// an enemy whose authored health outlasts the ceiling at the player's
        /// current DPS stays that way. Surfacing it lets the spawner pick
        /// something killable instead, and makes the limit of the guarantee
        /// visible rather than a surprise found in a playtest.
        /// </summary>
        public static bool ExceedsCeilingUnscaled(DifficultyCurve curve, float playerDps, float baseEnemyHealth)
        {
            if (curve == null || baseEnemyHealth <= 0f)
            {
                return false;
            }
            if (playerDps <= 0f)
            {
                return true;
            }
            return TimeToKill(baseEnemyHealth, 1f, playerDps) > curve.timeToKillCeilingSeconds;
        }

        /// <summary>Archetypes spawnable at this power level.</summary>
        public IReadOnlyList<EnemyDef> GetUnlockedArchetypes(float playerPowerLevel)
        {
            return GetUnlockedArchetypes(curve, playerPowerLevel);
        }

        /// <summary>
        /// Variety scaling, per roadmap section 4: new archetypes read to the
        /// player as "getting stronger" without bullet-sponge inflation.
        /// </summary>
        public static IReadOnlyList<EnemyDef> GetUnlockedArchetypes(DifficultyCurve curve, float playerPowerLevel)
        {
            var unlocked = new List<EnemyDef>();
            if (curve == null)
            {
                return unlocked;
            }
            foreach (DifficultyCurve.ArchetypeUnlock unlock in curve.unlocks)
            {
                // At the threshold, not past it: a designer typing 2.0 means
                // "from 2.0", and off-by-one here is invisible until someone
                // wonders why an archetype never appears.
                if (unlock.archetype != null && playerPowerLevel >= unlock.powerLevelThreshold)
                {
                    unlocked.Add(unlock.archetype);
                }
            }
            return unlocked;
        }
    }
}
