using UnityEngine;

namespace JetFighter.PowerUp
{
    /// <summary>
    /// The collectable in the world.
    ///
    /// Deactivates rather than destroys: these come from a drop table in the
    /// next cell and will be pooled like everything else that spawns during a
    /// run.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PowerUpPickup : MonoBehaviour
    {
        /// <summary>Called when collected. The pool's Release.</summary>
        public System.Action<GameObject> OnCollected;

        [SerializeField] private PowerUpDef def;

        [Tooltip("Only this layer collects. Enemies flying through a pickup must not consume it.")]
        [SerializeField] private LayerMask collectorLayers = ~0;

        [Tooltip("Seconds before an uncollected pickup returns itself.")]
        [Min(0.1f)]
        [SerializeField] private float lifetimeSeconds = 15f;

        private float remaining;
        private bool collected;

        public PowerUpDef Def
        {
            get => def;
            set => def = value;
        }

        public bool IsCollected => collected;

        public LayerMask CollectorLayers
        {
            get => collectorLayers;
            set => collectorLayers = value;
        }

        /// <summary>
        /// Arms the pickup. Called on every spawn from the pool, not once:
        /// a recycled pickup carrying the previous drop's countdown would
        /// vanish immediately.
        /// </summary>
        public void Arm(PowerUpDef powerUp, float lifetime)
        {
            def = powerUp;
            remaining = Mathf.Max(0.1f, lifetime);
            collected = false;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>Ages the pickup out. Takes deltaTime for simulated-time tests.</summary>
        public void Step(float deltaTime)
        {
            if (collected)
            {
                return;
            }
            remaining -= deltaTime;
            if (remaining <= 0f)
            {
                Finish();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || !IsCollector(other.gameObject))
            {
                // An enemy flying through must not consume the pickup, and
                // must not silently remove it from the player either.
                return;
            }
            TryCollect(other.GetComponentInParent<PowerUpController>());
        }

        /// <summary>
        /// Hands the power-up to a controller. Public so the rule is testable
        /// without staging a physics collision.
        /// </summary>
        public bool TryCollect(PowerUpController controller)
        {
            if (collected || controller == null)
            {
                return false;
            }
            // Consumed even when Apply refuses a mis-authored asset: leaving
            // it in the world would let the player farm the same broken
            // pickup forever.
            controller.Apply(def);
            Finish();
            return true;
        }

        /// <summary>Ends this pickup's life, exactly once.</summary>
        public void Finish()
        {
            if (collected)
            {
                return;
            }
            collected = true;
            OnCollected?.Invoke(gameObject);
            gameObject.SetActive(false);
        }

        private bool IsCollector(GameObject candidate)
        {
            return (collectorLayers.value & (1 << candidate.layer)) != 0;
        }
    }
}
