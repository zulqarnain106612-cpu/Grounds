using System.Collections.Generic;
using UnityEngine;
using JetFighter.Player;
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

        // Scratch space for one frame's walk over the pool's live instances.
        // Not a record of what is in flight -- the pool holds that, and
        // returning a spent round stays the round's own job. Reused so the
        // walk does not allocate every frame.
        private readonly List<GameObject> stepBuffer = new List<GameObject>();
        private float cooldownTimer;
        private IPlayerStats stats = DefaultPlayerStats.Instance;

        /// <summary>
        /// Live player multipliers. Assigning null restores the unmodified
        /// defaults rather than silencing the gun -- a jet that stops shooting
        /// because a power-up system has not spawned yet is a worse bug than
        /// any this would prevent.
        /// </summary>
        public IPlayerStats Stats
        {
            get => stats;
            set => stats = value ?? DefaultPlayerStats.Instance;
        }

        /// <summary>Seconds between shots, after the player's fire-rate multiplier.</summary>
        public float EffectiveCooldownSeconds =>
            weaponDef == null ? 0f : weaponDef.CooldownSeconds / Mathf.Max(0.01f, stats.FireRateMultiplier);

        /// <summary>Damage per projectile, after the player's damage multiplier.</summary>
        public float EffectiveDamage =>
            weaponDef == null ? 0f : weaponDef.damage * Mathf.Max(0f, stats.DamageMultiplier);

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
        /// Whether Update drives the gun. Turned off by the intro sequence
        /// (Phase 2) so the gun is silent until Go, and by tests that need to
        /// place every shot themselves -- a scene test cannot observe the pool
        /// before the first shot while the player loop is firing between its
        /// setup and its assertions.
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
                // Firing is off, but rounds already in the air still have to
                // travel and expire. Bullets do not advance themselves any
                // more, so returning here would strand every one of them: they
                // would hang in place and never come back to the pool, and the
                // gun would starve the moment firing resumed.
                StepLiveProjectiles(Time.deltaTime);
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
            StepLiveProjectiles(deltaTime);
            cooldownTimer -= deltaTime;

            float cooldown = EffectiveCooldownSeconds;
            // A loop, not an `if`: a frame longer than the cooldown (a hitch,
            // or a fire-rate power-up in Phase 3) still owes the player every
            // shot that elapsed during it.
            //
            // The first check admits a timer of exactly zero, which is what
            // makes the opening shot immediate. The carry after a shot has to
            // be strictly negative to fire again inside the same frame: a
            // frame that lands exactly on the cadence -- every frame, when the
            // step is the cooldown -- otherwise pays twice, once for the shot
            // due at its start and once for the one due at its end. That extra
            // opening round is one more bullet in the air than the pool was
            // sized for, and the pool answers by recycling a live one.
            bool due = cooldownTimer <= 0f;
            int guard = 0;
            while (due && guard++ < 64)
            {
                Fire();
                cooldownTimer += cooldown;
                due = cooldownTimer < 0f;
            }
        }

        /// <summary>
        /// Returns a spent projectile. Assigned to each bullet as it launches,
        /// so the gun never has to track what is in flight.
        /// </summary>
        private void ReleaseProjectile(GameObject projectile)
        {
            pool?.Release(projectile);
        }

        /// <summary>
        /// Advances every round in the air by the same deltaTime the gun was
        /// ticked with.
        ///
        /// The gun owns the clock because it is the only way the two agree. A
        /// bullet that advanced itself from Time.deltaTime while the gun ran
        /// on a simulated step would never reach its lifetime during a
        /// simulated soak: the gun fires four seconds' worth of shots inside
        /// one second of real time, none expire, and the pool starts recycling
        /// rounds that are still on screen.
        ///
        /// Which instances are live is asked of the pool each time rather than
        /// tracked here. The gun keeping its own list of bullets is the thing
        /// test_the_gun_does_not_track_bullets_in_flight exists to prevent.
        /// </summary>
        private void StepLiveProjectiles(float deltaTime)
        {
            if (pool == null)
            {
                return;
            }
            pool.CopyLiveTo(stepBuffer);
            for (int i = 0; i < stepBuffer.Count; i++)
            {
                GameObject instance = stepBuffer[i];
                if (instance == null)
                {
                    continue;
                }
                Bullet projectile = instance.GetComponent<Bullet>();
                if (projectile != null)
                {
                    projectile.Step(deltaTime);
                }
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

            // Armed on every Get, not once at spawn: a recycled bullet still
            // carrying the previous shot's countdown would expire mid-screen.
            // EffectiveDamage is read here, so the shot carries the player's
            // multiplier as it was at fire time.
            var projectile = bullet.GetComponent<Bullet>();
            if (projectile != null)
            {
                projectile.OnFinished = ReleaseProjectile;
                projectile.Launch(EffectiveDamage, weaponDef.projectileSpeed, weaponDef.projectileLifetime);
            }
        }
    }
}
