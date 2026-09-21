using UnityEngine;

namespace JetFighter.Player
{
    /// <summary>
    /// The jet's flight model: force toward a target velocity, plus a visual
    /// bank that leans into lateral motion.
    ///
    /// Phase 1 branch 2 deliberately runs this <b>unconstrained</b>. The plane
    /// lock arrives in its own branch (PlaneConstraint), so inertia and
    /// banking can be judged on their own before a post-solve correction is
    /// layered on top of them -- otherwise a bad feel has two possible causes
    /// and neither can be ruled out. See docs/PHASE1_TECHNICAL_SPEC.md
    /// section 3.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class JetController : MonoBehaviour
    {
        [SerializeField] private JetFlightConfig config;

        [Tooltip("Child transform holding the mesh. Banking rotates this, never the Rigidbody.")]
        [SerializeField] private Transform visual;

        /// <summary>
        /// Written by PlayerInputRouter. X is left/right, Y is up/down, both
        /// in the -1..1 range; longer vectors are clamped to the unit circle so
        /// a diagonal is not faster than an axis push.
        /// </summary>
        public Vector2 inputVector;

        private Rigidbody body;
        private float bankAngle;
        private IFlightStats stats;

        /// <summary>
        /// The numbers the jet flies with. Defaults to an immutable snapshot
        /// of the config; PlayerStatsRuntime replaces it so power-ups can
        /// change flight without anything writing to the asset. Assigning null
        /// falls back to the snapshot rather than grounding the jet.
        /// </summary>
        public IFlightStats Stats
        {
            get => stats ??= new FlightStatsSnapshot(config);
            set => stats = value ?? new FlightStatsSnapshot(config);
        }

        /// <summary>Current visual roll in degrees. Exposed for tests and tuning HUDs.</summary>
        public float BankAngle => bankAngle;

        public JetFlightConfig Config
        {
            get => config;
            set
            {
                config = value;
                // Re-snapshotted rather than kept: a swapped config that left
                // the old numbers in place would be a tuning change that
                // appears to do nothing.
                stats = new FlightStatsSnapshot(config);
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            stats = new FlightStatsSnapshot(config);
            ApplyConfigToBody();
        }

        /// <summary>
        /// Pushes the damping values from the config into the Rigidbody.
        /// Public so a tuning pass can hot-swap a config at runtime and see the
        /// change immediately rather than on the next scene load.
        /// </summary>
        public void ApplyConfigToBody()
        {
            if (body == null)
            {
                return;
            }
            body.linearDamping = Stats.LinearDrag;
            body.angularDamping = Stats.AngularDrag;
            body.useGravity = false;
        }

        private void FixedUpdate()
        {
            if (body == null)
            {
                return;
            }
            ApplyMovementForces(inputVector);
            ApplyBanking(Time.fixedDeltaTime);
        }

        /// <summary>
        /// Accelerates toward the input's target velocity instead of assigning
        /// velocity directly. Assignment would erase inertia and make the jet
        /// feel snapped and digital -- roadmap section 3, point 4.
        /// </summary>
        public void ApplyMovementForces(Vector2 input)
        {
            Vector3 current = body.linearVelocity;
            Vector3 acceleration = ComputeAcceleration(
                input, new Vector2(current.x, current.y), Stats.MaxSpeed, Stats.Acceleration);
            body.AddForce(acceleration, ForceMode.Acceleration);
        }

        /// <summary>
        /// Rolls the child visual, never the Rigidbody. The physics body stays
        /// axis-aligned so collision stays predictable; only the model tilts.
        /// </summary>
        public void ApplyBanking(float deltaTime)
        {
            float target = ComputeTargetBankAngle(
                body.linearVelocity.x, Stats.MaxSpeed, Stats.BankAngleMax);
            bankAngle = StepTowardBank(bankAngle, target, Stats.BankResponsiveness, deltaTime);
            if (visual != null)
            {
                visual.localRotation = Quaternion.Euler(0f, 0f, bankAngle);
            }
        }

        // --- pure functions -------------------------------------------------
        // The flight model's arithmetic lives here, free of Rigidbody and
        // Time, so EditMode tests can pin inertia and banking without a
        // physics tick and without a scene.

        /// <summary>
        /// Acceleration toward the target velocity the input asks for. The
        /// input is clamped to the unit circle first, so a diagonal push is
        /// not 1.41x faster than a cardinal one.
        /// </summary>
        public static Vector3 ComputeAcceleration(Vector2 input, Vector2 currentVelocity,
            float maxSpeed, float acceleration)
        {
            // Numbers rather than the asset: the jet flies on effective
            // values, and taking the ScriptableObject here would be an
            // invitation to write back to it.
            if (maxSpeed <= 0f || acceleration <= 0f)
            {
                return Vector3.zero;
            }
            Vector2 clamped = Vector2.ClampMagnitude(input, 1f);
            Vector2 targetVelocity = clamped * maxSpeed;
            Vector2 delta = targetVelocity - currentVelocity;

            // Cap the step at `acceleration` so releasing the stick coasts on
            // drag rather than braking, and so a large velocity error cannot
            // produce an impulse the tuning never accounted for.
            Vector2 result = Vector2.ClampMagnitude(delta * acceleration, acceleration);
            return new Vector3(result.x, result.y, 0f);
        }

        /// <summary>
        /// Bank angle for a lateral speed, in degrees. Negative roll for
        /// rightward motion: the jet leans into the turn, it does not lean
        /// away from it.
        /// </summary>
        public static float ComputeTargetBankAngle(float lateralVelocity, float maxSpeed, float bankAngleMax)
        {
            if (maxSpeed <= 0f)
            {
                return 0f;
            }
            float normalized = Mathf.Clamp(lateralVelocity / maxSpeed, -1f, 1f);
            return -normalized * bankAngleMax;
        }

        /// <summary>
        /// Frame-rate independent convergence toward the target bank.
        /// `Lerp(current, target, responsiveness * dt)` is the usual shortcut
        /// and is wrong: its rate depends on the tick length, so the same
        /// config banks differently at 30fps and 60fps -- and this project
        /// ships a 30fps low tier (QualityTierManager). Exponential decay is
        /// stable at any dt.
        /// </summary>
        public static float StepTowardBank(float current, float target, float responsiveness, float deltaTime)
        {
            if (deltaTime <= 0f || responsiveness <= 0f)
            {
                return current;
            }
            float t = 1f - Mathf.Exp(-responsiveness * deltaTime);
            return Mathf.Lerp(current, target, t);
        }
    }
}
