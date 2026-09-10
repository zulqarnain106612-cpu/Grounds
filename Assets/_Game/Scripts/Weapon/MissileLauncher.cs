using UnityEngine;
using JetFighter.Player;
using JetFighter.Shared;

namespace JetFighter.Weapon
{
    /// <summary>
    /// The secondary weapon: manually triggered, one missile per press, on a
    /// cooldown.
    ///
    /// Reuses the Phase 1 ObjectPool rather than a missile-specific one --
    /// which is why that utility was written generic in Phase 1 rather than
    /// as part of the gun.
    ///
    /// Unlike the primary gun this never auto-fires. That is the design split
    /// in ADR-002, and it is also what makes the cooldown meaningful: an
    /// auto-firing missile would spend its cooldown by itself and the player
    /// would never feel the resource.
    /// </summary>
    public class MissileLauncher : MonoBehaviour
    {
        [SerializeField] private WeaponBase missileDef;
        [SerializeField] private Transform launchTransform;

        [Tooltip("Seconds between launches. Independent of the missile's own fire rate field.")]
        [Min(0f)]
        [SerializeField] private float cooldownSeconds = 3f;

        private ObjectPool pool;
        // Double, not float: this is decremented one frame at a time and
        // compared against zero. A 3s cooldown ticked down by 180 float
        // additions of 1/60 lands ~1e-6 above zero, so the launcher is still
        // refusing presses a frame after the UI's radial fill shows it ready.
        private double cooldownRemaining;
        private IPlayerStats stats = DefaultPlayerStats.Instance;

        /// <summary>Missiles launched since this launcher woke. For soak tests.</summary>
        public int LaunchCount { get; private set; }

        /// <summary>Presses refused by the cooldown. Surfaced so UI can show the lockout.</summary>
        public int BlockedByCooldown { get; private set; }

        public float CooldownRemaining => (float)cooldownRemaining;

        // Same constant and the same reasoning as Bullet.LifetimeEpsilon and
        // PrimaryGunController.CooldownEpsilon: float subtraction does not land
        // on zero exactly, so counting a 3s cooldown down in 1/60s steps leaves
        // ~2e-6 behind and a bare `<= 0f` never reports ready.
        private const float CooldownEpsilon = 1e-4f;

        public bool IsReady => cooldownRemaining <= CooldownEpsilon;

        /// <summary>Fraction of the cooldown elapsed, 0..1. For a radial UI fill.</summary>
        public float CooldownProgress =>
            cooldownSeconds <= 0f ? 1f : Mathf.Clamp01(1f - (float)(cooldownRemaining / cooldownSeconds));

        public ObjectPool Pool => pool;

        public WeaponBase MissileDef
        {
            get => missileDef;
            set
            {
                missileDef = value;
                pool = null;
            }
        }

        public float CooldownSeconds
        {
            get => cooldownSeconds;
            set => cooldownSeconds = Mathf.Max(0f, value);
        }

        public IPlayerStats Stats
        {
            get => stats;
            set => stats = value ?? DefaultPlayerStats.Instance;
        }

        public Transform LaunchTransform
        {
            get => launchTransform;
            set => launchTransform = value;
        }

        private void Awake()
        {
            EnsurePool();
        }

        public void EnsurePool()
        {
            if (pool != null || missileDef == null || missileDef.projectilePrefab == null)
            {
                return;
            }
            pool = new ObjectPool(missileDef.projectilePrefab, missileDef.poolCapacity, transform);
            pool.Prewarm(missileDef.poolCapacity);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances the cooldown. Clamped at zero rather than left to run
        /// negative: a launcher idle for two minutes would otherwise bank two
        /// minutes of readiness and fire that many missiles back to back the
        /// moment the player pressed.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (cooldownRemaining > 0f)
            {
                cooldownRemaining = System.Math.Max(0d, cooldownRemaining - deltaTime);
            }
        }

        /// <summary>
        /// Launches at a locked target. Returns false when refused, so the
        /// button can tell "no target" from "still cooling" without reaching
        /// into this class's state.
        /// </summary>
        public bool Fire(Transform target)
        {
            if (target == null || missileDef == null)
            {
                return false;
            }
            if (!IsReady)
            {
                BlockedByCooldown++;
                return false;
            }
            EnsurePool();
            if (pool == null)
            {
                return false;
            }

            GameObject instance = pool.Get();
            Transform origin = launchTransform != null ? launchTransform : transform;
            instance.transform.SetPositionAndRotation(origin.position, origin.rotation);

            var missile = instance.GetComponent<MissileController>();
            if (missile != null)
            {
                missile.OnFinished = ReleaseMissile;
                missile.Launch(target,
                    missileDef.damage * Mathf.Max(0f, stats.DamageMultiplier),
                    missileDef.projectileSpeed,
                    missileDef.turnDegreesPerSecond,
                    missileDef.projectileLifetime,
                    missileDef.impactRadius);
            }

            cooldownRemaining = cooldownSeconds;
            LaunchCount++;
            return true;
        }

        private void ReleaseMissile(GameObject missile)
        {
            pool?.Release(missile);
        }
    }
}
