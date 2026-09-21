namespace JetFighter.Economy
{
    /// <summary>
    /// The currencies the game knows about.
    ///
    /// Gems exist from Phase 3 even though only Coins are earned this phase.
    /// The roadmap's "architect once" principle, applied literally: Phase 5
    /// adds an IAP that grants gems, and having the value here already means
    /// that phase touches no enum, no Wallet, and no save format -- a
    /// migration of the save file is the expensive part, and it is avoided by
    /// one line written early.
    /// </summary>
    public enum CurrencyType
    {
        Coins = 0,
        Gems = 1,
    }
}
