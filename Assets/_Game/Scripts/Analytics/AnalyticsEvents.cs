namespace JetFighter.Analytics
{
    /// <summary>
    /// Every event name and parameter key, in one place.
    ///
    /// Constants rather than string literals at the call sites, because an
    /// analytics typo is uniquely expensive: it does not fail, it does not
    /// warn, it produces a dashboard that looks complete and is missing a
    /// funnel step. The cost is discovered weeks later when someone tries to
    /// answer a question with the data and cannot.
    ///
    /// Firebase also silently drops events with names over 40 characters and
    /// parameters over 40, and lowercases nothing for you -- so the names are
    /// validated rather than trusted.
    /// </summary>
    public static class AnalyticsEvents
    {
        // --- events ---------------------------------------------------------

        public const string RunStart = "run_start";
        public const string RunEnd = "run_end";
        public const string DeathCause = "death_cause";
        public const string PowerUpCollected = "powerup_collected";
        public const string IapPurchase = "iap_purchase";
        public const string AdWatched = "ad_watched";
        public const string DifficultyTierReached = "difficulty_tier_reached";

        /// <summary>The event set the Phase 6 spec lists. The acceptance criterion is about these.</summary>
        public static readonly string[] All =
        {
            RunStart, RunEnd, DeathCause, PowerUpCollected,
            IapPurchase, AdWatched, DifficultyTierReached,
        };

        // --- parameter keys -------------------------------------------------

        public const string ParamDurationSeconds = "duration_seconds";
        public const string ParamScore = "score";
        public const string ParamCause = "cause";
        public const string ParamPowerUpType = "powerup_type";
        public const string ParamProductId = "product_id";
        public const string ParamGemsGranted = "gems_granted";
        public const string ParamAdOutcome = "ad_outcome";
        public const string ParamPowerLevel = "power_level";
        public const string ParamTier = "tier";
        public const string ParamIsCoop = "is_coop";

        /// <summary>Firebase's hard limits. Exceeding either drops the event silently.</summary>
        public const int MaxNameLength = 40;
        public const int MaxParametersPerEvent = 25;
    }
}
