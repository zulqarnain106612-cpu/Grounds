using UnityEngine;
using UnityEngine.Events;
using JetFighter.PowerUp;
using JetFighter.Shared;

namespace JetFighter.Enemy
{
    /// <summary>
    /// Health and death for one enemy.
    ///
    /// Emits OnDamaged rather than exposing health for the UI to poll. The
    /// roadmap makes this a performance requirement, and it is also the only
    /// version that survives Phase 4: with host-authoritative enemies, health
    /// changes arrive from the network on no particular frame, and a poller
    /// would show them a frame late on one device and on time on the other.
    /// </summary>
    public class EnemyHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private EnemyDef def;

        [Tooltip("Fires on every damage application. Argument is the fraction of health remaining, 0..1.")]
        public UnityEvent<float> OnDamaged = new UnityEvent<float>();

        [Tooltip("Fires once, when health reaches zero.")]
        public UnityEvent OnDied = new UnityEvent();

        [Tooltip("Fires once on death with whatever the drop table rolled. Never fires with null.")]
        public UnityEvent<PowerUpDef> OnDropped = new UnityEvent<PowerUpDef>();

        private float currentHealth;
        private float scaledMaxHealth;

        public float CurrentHealth => currentHealth;

        /// <summary>
        /// Effective max health: the difficulty-scaled value when one was set,
        /// otherwise the archetype's authored value.
        /// </summary>
        public float MaxHealth => scaledMaxHealth > 0f ? scaledMaxHealth : (def != null ? def.maxHealth : 0f);

        /// <summary>The archetype's authored health, unscaled.</summary>
        public float AuthoredMaxHealth => def != null ? def.maxHealth : 0f;

        public bool IsDead => currentHealth <= 0f;

        /// <summary>Fraction of health remaining, 0..1. Zero when undefined.</summary>
        public float PercentRemaining => MaxHealth > 0f ? currentHealth / MaxHealth : 0f;

        public EnemyDef Def
        {
            get => def;
            set
            {
                def = value;
                ResetHealth();
            }
        }

        private void Awake()
        {
            ResetHealth();
        }

        /// <summary>
        /// Restores full health. Called on spawn -- and on reuse, because
        /// these come from a pool and a recycled enemy that kept its old
        /// health would die to a single bullet.
        /// </summary>
        public void ResetHealth()
        {
            scaledMaxHealth = 0f;
            currentHealth = MaxHealth;
        }

        /// <summary>
        /// Applies authoritative health from the host.
        ///
        /// Distinct from ApplyDamage on purpose: this does not fire OnDamaged
        /// or OnDied, and does not roll a drop. Only the host rolls drops --
        /// a guest that rolled its own would produce different loot from the
        /// same kill, and the health bar is driven by the value it is handed
        /// rather than by a damage event it did not witness.
        ///
        /// It also does not clamp to the previous health: a host that revived
        /// or rescaled an enemy is still the authority, and a guest refusing
        /// to follow it upward is exactly the divergence this exists to stop.
        /// </summary>
        public void SetNetworkedHealth(float current, float max)
        {
            if (max > 0f)
            {
                scaledMaxHealth = max;
            }
            currentHealth = Mathf.Clamp(current, 0f, MaxHealth);
            OnDamaged.Invoke(PercentRemaining);
        }

        /// <summary>
        /// Overrides max health for this instance, for the difficulty
        /// multiplier.
        ///
        /// On the instance, never written back to the EnemyDef -- the same
        /// rule PlayerStatsRuntime follows, and for the same reason: a
        /// ScriptableObject edited in play mode keeps the change in the
        /// editor, so one scaled spawn during testing would become the
        /// archetype's authored health.
        /// </summary>
        public void SetScaledHealth(float maxHealthForThisInstance)
        {
            scaledMaxHealth = Mathf.Max(1f, maxHealthForThisInstance);
            currentHealth = scaledMaxHealth;
        }

        public void ApplyDamage(float amount)
        {
            // Ignored rather than trusted: a zero-damage weapon should not
            // trip the UI every tick, and a negative one is never what
            // anyone meant.
            if (amount <= 0f || IsDead)
            {
                return;
            }

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            OnDamaged.Invoke(PercentRemaining);

            if (currentHealth <= 0f)
            {
                Die();
            }
        }

        /// <summary>
        /// Phase 2 deactivates. Drop tables are Phase 3 and hook OnDied --
        /// which is why death is an event here rather than a Destroy call:
        /// a destroyed enemy cannot be returned to the pool it came from.
        /// </summary>
        public void Die()
        {
            RollDrop();
            OnDied.Invoke();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Rolls the drop table and announces the result.
        ///
        /// Announced rather than spawned here: EnemyHealth knows about damage,
        /// not about where pickups come from or which pool owns them. The
        /// spawner listening to this is the same split the health bar already
        /// uses, and it is what lets Phase 4 make the host the only roller
        /// without touching this class.
        ///
        /// Rolled before OnDied so a listener that deactivates the enemy
        /// cannot cancel the drop.
        /// </summary>
        private void RollDrop()
        {
            if (def == null || def.dropTable == null)
            {
                return;
            }
            PowerUpDef dropped = def.dropTable.Roll(NextRoll(), NextRoll());
            if (dropped != null)
            {
                OnDropped.Invoke(dropped);
            }
        }

        /// <summary>
        /// Source of randomness for drops. Overridable so a test can make the
        /// distribution deterministic, and so Phase 4 can drive both clients
        /// from one seed -- a drop each client rolled separately is a desync
        /// with loot in it.
        /// </summary>
        public System.Func<float> RollSource { get; set; }

        private float NextRoll()
        {
            return RollSource != null ? RollSource() : UnityEngine.Random.value;
        }
    }
}
