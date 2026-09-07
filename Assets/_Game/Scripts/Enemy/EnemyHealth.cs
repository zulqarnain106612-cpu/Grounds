using UnityEngine;
using UnityEngine.Events;
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

        private float currentHealth;

        public float CurrentHealth => currentHealth;

        public float MaxHealth => def != null ? def.maxHealth : 0f;

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
            currentHealth = MaxHealth;
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
            OnDied.Invoke();
            gameObject.SetActive(false);
        }
    }
}
