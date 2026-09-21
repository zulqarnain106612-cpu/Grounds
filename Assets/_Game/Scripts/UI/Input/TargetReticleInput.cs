using UnityEngine;
using UnityEngine.EventSystems;

namespace JetFighter.UI.Input
{
    /// <summary>
    /// Right-hand targeting: raycasts a touch onto the ground layer and holds
    /// the result as the current missile target.
    ///
    /// The mirror of JoystickInput's guarantee, and the same structural
    /// pattern: the region check is a rejection inside OnPointerDown, not an
    /// assumption about where the RectTransform sits. Both halves consult
    /// ScreenRegions, so the divide is one number in one place.
    ///
    /// The ground restriction is a layer mask, which is ADR-002's escape
    /// hatch. That ADR is Accepted (implied) -- nothing in the repo says
    /// missiles may not hit air targets, the mask just makes it so. Keeping
    /// it as data means reversing it is a mask change and a targeting-priority
    /// rule, not a rewrite.
    /// </summary>
    public class TargetReticleInput : MonoBehaviour,
        ITargetInput, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Camera targetingCamera;
        [SerializeField] private Transform reticle;

        [Tooltip("Layers a missile may lock onto. Ground only, per ADR-002.")]
        [SerializeField] private LayerMask targetableLayers = ~0;

        [Tooltip("Fraction of screen width belonging to the left hand. The rest is this control's.")]
        [Range(0f, 1f)]
        [SerializeField] private float split = ScreenRegions.DefaultSplit;

        [Min(1f)]
        [SerializeField] private float maxRayDistance = 500f;

        [Tooltip("Keep the last lock when a touch lands on nothing targetable.")]
        [SerializeField] private bool keepTargetOnMiss = true;

        private int activePointerId = NoPointer;

        public const int NoPointer = -999;

        /// <summary>The locked target, or null.</summary>
        public Transform CurrentTarget { get; private set; }

        /// <summary>Where the reticle sits, or null when nothing is locked.</summary>
        public Vector2? ScreenTarget { get; private set; }

        /// <summary>True while a touch owns the reticle.</summary>
        public bool IsHeld => activePointerId != NoPointer;

        public float Split
        {
            get => split;
            set => split = Mathf.Clamp01(value);
        }

        public LayerMask TargetableLayers
        {
            get => targetableLayers;
            set => targetableLayers = value;
        }

        public Camera TargetingCamera
        {
            get => targetingCamera;
            set => targetingCamera = value;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || !IsInRegion(eventData.position, Screen.width, split))
            {
                return;
            }
            activePointerId = eventData.pointerId;
            Aim(eventData.position);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Only the finger that claimed the reticle may move it. Without
            // this the left thumb drags the lock while flying.
            if (eventData.pointerId != activePointerId)
            {
                return;
            }
            Aim(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId)
            {
                return;
            }
            activePointerId = NoPointer;
            // The lock survives the lift on purpose: the player raises the
            // thumb to press the missile button, and a lock that died with
            // the touch would make the weapon unusable one-handed.
        }

        /// <summary>
        /// Casts a screen position onto the targetable layers and updates the
        /// lock. Public so the targeting rule is testable without a
        /// synthesized touch.
        /// </summary>
        public void Aim(Vector2 screenPosition)
        {
            if (targetingCamera == null)
            {
                return;
            }
            Ray ray = targetingCamera.ScreenPointToRay(screenPosition);
            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, targetableLayers))
            {
                SetTarget(hit.transform, screenPosition);
                return;
            }
            if (!keepTargetOnMiss)
            {
                ClearTarget();
            }
        }

        /// <summary>Locks onto a transform and moves the reticle. The only writer of the lock.</summary>
        public void SetTarget(Transform target, Vector2 screenPosition)
        {
            CurrentTarget = target;
            ScreenTarget = screenPosition;
            if (reticle != null)
            {
                reticle.position = screenPosition;
                reticle.gameObject.SetActive(target != null);
            }
        }

        public void ClearTarget()
        {
            CurrentTarget = null;
            ScreenTarget = null;
            if (reticle != null)
            {
                reticle.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            // A locked enemy that died or was pooled away leaves a reticle
            // hovering over nothing, and the launcher would fire at a
            // destroyed transform.
            if (CurrentTarget != null && !CurrentTarget.gameObject.activeInHierarchy)
            {
                ClearTarget();
            }
        }

        /// <summary>
        /// Whether a screen position belongs to the right hand. Mirrors
        /// JoystickInput.IsInRegion; both delegate to ScreenRegions.
        /// </summary>
        public static bool IsInRegion(Vector2 screenPosition, float screenWidth, float split)
        {
            return ScreenRegions.IsInRight(screenPosition, screenWidth, split);
        }
    }
}
