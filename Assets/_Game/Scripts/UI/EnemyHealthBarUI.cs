using UnityEngine;
using UnityEngine.UI;
using JetFighter.Enemy;

namespace JetFighter.UI
{
    /// <summary>
    /// World-space health bar over one enemy.
    ///
    /// Fill and colour are driven by EnemyHealth.OnDamaged, never by reading
    /// health each frame. The roadmap makes that a performance requirement --
    /// this component exists once per live enemy, so a per-frame read is a
    /// per-frame read times the on-screen enemy cap.
    ///
    /// Billboarding *is* per-frame, and that is not a contradiction: the
    /// camera moves every frame whether or not anyone takes damage. The rule
    /// is about health, and the two are separated so the distinction survives
    /// someone editing this later.
    /// </summary>
    public class EnemyHealthBarUI : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private EnemyHealth health;
        [SerializeField] private Image fillImage;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Placement")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Thresholds")]
        [Tooltip("At or above this fraction the bar is healthy-coloured.")]
        [Range(0f, 1f)]
        [SerializeField] private float healthyThreshold = 0.6f;

        [Tooltip("Below this fraction the bar is critical-coloured.")]
        [Range(0f, 1f)]
        [SerializeField] private float criticalThreshold = 0.3f;

        [Tooltip("Hide the bar while the enemy is undamaged.")]
        [SerializeField] private bool hideAtFullHealth = true;

        private Camera billboardCamera;

        /// <summary>Times the bar has been refreshed. A polling regression shows up here.</summary>
        public int RefreshCount { get; private set; }

        public EnemyHealth Health
        {
            get => health;
            set
            {
                Unsubscribe();
                health = value;
                Subscribe();
                Refresh(health != null ? health.PercentRemaining : 1f);
            }
        }

        public Transform FollowTarget
        {
            get => followTarget;
            set => followTarget = value;
        }

        /// <summary>
        /// The filled image. Settable because a pooled enemy's bar is wired up
        /// in code at spawn, not dragged in an inspector.
        /// </summary>
        public Image FillImage
        {
            get => fillImage;
            set => fillImage = value;
        }

        public CanvasGroup Group
        {
            get => canvasGroup;
            set => canvasGroup = value;
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh(health != null ? health.PercentRemaining : 1f);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (health == null)
            {
                return;
            }
            // Removed first so a re-enable, or a Health reassignment, cannot
            // leave two subscriptions behind -- pooled enemies enable and
            // disable repeatedly, and a doubled listener is invisible until
            // profiling.
            health.OnDamaged.RemoveListener(Refresh);
            health.OnDamaged.AddListener(Refresh);
            health.OnDied.RemoveListener(HandleDeath);
            health.OnDied.AddListener(HandleDeath);
        }

        private void Unsubscribe()
        {
            if (health == null)
            {
                return;
            }
            health.OnDamaged.RemoveListener(Refresh);
            health.OnDied.RemoveListener(HandleDeath);
        }

        /// <summary>Applies a health fraction to the bar. The only writer of fill and colour.</summary>
        public void Refresh(float percentRemaining)
        {
            RefreshCount++;
            float clamped = Mathf.Clamp01(percentRemaining);

            if (fillImage != null)
            {
                fillImage.fillAmount = clamped;
                fillImage.color = ColorFor(clamped, healthyThreshold, criticalThreshold);
            }
            if (canvasGroup != null)
            {
                canvasGroup.alpha = hideAtFullHealth && clamped >= 1f ? 0f : 1f;
            }
        }

        private void HandleDeath()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private void LateUpdate()
        {
            // Placement only. Nothing here reads health.
            if (followTarget != null)
            {
                transform.position = followTarget.position + worldOffset;
            }
            if (billboardCamera == null)
            {
                billboardCamera = Camera.main;
            }
            if (billboardCamera != null)
            {
                transform.rotation = billboardCamera.transform.rotation;
            }
        }

        /// <summary>
        /// Bar colour for a health fraction.
        ///
        /// Static and pure so the thresholds are a unit test rather than a
        /// screenshot. Lerped inside each band rather than snapping between
        /// three colours: a bar that jumps from green to yellow reads as a
        /// bug the first time a player sees it happen mid-burst.
        /// </summary>
        public static Color ColorFor(float percentRemaining, float healthyThreshold, float criticalThreshold)
        {
            float pct = Mathf.Clamp01(percentRemaining);
            // Guard against an inspector where the two were dragged past each
            // other; the bands must stay ordered or the lerps invert.
            float healthy = Mathf.Max(healthyThreshold, criticalThreshold);
            float critical = Mathf.Min(healthyThreshold, criticalThreshold);

            if (pct >= healthy)
            {
                return Color.green;
            }
            if (pct <= critical)
            {
                return Color.red;
            }
            // Two bands with yellow at the midpoint, per the spec's
            // green -> yellow -> red. A single red-to-green lerp passes
            // through a muddy olive instead, which reads as a rendering fault
            // rather than as a warning.
            float t = Mathf.InverseLerp(critical, healthy, pct);
            return t < 0.5f
                ? Color.Lerp(Color.red, Color.yellow, t * 2f)
                : Color.Lerp(Color.yellow, Color.green, (t - 0.5f) * 2f);
        }
    }
}
