using UnityEngine;

namespace JetFighter.Player
{
    /// <summary>
    /// Tuning for the jet's flight model. A ScriptableObject rather than
    /// inspector fields on the controller: flight feel is the highest-risk
    /// item in the project (roadmap section 3), so it has to be retunable
    /// against a device capture without touching code, and two configs have
    /// to be comparable side by side.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Jet Flight Config", fileName = "JetFlightConfig")]
    public class JetFlightConfig : ScriptableObject
    {
        [Header("Translation")]
        [Tooltip("Speed cap on the free axes, in units/second.")]
        [Min(0.01f)]
        public float maxSpeed = 12f;

        [Tooltip("How hard the jet is pushed toward its target velocity, in units/second^2.")]
        [Min(0.01f)]
        public float acceleration = 40f;

        [Tooltip("Rigidbody linear damping. This is what produces the coast-to-stop.")]
        [Min(0f)]
        public float linearDrag = 2.5f;

        [Tooltip("Rigidbody angular damping.")]
        [Min(0f)]
        public float angularDrag = 4f;

        [Header("Banking (visual only)")]
        [Tooltip("Maximum roll, in degrees, at full lateral speed.")]
        [Range(0f, 89f)]
        public float bankAngleMax = 35f;

        [Tooltip("How fast the bank converges on its target. Higher is snappier.")]
        [Min(0.01f)]
        public float bankResponsiveness = 8f;
    }
}
