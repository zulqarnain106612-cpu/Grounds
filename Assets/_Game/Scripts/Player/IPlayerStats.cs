namespace JetFighter.Player
{
    /// <summary>
    /// The player's live combat multipliers, as a seam.
    ///
    /// The cross-phase note in docs/PHASE2_TECHNICAL_SPEC.md asks that the gun
    /// read its damage and fire rate from PlayerStatsRuntime rather than from
    /// WeaponBase directly, *before* Phase 2 starts. PlayerStatsRuntime itself
    /// is Cycle 3 (`phase3/player-stats-runtime`), so taking the note
    /// literally now would mean inventing Phase 3's class inside Phase 2 --
    /// exactly the speculative design the roadmap refuses elsewhere.
    ///
    /// What the note is actually protecting against is the retrofit cost: by
    /// Cycle 3 the gun has callers, a missile launcher beside it and a
    /// power-up system arriving, and changing where damage comes from at that
    /// point touches all of them. So the *seam* lands now and the
    /// implementation lands in its own cell. PlayerStatsRuntime will implement
    /// this interface and nothing in the weapon code will change.
    ///
    /// Multipliers rather than absolute values: WeaponBase stays the source of
    /// balance (ADR-004's principle -- data, not code), and a power-up scales
    /// it. An absolute override here would make every weapon's tuning
    /// unreachable the moment one power-up applied.
    /// </summary>
    public interface IPlayerStats
    {
        /// <summary>Scales <c>WeaponBase.damage</c>. 1.0 is unmodified.</summary>
        float DamageMultiplier { get; }

        /// <summary>Scales <c>WeaponBase.fireRatePerSecond</c>. 1.0 is unmodified.</summary>
        float FireRateMultiplier { get; }
    }

    /// <summary>
    /// The identity stats: everything unmodified.
    ///
    /// A null-object rather than null checks at each call site. A gun with no
    /// stats source has to keep firing at its configured rate -- a jet that
    /// stops shooting because a power-up system has not spawned yet is a
    /// worse bug than any it would prevent.
    /// </summary>
    public sealed class DefaultPlayerStats : IPlayerStats
    {
        public static readonly DefaultPlayerStats Instance = new DefaultPlayerStats();

        private DefaultPlayerStats()
        {
        }

        public float DamageMultiplier => 1f;

        public float FireRateMultiplier => 1f;
    }
}
