using System.Collections.Generic;
using UnityEngine;
using JetFighter.Shared;

namespace JetFighter.Enemy
{
    /// <summary>
    /// Spawns from the currently unlocked archetypes, on an interval, with the
    /// difficulty multiplier applied.
    ///
    /// The criterion is negative: an archetype must appear only after its
    /// threshold is crossed, never before. So the spawner asks
    /// DifficultyManager what is unlocked on every spawn rather than caching a
    /// list -- a cached list is how an archetype leaks in early after a power
    /// level drops and rises again, and the report would be "an enemy I
    /// shouldn't have seen yet", which nobody can reproduce.
    ///
    /// One pool per archetype, all bounded. A spawner is the single easiest
    /// place in an endless runner to leak instances forever, because nothing
    /// ever tells it to stop.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private DifficultyManager difficulty;

        [Tooltip("Prefab per archetype. An archetype with no prefab is skipped rather than spawned as nothing.")]
        [SerializeField] private List<ArchetypePrefab> prefabs = new List<ArchetypePrefab>();

        [Min(0.05f)]
        [SerializeField] private float secondsBetweenSpawns = 2f;

        [Tooltip("Hard cap on simultaneously live enemies, per archetype.")]
        [Min(1)]
        [SerializeField] private int poolCapacityPerArchetype = 12;

        [Tooltip("Where spawned enemies appear, relative to this transform.")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0f, 40f);

        [System.Serializable]
        public struct ArchetypePrefab
        {
            public EnemyDef archetype;
            public GameObject prefab;
        }

        private readonly Dictionary<EnemyDef, ObjectPool> pools = new Dictionary<EnemyDef, ObjectPool>();
        private float spawnTimer;

        /// <summary>Enemies spawned since this spawner woke. For soak tests.</summary>
        public int SpawnCount { get; private set; }

        /// <summary>
        /// Spawns skipped because every unlocked archetype was already past
        /// the ceiling unscaled. Surfaced rather than silent: a spawner that
        /// quietly stops is indistinguishable from a broken one.
        /// </summary>
        public int SkippedAsUnkillable { get; private set; }

        public DifficultyManager Difficulty
        {
            get => difficulty;
            set => difficulty = value;
        }

        public float SecondsBetweenSpawns
        {
            get => secondsBetweenSpawns;
            set => secondsBetweenSpawns = Mathf.Max(0.05f, value);
        }

        /// <summary>Live instances of one archetype. Never exceeds the capacity.</summary>
        public int ActiveCount(EnemyDef archetype)
        {
            return pools.TryGetValue(archetype, out ObjectPool pool) ? pool.ActiveCount : 0;
        }

        public int PoolCount(EnemyDef archetype)
        {
            return pools.TryGetValue(archetype, out ObjectPool pool) ? pool.Count : 0;
        }

        public void Register(EnemyDef archetype, GameObject prefab)
        {
            prefabs.Add(new ArchetypePrefab { archetype = archetype, prefab = prefab });
        }

        private void Update()
        {
            Tick(Time.deltaTime, CurrentPowerLevel(), CurrentPlayerDps());
        }

        /// <summary>
        /// Advances the spawn interval. Power level and DPS are arguments
        /// rather than looked up, so a wave soak runs in simulated time and so
        /// a test can hold the player at a chosen power level -- which is the
        /// only way to assert the threshold rule at the threshold.
        /// </summary>
        public void Tick(float deltaTime, float playerPowerLevel, float playerDps)
        {
            if (deltaTime <= 0f)
            {
                return;
            }
            spawnTimer -= deltaTime;
            // A loop, not an `if`: a hitch longer than the interval still owes
            // the wave the enemies that should have arrived during it.
            int guard = 0;
            while (spawnTimer <= 0f && guard++ < 32)
            {
                SpawnOne(playerPowerLevel, playerDps);
                spawnTimer += secondsBetweenSpawns;
            }
        }

        /// <summary>
        /// Spawns one enemy from the unlocked set. Returns null when nothing
        /// is spawnable, which is a legitimate early-run state rather than an
        /// error.
        /// </summary>
        public GameObject SpawnOne(float playerPowerLevel, float playerDps)
        {
            if (difficulty == null)
            {
                return null;
            }

            // Asked every time. A cached list is how an archetype leaks in
            // early after a power level drops and rises again.
            IReadOnlyList<EnemyDef> unlocked = difficulty.GetUnlockedArchetypes(playerPowerLevel);
            EnemyDef chosen = ChooseKillable(unlocked, playerDps);
            if (chosen == null)
            {
                return null;
            }

            GameObject prefab = PrefabFor(chosen);
            if (prefab == null)
            {
                // An archetype with no prefab is a wiring mistake. Skipping it
                // keeps the wave running; spawning nothing under its name
                // would look like the threshold rule failing.
                return null;
            }

            ObjectPool pool = PoolFor(chosen, prefab);
            GameObject instance = pool.Get();
            instance.transform.SetPositionAndRotation(
                transform.position + spawnOffset, transform.rotation);
            ApplyDifficulty(instance, chosen, playerPowerLevel, playerDps);
            SpawnCount++;
            return instance;
        }

        /// <summary>
        /// Prefers an archetype the player can actually kill inside the
        /// ceiling. DifficultyManager cannot scale one down below its authored
        /// stats, so choosing well here is the only lever left.
        /// </summary>
        private EnemyDef ChooseKillable(IReadOnlyList<EnemyDef> unlocked, float playerDps)
        {
            if (unlocked == null || unlocked.Count == 0)
            {
                return null;
            }

            DifficultyCurve curve = difficulty.Curve;
            EnemyDef fallback = null;
            var killable = new List<EnemyDef>();
            foreach (EnemyDef archetype in unlocked)
            {
                if (archetype == null)
                {
                    continue;
                }
                fallback ??= archetype;
                if (!DifficultyManager.ExceedsCeilingUnscaled(curve, playerDps, archetype.maxHealth))
                {
                    killable.Add(archetype);
                }
            }

            if (killable.Count > 0)
            {
                return killable[Random.Range(0, killable.Count)];
            }
            // Everything unlocked is already past the ceiling. Spawning the
            // weakest anyway beats spawning nothing -- an empty screen reads
            // as the game having stopped.
            SkippedAsUnkillable++;
            return fallback;
        }

        private void ApplyDifficulty(GameObject instance, EnemyDef archetype,
            float playerPowerLevel, float playerDps)
        {
            var health = instance.GetComponent<EnemyHealth>();
            if (health == null)
            {
                return;
            }
            health.Def = archetype;
            float multiplier = difficulty.ComputeStatMultiplier(
                playerPowerLevel, playerDps, archetype.maxHealth);
            // Scaled health is set on the instance, never written back to the
            // archetype asset -- the same rule PlayerStatsRuntime follows.
            health.SetScaledHealth(archetype.maxHealth * multiplier);
        }

        private GameObject PrefabFor(EnemyDef archetype)
        {
            foreach (ArchetypePrefab entry in prefabs)
            {
                if (entry.archetype == archetype)
                {
                    return entry.prefab;
                }
            }
            return null;
        }

        private ObjectPool PoolFor(EnemyDef archetype, GameObject prefab)
        {
            if (!pools.TryGetValue(archetype, out ObjectPool pool))
            {
                pool = new ObjectPool(prefab, poolCapacityPerArchetype, transform);
                pool.Prewarm(poolCapacityPerArchetype);
                pools[archetype] = pool;
            }
            return pool;
        }

        private float CurrentPowerLevel()
        {
            var powerUps = FindFirstObjectByType<JetFighter.PowerUp.PowerUpController>();
            return powerUps != null ? powerUps.PlayerPowerLevel : 0f;
        }

        private float CurrentPlayerDps()
        {
            var gun = FindFirstObjectByType<JetFighter.Weapon.PrimaryGunController>();
            if (gun == null || gun.WeaponDef == null)
            {
                return 0f;
            }
            return gun.EffectiveDamage / Mathf.Max(0.01f, gun.EffectiveCooldownSeconds);
        }
    }
}
