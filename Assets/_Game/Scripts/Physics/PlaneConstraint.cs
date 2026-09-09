using UnityEngine;

namespace JetFighter.Physics
{
    /// <summary>
    /// Clamps one world axis after the physics solve, so a fully simulated
    /// Rigidbody can never leave its plane.
    ///
    /// A post-solve correction rather than a ConfigurableJoint: joints resolve
    /// through the solver alongside every other constraint, which introduces
    /// jitter and fights the arcade feel the flight model was tuned for
    /// (roadmap section 3, point 2). Clamping after the solve is exact and
    /// costs one assignment.
    ///
    /// Z is the locked axis (ADR-001): X and Y stay free and physics-driven.
    ///
    /// The execution order is the whole point. JetController defaults to 0, so
    /// -- ordering after it -- this runs once JetController has added its
    /// forces for the tick, and Unity applies the corrected velocity to the
    /// next integration step. Reversing the two would let a tick's force
    /// escape the plane before anything clamped it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Rigidbody))]
    public class PlaneConstraint : MonoBehaviour
    {
        public enum Axis
        {
            X = 0,
            Y = 1,
            Z = 2
        }

        [Tooltip("The axis the body may not move along. Z per ADR-001.")]
        [SerializeField] private Axis lockedAxis = Axis.Z;

        [Tooltip("Capture the plane from the spawn position instead of the value below.")]
        [SerializeField] private bool captureFromSpawn = true;

        [Tooltip("World coordinate the locked axis is held at when not captured from spawn.")]
        [SerializeField] private float lockedValue;

        private Rigidbody body;

        public Axis LockedAxis
        {
            get => lockedAxis;
            set => lockedAxis = value;
        }

        /// <summary>The world coordinate the locked axis is held at.</summary>
        public float LockedValue => lockedValue;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (captureFromSpawn)
            {
                lockedValue = Component(transform.position, lockedAxis);
            }
        }

        private void FixedUpdate()
        {
            if (body == null)
            {
                return;
            }
            Apply();
        }

        /// <summary>
        /// Snaps the locked axis back to its plane and removes the velocity
        /// component that would carry the body off it again.
        ///
        /// Both halves are required. Clamping position alone leaves the
        /// velocity intact, so the body fights the clamp every tick -- the
        /// visible symptom is jitter, and the invisible one is a physics body
        /// carrying momentum that never resolves. Zeroing velocity alone lets
        /// any position set by another system stay off-plane forever.
        /// </summary>
        public void Apply()
        {
            body.position = WithComponent(body.position, lockedAxis, lockedValue);
            body.linearVelocity = WithComponent(body.linearVelocity, lockedAxis, 0f);
            CancelPendingForce();
        }

        /// <summary>
        /// Removes the part of this tick's accumulated force that points along
        /// the locked axis, before the solver integrates it.
        ///
        /// Position and velocity alone are not enough. This runs inside
        /// FixedUpdate, so the solve is still ahead of it: any force a system
        /// ordered earlier added this tick -- JetController's, when the locked
        /// axis is one the stick drives -- is integrated after the clamp and
        /// puts the body back off-plane by a whole tick of acceleration. The
        /// clamp then looks like it is holding to within one tick of drift
        /// rather than holding exactly, which is the difference between the
        /// soak's epsilon and a visible wobble.
        ///
        /// Z, the ADR-001 default, never showed it: nothing pushes along Z.
        /// </summary>
        private void CancelPendingForce()
        {
            float pending = Component(body.GetAccumulatedForce(), lockedAxis);
            if (pending == 0f)
            {
                return;
            }
            body.AddForce(WithComponent(Vector3.zero, lockedAxis, -pending), ForceMode.Force);
        }

        /// <summary>Reads one component of a vector by axis.</summary>
        public static float Component(Vector3 vector, Axis axis)
        {
            return vector[(int)axis];
        }

        /// <summary>Returns <paramref name="vector"/> with one component replaced.</summary>
        public static Vector3 WithComponent(Vector3 vector, Axis axis, float value)
        {
            vector[(int)axis] = value;
            return vector;
        }

        /// <summary>
        /// How far off the plane a position is. Exposed so a soak test can
        /// assert an epsilon rather than re-deriving the axis arithmetic, and
        /// so a tuning HUD can show the drift directly.
        /// </summary>
        public float DeviationFrom(Vector3 position)
        {
            return Mathf.Abs(Component(position, lockedAxis) - lockedValue);
        }
    }
}
