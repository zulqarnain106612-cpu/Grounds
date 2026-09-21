namespace JetFighter.Player
{
    /// <summary>
    /// The flight numbers the jet actually flies with, as opposed to the ones
    /// its ScriptableObject was authored with.
    ///
    /// The Phase 3 criterion is that JetController reads only from
    /// PlayerStatsRuntime and that the source assets are never mutated at
    /// runtime. Both halves matter, and the second is the one that bites: a
    /// ScriptableObject edited in play mode keeps its new value in the editor
    /// after the session ends, so a speed power-up collected once silently
    /// becomes the project's authored baseline.
    ///
    /// An interface rather than a direct reference so the controller keeps
    /// working with no stats component in the scene -- which is also what
    /// lets the flight model be tested without one.
    /// </summary>
    public interface IFlightStats
    {
        float MaxSpeed { get; }

        float Acceleration { get; }

        float BankAngleMax { get; }

        float BankResponsiveness { get; }

        /// <summary>
        /// Rigidbody linear damping. Here rather than read straight off the
        /// asset so "the jet flies on effective values" has no exceptions --
        /// one field still coming from the ScriptableObject is one field a
        /// power-up cannot reach and one place the criterion does not hold.
        /// </summary>
        float LinearDrag { get; }

        float AngularDrag { get; }
    }

    /// <summary>
    /// An immutable snapshot of a JetFlightConfig.
    ///
    /// Taken once, so nothing downstream holds the asset itself and nothing
    /// can write back to it. This is what JetController uses when no
    /// PlayerStatsRuntime is present.
    /// </summary>
    public sealed class FlightStatsSnapshot : IFlightStats
    {
        public FlightStatsSnapshot(JetFlightConfig config)
        {
            MaxSpeed = config != null ? config.maxSpeed : 0f;
            Acceleration = config != null ? config.acceleration : 0f;
            BankAngleMax = config != null ? config.bankAngleMax : 0f;
            BankResponsiveness = config != null ? config.bankResponsiveness : 0f;
            LinearDrag = config != null ? config.linearDrag : 0f;
            AngularDrag = config != null ? config.angularDrag : 0f;
        }

        public float MaxSpeed { get; }

        public float Acceleration { get; }

        public float BankAngleMax { get; }

        public float BankResponsiveness { get; }

        public float LinearDrag { get; }

        public float AngularDrag { get; }
    }
}
