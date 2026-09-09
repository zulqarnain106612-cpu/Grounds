using UnityEngine;
using JetFighter.Player;

namespace JetFighter.UI.Input
{
    /// <summary>
    /// Carries the joystick's vector to the jet, once per frame.
    ///
    /// A separate component rather than a reference inside JetController: the
    /// flight model is driven by a Vector2 and does not care where it came
    /// from, which is what let the whole model be unit-tested with no input
    /// system at all. Merging the two would undo that.
    ///
    /// Runs in Update, not FixedUpdate. Input arrives on frame boundaries, so
    /// sampling it on physics ticks would drop or double-read touches
    /// depending on the tick rate -- and this project ships two.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInputRouter : MonoBehaviour
    {
        [SerializeField] private JoystickInput joystick;
        [SerializeField] private JetController jet;

        /// <summary>
        /// Right-hand input is Phase 2 (ADR-002). Held as the interface so
        /// this class never learns what implements it.
        /// </summary>
        public ITargetInput TargetInput { get; set; }

        public JoystickInput Joystick
        {
            get => joystick;
            set => joystick = value;
        }

        public JetController Jet
        {
            get => jet;
            set => jet = value;
        }

        private void Update()
        {
            if (jet == null)
            {
                return;
            }
            // A missing joystick means "no input", not "keep the last input".
            // A stale vector would fly the jet into a wall while the UI is
            // being rebuilt.
            jet.inputVector = joystick != null ? joystick.GetNormalizedVector() : Vector2.zero;
        }
    }
}
