using UnityEngine;
using UnityEngine.EventSystems;
using JetFighter.Weapon;

namespace JetFighter.UI.Input
{
    /// <summary>
    /// The fire button for the secondary weapon.
    ///
    /// Fires on press, not on click. A UGUI click needs the release to land
    /// on the same button, so a thumb that drifts a few pixels while the jet
    /// is being flown swallows the shot -- and the player reads that as the
    /// game ignoring them, not as a gesture technicality.
    /// </summary>
    public class MissileTriggerButton : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField] private TargetReticleInput reticle;
        [SerializeField] private MissileLauncher launcher;

        /// <summary>Presses with nothing locked. Surfaced so UI can grey the button out.</summary>
        public int PressesWithoutTarget { get; private set; }

        public TargetReticleInput Reticle
        {
            get => reticle;
            set => reticle = value;
        }

        public MissileLauncher Launcher
        {
            get => launcher;
            set => launcher = value;
        }

        /// <summary>Whether a press right now would launch. For button interactability.</summary>
        public bool CanFire =>
            launcher != null && launcher.IsReady && reticle != null && reticle.CurrentTarget != null;

        public void OnPointerDown(PointerEventData eventData)
        {
            Press();
        }

        /// <summary>Attempts a launch. Public so the rule is testable without a synthesized touch.</summary>
        public bool Press()
        {
            Transform target = reticle != null ? reticle.CurrentTarget : null;
            if (target == null)
            {
                // Not a failure worth logging on every press; it is the normal
                // state before the player has aimed.
                PressesWithoutTarget++;
                return false;
            }
            return launcher != null && launcher.Fire(target);
        }
    }
}
