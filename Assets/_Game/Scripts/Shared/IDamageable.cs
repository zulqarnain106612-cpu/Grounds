namespace JetFighter.Shared
{
    /// <summary>
    /// Anything a weapon can hurt.
    ///
    /// The whole reason this exists is that Phase 2 ships two weapon types --
    /// bullet and missile -- and Phase 4 makes enemy health host-authoritative.
    /// A bullet that knew about EnemyHealth would need changing for each of
    /// those; a bullet that knows about IDamageable needs changing for none.
    ///
    /// Deliberately not an event or a message: damage has to be synchronous,
    /// because the caller (a bullet) has to know whether it hit something in
    /// order to return itself to its pool on the same frame.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>Already dead. Callers use this to avoid spending a shot.</summary>
        bool IsDead { get; }

        /// <summary>
        /// Applies damage. Negative or zero amounts are ignored by the
        /// implementation rather than trusted -- a healing bullet is never
        /// what anyone meant.
        /// </summary>
        void ApplyDamage(float amount);
    }
}
