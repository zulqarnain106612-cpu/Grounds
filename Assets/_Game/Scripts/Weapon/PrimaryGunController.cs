using UnityEngine;
using JetFighter.Shared;

namespace JetFighter.Weapon
{
    /// <summary>
    /// Auto-fires the primary gun from a pool.
    ///
    /// The cooldown is time-based, not frame-counted, so the fire rate is the
    /// same on the 30fps low tier as on 60 (QualityTierManager). The
    /// acceptance criterion is 1.0s +/- 0.02s "regardless of frame rate",
    /// which a frame counter cannot satisfy by construction.
    /// </summary>
    public class PrimaryGunController : MonoBehaviour
    {
        [SerializeField] private WeaponBase weaponDef;

        [Tooltip("Front-centre mount point. Bullets spawn here.")]
        [SerializeField] private Transform muzzleTransform;

        [SerializeField] private bool autoFire = true;

        private ObjectPool pool;
        private float cooldownTimer;

        /// <summary>Shots fired since the gun woke. Exposed for soak tests.</summary>
        public int ShotsFired { get; private set; }

        /// <summary>Seconds until the next shot is allowed.</summary>
        public float CooldownRemaining => cooldownTimer;

        public ObjectPool Pool => pool;

        public WeaponBase WeaponDef
        {
            get => weaponDef;
            set
            {
                weaponDef = value;
                pool = null;
            }
        }

        public Transform MuzzleTransform
        {
            get => muzzleTransform;
            set => muzzleTransform = value;
        }

        /// <summary>
        /// Whether Update drives the cooldown by itself.
        ///
        /// Exposed for the same reason Tick takes a deltaTime: a scene test
        /// that wants to observe the pool before the first shot cannot do it
        /// while the player loop is firing between its setup and its
        /// assertions.
        /// </summary>
        public bool AutoFire
        {
            get => autoFire;
            set => autoFire = value;
        }

        private void Awake()
        {
            EnsurePool();
        }

        /// <summary>
        /// Builds the pool and fills it. Prewarming matters here: the first
        /// shot is the one moment the player is guaranteed to be watching,
        /// and an Instantiate spike there is the one they remember.
        /// </summary>
        public void EnsurePool()
        {
            if (pool != null || weaponDef == null || weaponDef.projectilePrefab == null)
            {
                return;
            }
            pool = new ObjectPool(weaponDef.projectilePrefab, weaponDef.poolCapacity, transform);
            pool.Prewarm(weaponDef.poolCapacity);
        }

        private void Update()
        {
            if (!autoFire)
            {
                return;
            }
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances the cooldown and fires when it expires. Takes deltaTime as
        /// an argument so a test can drive a hundred simulated seconds without
        /// waiting a hundred real ones.
        ///
        /// The remainder is carried rather than reset to the full cooldown: at
        /// 30fps a 1.0s cooldown expiring 0.02s into a 0.033s frame would
        /// otherwise drift by up to a frame per shot, which is exactly the
        /// tolerance the criterion sets.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (weaponDef == null)
            {
                return;
            }
            EnsurePool();
            cooldownTimer -= deltaTime;

            float cooldown = weaponDef.CooldownSeconds;
            // A loop, not an `if`: a frame longer than the cooldown (a hitch,
            // or a fire-rate power-up in Phase 3) still owes the player every
            // shot that elapsed during it.
            int guard = 0;
            while (cooldownTimer <= 0f && guard++ < 64)
            {
                Fire();
                cooldownTimer += cooldown;
            }
        }

        /// <summary>
        /// Takes one projectile from the pool and positions it at the muzzle.
        /// No Instantiate and no Destroy on this path -- that is the cell's
        /// acceptance criterion, not an optimisation.
        /// </summary>
        public void Fire()
        {
            ShotsFired++;
            if (pool == null)
            {
                return;
            }
            GameObject bullet = pool.Get();
            Transform origin = muzzleTransform != null ? muzzleTransform : transform;
            bullet.transform.SetPositionAndRotation(origin.position, origin.rotation);
        }
    }
}
