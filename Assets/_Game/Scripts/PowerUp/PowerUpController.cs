using System.Collections.Generic;
using UnityEngine;
using JetFighter.Analytics;
using JetFighter.Player;

namespace JetFighter.PowerUp
{
    /// <summary>
    /// Applies power-ups to the player's runtime stats and expires them.
    ///
    /// Stacking is multiplicative against a hard cap (ADR-003, and the Phase 3
    /// spec's "fire rate can't exceed 3x base"). Additive stacking would make
    /// the tenth pickup worth as much as the first, and an uncapped
    /// multiplicative one trivialises the game in about a minute -- the 1/sec
    /// baseline the whole difficulty curve is calibrated against stops meaning
    /// anything.
    ///
    /// The cap is enforced on the *composed* multiplier rather than by
    /// refusing pickups. A refused pickup is invisible to the player, who
    /// collected something and saw nothing happen; a capped one still shows
    /// its pickup effect and simply stops adding.
    /// </summary>
    public class PowerUpController : MonoBehaviour
    {
        /// <summary>One applied power-up and what is left of it.</summary>
        private struct ActiveStack
        {
            public PowerUpDef Def;
            public float RemainingSeconds;
            public bool Permanent;
        }

        [SerializeField] private PlayerStatsRuntime stats;

        [Header("Caps (ADR-003)")]
        [Tooltip("Hard ceiling on the composed multiplier for any one stat. 3 means 3x base.")]
        [Min(1f)]
        [SerializeField] private float maxMultiplier = 3f;

        private readonly List<ActiveStack> active = new List<ActiveStack>();

        /// <summary>Power-ups currently in effect.</summary>
        public int ActiveCount => active.Count;

        public float MaxMultiplier
        {
            get => maxMultiplier;
            set => maxMultiplier = Mathf.Max(1f, value);
        }

        public PlayerStatsRuntime Stats
        {
            get => stats;
            set
            {
                stats = value;
                Recompose();
            }
        }

        /// <summary>
        /// The difficulty curve's input.
        ///
        /// Derived from *currently active* stacks, so difficulty falls again
        /// as power-ups expire -- ADR-003's stated consequence, and what makes
        /// the scaling power-relative rather than time-relative. A player who
        /// loses their stacks must not stay in the deep end.
        /// </summary>
        public float PlayerPowerLevel
        {
            get
            {
                float level = 0f;
                foreach (ActiveStack stack in active)
                {
                    if (stack.Def != null)
                    {
                        level += Mathf.Max(0f, stack.Def.magnitude - 1f);
                    }
                }
                return level;
            }
        }

        /// <summary>Composed multiplier for one stat, after the cap.</summary>
        public float MultiplierFor(PowerUpDef.PowerUpType type)
        {
            float product = 1f;
            foreach (ActiveStack stack in active)
            {
                if (stack.Def != null && stack.Def.type == type)
                {
                    product *= Mathf.Max(1f, stack.Def.magnitude);
                }
            }
            return Mathf.Clamp(product, 1f, maxMultiplier);
        }

        /// <summary>
        /// Collects a power-up. Returns false only when there was nothing to
        /// apply, so a pickup can tell "collected" from "ignored" without
        /// inspecting this class.
        /// </summary>
        public bool Apply(PowerUpDef def)
        {
            if (def == null || def.magnitude <= 1f)
            {
                // A magnitude at or below 1 is a mis-authored asset, not a
                // debuff -- there is no design for those, and silently
                // slowing the player would read as a bug.
                return false;
            }
            active.Add(new ActiveStack
            {
                Def = def,
                RemainingSeconds = def.duration,
                Permanent = def.IsPermanentForRun,
            });
            Recompose();

            // Reported after Recompose, so PlayerPowerLevel is the value the
            // difficulty curve will actually see -- a pickup logged with the
            // pre-pickup level makes the two datasets disagree.
            AnalyticsService.LogEvent(AnalyticsEvents.PowerUpCollected,
                new System.Collections.Generic.Dictionary<string, object>
                {
                    { AnalyticsEvents.ParamPowerUpType, def.type.ToString() },
                    { AnalyticsEvents.ParamPowerLevel, PlayerPowerLevel },
                });
            return true;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Expires timed stacks. Takes deltaTime so a stacking soak runs in
        /// simulated time rather than real minutes.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (active.Count == 0 || deltaTime <= 0f)
            {
                return;
            }

            bool expired = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ActiveStack stack = active[i];
                if (stack.Permanent)
                {
                    continue;
                }
                stack.RemainingSeconds -= deltaTime;
                if (stack.RemainingSeconds <= 0f)
                {
                    active.RemoveAt(i);
                    expired = true;
                }
                else
                {
                    active[i] = stack;
                }
            }

            if (expired)
            {
                Recompose();
            }
        }

        /// <summary>Drops every stack. Called at the start of a run.</summary>
        public void ClearAll()
        {
            active.Clear();
            Recompose();
        }

        /// <summary>
        /// Recomputes every stat from the whole active set.
        ///
        /// Rebuilt from scratch rather than applying deltas as stacks come and
        /// go. Incremental application drifts: dividing out an expired stack
        /// after the cap has clamped the product does not return the value the
        /// player started with, and the error accumulates over a run until the
        /// jet is quietly faster or slower than any pickup explains.
        /// </summary>
        private void Recompose()
        {
            if (stats == null)
            {
                return;
            }
            stats.ResetToBaseline();
            stats.ScaleSpeed(MultiplierFor(PowerUpDef.PowerUpType.Speed));
            stats.ScaleFireRate(MultiplierFor(PowerUpDef.PowerUpType.FireRate));
            stats.ScaleDamage(MultiplierFor(PowerUpDef.PowerUpType.Damage));
        }
    }
}
