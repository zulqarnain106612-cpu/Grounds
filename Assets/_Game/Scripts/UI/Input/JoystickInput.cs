using UnityEngine;
using UnityEngine.EventSystems;

namespace JetFighter.UI.Input
{
    /// <summary>
    /// Left-hand virtual joystick.
    ///
    /// The left-half restriction is enforced in OnPointerDown, not by where
    /// the RectTransform happens to sit. That distinction is the whole
    /// acceptance criterion: a joystick that only *usually* ignores the right
    /// half is one canvas-anchor change away from stealing the touch Phase 2
    /// needs for missile lock, and the failure looks like an input bug in a
    /// system that has not been written yet.
    /// </summary>
    public class JoystickInput : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform handle;
        [SerializeField] private RectTransform background;

        [Tooltip("Maximum handle displacement from centre, in canvas units.")]
        [Min(1f)]
        [SerializeField] private float radius = 120f;

        [Tooltip("Fraction of screen width this control will accept touches within.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float regionFraction = 0.5f;

        private Vector2 offset;
        private int activePointerId = InvalidPointer;

        /// <summary>No finger is currently driving the stick.</summary>
        public const int InvalidPointer = -999;

        /// <summary>True while a touch owns the stick.</summary>
        public bool IsHeld => activePointerId != InvalidPointer;

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(1f, value);
        }

        public float RegionFraction
        {
            get => regionFraction;
            set => regionFraction = Mathf.Clamp(value, 0.1f, 1f);
        }

        /// <summary>
        /// Stick offset over its radius, clamped to the unit circle so a
        /// corner pull is not faster than a cardinal one.
        /// </summary>
        public Vector2 GetNormalizedVector()
        {
            return Normalize(offset, radius);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || !IsInRegion(eventData.position, Screen.width, regionFraction))
            {
                return;
            }
            activePointerId = eventData.pointerId;
            if (background != null)
            {
                // Move the stick under the finger instead of making the player
                // find it. On a phone the thumb lands where it lands.
                background.position = eventData.position;
            }
            SetOffset(Vector2.zero);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Only the finger that claimed the stick may move it. Without this
            // a second touch anywhere on the left half yanks the jet sideways.
            if (eventData.pointerId != activePointerId)
            {
                return;
            }
            Vector2 origin = background != null ? (Vector2)background.position : eventData.pressPosition;
            SetOffset(Vector2.ClampMagnitude(eventData.position - origin, radius));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId)
            {
                return;
            }
            activePointerId = InvalidPointer;
            SetOffset(Vector2.zero);
        }

        private void SetOffset(Vector2 value)
        {
            offset = value;
            if (handle != null)
            {
                handle.anchoredPosition = value;
            }
        }

        // --- pure functions -------------------------------------------------

        /// <summary>
        /// Whether a screen position belongs to this control's region.
        ///
        /// Static and side-effect free so the left-region guarantee is a unit
        /// test rather than a device observation -- the acceptance criterion
        /// asks for zero effect from right-half touches, and "we tried it and
        /// nothing happened" does not prove zero.
        /// </summary>
        public static bool IsInRegion(Vector2 screenPosition, float screenWidth, float regionFraction)
        {
            // Delegated to ScreenRegions so this half and the right hand's
            // half cannot drift apart. Two copies of the divide either leave
            // a column no hand answers or one both hands claim, and neither
            // shows up in a screenshot.
            return ScreenRegions.IsInLeft(screenPosition, screenWidth, regionFraction);
        }

        /// <summary>Offset over radius, clamped to the unit circle.</summary>
        public static Vector2 Normalize(Vector2 offset, float radius)
        {
            if (radius <= 0f)
            {
                return Vector2.zero;
            }
            return Vector2.ClampMagnitude(offset / radius, 1f);
        }
    }
}
