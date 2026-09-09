using System;
using UnityEngine;
using JetFighter.UI.Input;
using JetFighter.Weapon;

namespace JetFighter.Player
{
    /// <summary>
    /// Spawn, countdown, hand off.
    ///
    /// The criterion is negative and precise: the jet cannot be moved or fire
    /// during the countdown, and both enable exactly at Go -- not before. So
    /// control is gated in one place rather than by each system checking a
    /// flag. Scattered checks are how "not before" becomes "not before,
    /// except the gun, which someone re-enabled in a later branch."
    ///
    /// The spawn tween is not physics-driven. It runs while the Rigidbody is
    /// kinematic, because a jet flown in by forces would arrive with velocity
    /// that the plane constraint then has to fight on the first frame the
    /// player is given control.
    /// </summary>
    public class IntroSequenceController : MonoBehaviour
    {
        public enum State
        {
            Idle = 0,
            Spawn = 1,
            Countdown = 2,
            Go = 3,
            PlayerControl = 4,
        }

        [Header("Gated systems")]
        [SerializeField] private Rigidbody jetBody;
        [SerializeField] private PlayerInputRouter inputRouter;
        [SerializeField] private PrimaryGunController primaryGun;
        [SerializeField] private MissileLauncher missileLauncher;

        [Header("Spawn")]
        [Tooltip("Where the jet starts, typically below the screen.")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, -12f, 0f);

        [Min(0.01f)]
        [SerializeField] private float spawnSeconds = 1.5f;

        [Tooltip("Eases the fly-in. Linear looks mechanical at this length.")]
        [SerializeField] private AnimationCurve spawnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Countdown")]
        [Min(1)]
        [SerializeField] private int countFrom = 5;

        [Min(0.05f)]
        [SerializeField] private float secondsPerCount = 1f;

        [SerializeField] private bool autoStart = true;

        /// <summary>Raised on each countdown number, then once with 0 meaning Go.</summary>
        public event Action<int> OnCountChanged;

        /// <summary>Raised when control passes to the player.</summary>
        public event Action OnPlayerControl;

        private Vector3 restPosition;
        private float stateElapsed;
        private int currentCount;

        public State Current { get; private set; } = State.Idle;

        /// <summary>The number currently displayed, or 0 once the countdown is done.</summary>
        public int CurrentCount => currentCount;

        public Rigidbody JetBody { get => jetBody; set => jetBody = value; }

        public PlayerInputRouter InputRouter { get => inputRouter; set => inputRouter = value; }

        public PrimaryGunController PrimaryGun { get => primaryGun; set => primaryGun = value; }

        public MissileLauncher MissileLauncher { get => missileLauncher; set => missileLauncher = value; }

        public float SpawnSeconds { get => spawnSeconds; set => spawnSeconds = Mathf.Max(0.01f, value); }

        public int CountFrom { get => countFrom; set => countFrom = Mathf.Max(1, value); }

        public float SecondsPerCount
        {
            get => secondsPerCount;
            set => secondsPerCount = Mathf.Max(0.05f, value);
        }

        private void Awake()
        {
            restPosition = transform.position;
            if (autoStart)
            {
                Begin();
            }
        }

        /// <summary>Starts the sequence from the top. Public so a retry can replay it.</summary>
        public void Begin()
        {
            restPosition = transform.position;
            currentCount = countFrom;
            stateElapsed = 0f;
            Enter(State.Spawn);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances the sequence. Takes deltaTime so the whole intro can be
        /// driven in simulated time -- the criterion is about what is enabled
        /// at each instant, and waiting six real seconds per assertion would
        /// make that suite unusable.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (Current == State.Idle || Current == State.PlayerControl)
            {
                return;
            }
            stateElapsed += deltaTime;

            // A single frame can be longer than a whole state -- a hitch, or a
            // deliberately large step from a test. Dispatching once per Tick
            // dropped whatever was left of the frame at each state boundary, so
            // one 3.5s frame during the countdown emitted a single number and
            // threw the other three away. Each state now leaves its unused
            // remainder in stateElapsed and the next one consumes it in the
            // same frame. The bound is a guard against a zero-length state
            // looping forever, not an expected number of transitions.
            for (int guard = 0; guard < 64; guard++)
            {
                State before = Current;
                switch (Current)
                {
                    case State.Spawn:
                        TickSpawn();
                        break;
                    case State.Countdown:
                        TickCountdown();
                        break;
                    case State.Go:
                        Enter(State.PlayerControl);
                        break;
                }

                if (Current == before || Current == State.Idle
                    || Current == State.PlayerControl)
                {
                    break;
                }
            }
        }

        private void TickSpawn()
        {
            float t = Mathf.Clamp01(stateElapsed / spawnSeconds);
            transform.position = Vector3.LerpUnclamped(
                restPosition + spawnOffset, restPosition, spawnCurve.Evaluate(t));

            if (t >= 1f)
            {
                // Snapped to the exact rest position rather than left wherever
                // the curve landed: an overshooting ease would otherwise hand
                // the player a jet a few centimetres off its plane, and the
                // constraint would visibly yank it back on the first frame.
                transform.position = restPosition;
                // Carry the overshoot rather than zeroing it: the frame that
                // ends the spawn may be long enough to cover part of the
                // countdown too, and that time belongs to the countdown.
                stateElapsed = Mathf.Max(0f, stateElapsed - spawnSeconds);
                Enter(State.Countdown);
                OnCountChanged?.Invoke(currentCount);
            }
        }

        private void TickCountdown()
        {
            while (stateElapsed >= secondsPerCount && currentCount > 0)
            {
                stateElapsed -= secondsPerCount;
                currentCount--;
                OnCountChanged?.Invoke(currentCount);
            }
            if (currentCount <= 0)
            {
                Enter(State.Go);
            }
        }

        private void Enter(State next)
        {
            Current = next;
            // One place decides what is enabled. Each system checking a flag
            // for itself is how "not before Go" becomes "not before Go,
            // except the gun".
            bool playerHasControl = next == State.Go || next == State.PlayerControl;
            ApplyControl(playerHasControl);

            if (next == State.Spawn)
            {
                // Snap to the entry position on entering the state, not on the
                // first Tick. Waiting for the tick leaves the jet sitting at its
                // rest position until a frame has passed -- one frame of the jet
                // visible where it is supposed to arrive, then a jump below it.
                transform.position = restPosition + spawnOffset;
            }

            if (next == State.PlayerControl)
            {
                OnPlayerControl?.Invoke();
            }
        }

        /// <summary>
        /// Enables or disables every player-driven system at once. Public so a
        /// pause menu, or Phase 4's disconnect handling, reuses the same gate
        /// rather than inventing a second one.
        /// </summary>
        public void ApplyControl(bool enabled)
        {
            if (jetBody != null)
            {
                jetBody.isKinematic = !enabled;
                if (!enabled)
                {
                    // Cleared as it locks, not as it unlocks: velocity
                    // accumulated before the gate would otherwise be waiting
                    // to launch the jet the instant control arrives.
                    jetBody.linearVelocity = Vector3.zero;
                    jetBody.angularVelocity = Vector3.zero;
                }
            }
            if (inputRouter != null)
            {
                inputRouter.enabled = enabled;
                if (!enabled && inputRouter.Jet != null)
                {
                    // The router is what normally zeroes this. Disabled, it
                    // cannot, and a held stick would resume mid-turn.
                    inputRouter.Jet.inputVector = Vector2.zero;
                }
            }
            if (primaryGun != null)
            {
                primaryGun.AutoFire = enabled;
            }
            if (missileLauncher != null)
            {
                missileLauncher.enabled = enabled;
            }
        }
    }
}
