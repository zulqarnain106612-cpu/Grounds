using UnityEngine;
using JetFighter.Weapon;

namespace JetFighter.Player
{
    /// <summary>
    /// The player's effective stats for one run.
    ///
    /// Seeded once from the authored ScriptableObjects and never written back
    /// to them. That is this cell's whole point: a ScriptableObject edited in
    /// play mode keeps its new value in the editor after the session ends, so
    /// one speed power-up collected during testing silently becomes the
    /// project's authored baseline -- and the change arrives in git as a
    /// modified asset nobody remembers touching.
    ///
    /// Implements both seams the weapons and the flight model already hold
    /// (IFlightStats from Phase 1's controller, IPlayerStats from the Phase 2
    /// retrofit), so wiring this in changes no code in either -- which is the
    /// return on having declared them early.
    ///
    /// The write side is deliberately multiplier-based. Phase 3's power-ups
    /// stack multiplicatively against a hard cap, and absolute setters would
    /// make "3x base fire rate" a number nobody could compute once two
    /// power-ups were live.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PlayerStatsRuntime : MonoBehaviour, IFlightStats, IPlayerStats
    {
        [Header("Authored baselines (read once, never written)")]
        [SerializeField] private JetFlightConfig flightConfig;
        [SerializeField] private WeaponBase primaryWeapon;

        [Header("Caps")]
        [Tooltip("Hardest ceiling on any single multiplier. ADR-003's cap lives with the power-ups; this is the backstop.")]
        [Min(1f)]
        [SerializeField] private float absoluteMultiplierCeiling = 5f;

        private FlightStatsSnapshot baseline;
        private float speedMultiplier = 1f;
        private float fireRateMultiplier = 1f;
        private float damageMultiplier = 1f;

        /// <summary>Base values as authored. Read-only by construction.</summary>
        public IFlightStats Baseline => baseline ??= new FlightStatsSnapshot(flightConfig);

        public JetFlightConfig FlightConfig
        {
            get => flightConfig;
            set
            {
                flightConfig = value;
                baseline = new FlightStatsSnapshot(flightConfig);
            }
        }

        public WeaponBase PrimaryWeapon
        {
            get => primaryWeapon;
            set => primaryWeapon = value;
        }

        // --- IFlightStats ---------------------------------------------------

        public float MaxSpeed => Baseline.MaxSpeed * speedMultiplier;

        public float Acceleration => Baseline.Acceleration * speedMultiplier;

        public float BankAngleMax => Baseline.BankAngleMax;

        public float BankResponsiveness => Baseline.BankResponsiveness;

        // Drag is not scaled by the speed power-up on purpose. Raising the
        // speed cap and the drag together would leave the jet feeling
        // identical while the number on the HUD went up.
        public float LinearDrag => Baseline.LinearDrag;

        public float AngularDrag => Baseline.AngularDrag;

        // --- IPlayerStats ---------------------------------------------------

        public float DamageMultiplier => damageMultiplier;

        public float FireRateMultiplier => fireRateMultiplier;

        /// <summary>Current speed multiplier. 1.0 is the authored value.</summary>
        public float SpeedMultiplier => speedMultiplier;

        private void Awake()
        {
            baseline = new FlightStatsSnapshot(flightConfig);
            ResetToBaseline();
        }

        /// <summary>
        /// Clears every modifier. Called at the start of a run -- these are
        /// per-run stats, and a component that survives a scene reload would
        /// otherwise carry the last run's power-ups into the next one.
        /// </summary>
        public void ResetToBaseline()
        {
            speedMultiplier = 1f;
            fireRateMultiplier = 1f;
            damageMultiplier = 1f;
        }

        /// <summary>
        /// Multiplies the speed stat. Composed rather than assigned so two
        /// power-ups stack, and clamped so no amount of stacking can push a
        /// stat past the backstop.
        /// </summary>
        public void ScaleSpeed(float factor) =>
            speedMultiplier = Clamp(speedMultiplier * SanitizeFactor(factor));

        public void ScaleFireRate(float factor) =>
            fireRateMultiplier = Clamp(fireRateMultiplier * SanitizeFactor(factor));

        public void ScaleDamage(float factor) =>
            damageMultiplier = Clamp(damageMultiplier * SanitizeFactor(factor));

        /// <summary>
        /// Wires this component into the systems that read stats.
        ///
        /// Done here rather than by each system finding the player, so there
        /// is one place that knows the graph and one place to change when
        /// Phase 4 gives the remote player its own stats.
        /// </summary>
        public void ApplyTo(JetController jet, PrimaryGunController gun, MissileLauncher launcher)
        {
            if (jet != null)
            {
                jet.Stats = this;
                jet.ApplyConfigToBody();
            }
            if (gun != null)
            {
                gun.Stats = this;
            }
            if (launcher != null)
            {
                launcher.Stats = this;
            }
        }

        /// <summary>
        /// A factor that cannot invert or zero a stat.
        ///
        /// Drop tables and power-up assets are authored data, and a zero or
        /// negative magnitude typed into an inspector should not be able to
        /// stop the gun firing or fly the jet backwards.
        /// </summary>
        private static float SanitizeFactor(float factor)
        {
            return factor <= 0f || float.IsNaN(factor) ? 1f : factor;
        }

        private float Clamp(float multiplier)
        {
            return Mathf.Clamp(multiplier, 1f / absoluteMultiplierCeiling, absoluteMultiplierCeiling);
        }
    }
}
