using NUnit.Framework;
using UnityEngine;
using JetFighter.Enemy;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion is negative: an archetype appears only after its
    /// threshold is crossed, never before.
    ///
    /// Power level is an argument to Tick rather than looked up, which is the
    /// only way to hold the player exactly at a threshold and assert both
    /// sides of it.
    /// </summary>
    public class EnemySpawnerTests
    {
        private GameObject spawnerObject;
        private EnemySpawner spawner;
        private DifficultyManager difficulty;
        private DifficultyCurve curve;
        private EnemyDef basic;
        private EnemyDef heavy;
        private GameObject basicPrefab;
        private GameObject heavyPrefab;

        private const float Dps = 50f;

        [SetUp]
        public void SetUp()
        {
            curve = ScriptableObject.CreateInstance<DifficultyCurve>();
            curve.k = 0.5f;
            curve.timeToKillCeilingSeconds = 8f;
            curve.minimumMultiplier = 1f;

            basic = ScriptableObject.CreateInstance<EnemyDef>();
            basic.maxHealth = 10f;
            heavy = ScriptableObject.CreateInstance<EnemyDef>();
            heavy.maxHealth = 30f;

            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = basic, powerLevelThreshold = 0f });
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = heavy, powerLevelThreshold = 2f });

            basicPrefab = MakePrefab("BasicEnemy");
            heavyPrefab = MakePrefab("HeavyEnemy");

            spawnerObject = new GameObject("Spawner");
            difficulty = spawnerObject.AddComponent<DifficultyManager>();
            difficulty.Curve = curve;
            spawner = spawnerObject.AddComponent<EnemySpawner>();
            spawner.Difficulty = difficulty;
            spawner.SecondsBetweenSpawns = 1f;
            spawner.Register(basic, basicPrefab);
            spawner.Register(heavy, heavyPrefab);
        }

        private static GameObject MakePrefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.AddComponent<EnemyHealth>();
            prefab.SetActive(false);
            return prefab;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(spawnerObject);
            Object.DestroyImmediate(basicPrefab);
            Object.DestroyImmediate(heavyPrefab);
            Object.DestroyImmediate(curve);
            Object.DestroyImmediate(basic);
            Object.DestroyImmediate(heavy);
        }

        private int SpawnMany(int count, float powerLevel)
        {
            int heavies = 0;
            for (int i = 0; i < count; i++)
            {
                GameObject spawned = spawner.SpawnOne(powerLevel, Dps);
                if (spawned != null && spawned.name.StartsWith("HeavyEnemy"))
                {
                    heavies++;
                }
                if (spawned != null)
                {
                    spawned.SetActive(false);
                }
            }
            return heavies;
        }

        [Test]
        public void ALockedArchetypeNeverSpawns()
        {
            // The criterion. 200 attempts below the threshold, none of which
            // may produce the heavy.
            Assert.AreEqual(0, SpawnMany(200, 1.99f));
        }

        [Test]
        public void AnArchetypeSpawnsOnceItsThresholdIsReached()
        {
            Assert.Greater(SpawnMany(200, 2f), 0, "the heavy never appeared at its threshold");
        }

        [Test]
        public void AnArchetypeThatUnlockedDoesNotLeakBackInWhenPowerFalls()
        {
            // A cached unlock list is how an archetype leaks in early after a
            // power level drops and rises again -- and the report is "an enemy
            // I shouldn't have seen yet", which nobody can reproduce.
            SpawnMany(20, 5f);
            Assert.AreEqual(0, SpawnMany(200, 0.5f));
        }

        [Test]
        public void NothingSpawnsBelowEveryThreshold()
        {
            curve.unlocks.Clear();
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = heavy, powerLevelThreshold = 5f });
            Assert.IsNull(spawner.SpawnOne(0f, Dps));
            Assert.AreEqual(0, spawner.SpawnCount);
        }

        [Test]
        public void SpawnedEnemiesCarryTheDifficultyMultiplier()
        {
            GameObject spawned = spawner.SpawnOne(4f, Dps);
            var health = spawned.GetComponent<EnemyHealth>();
            float expected = basic.maxHealth *
                DifficultyManager.ComputeStatMultiplier(curve, 4f, Dps, basic.maxHealth);
            Assert.AreEqual(expected, health.MaxHealth, 1e-3f);
            Assert.AreEqual(expected, health.CurrentHealth, 1e-3f);
        }

        [Test]
        public void ScalingNeverWritesBackToTheArchetypeAsset()
        {
            // The same rule PlayerStatsRuntime follows: a ScriptableObject
            // edited in play mode keeps the change in the editor, so one
            // scaled spawn during testing becomes the archetype's health.
            spawner.SpawnOne(20f, Dps);
            Assert.AreEqual(10f, basic.maxHealth, 1e-4f);
        }

        [Test]
        public void ARecycledEnemyIsRescaledRatherThanKeepingItsOldHealth()
        {
            GameObject first = spawner.SpawnOne(0f, Dps);
            float unscaled = first.GetComponent<EnemyHealth>().MaxHealth;
            first.SetActive(false);

            GameObject second = spawner.SpawnOne(6f, Dps);
            Assert.Greater(second.GetComponent<EnemyHealth>().MaxHealth, unscaled,
                "the recycled enemy kept the previous spawn's health");
        }

        [Test]
        public void ThePoolStaysBoundedOverASoak()
        {
            // A spawner is the easiest place in an endless runner to leak
            // instances forever, because nothing ever tells it to stop.
            for (int i = 0; i < 5000; i++)
            {
                spawner.Tick(0.5f, 0f, Dps);
            }
            Assert.LessOrEqual(spawner.PoolCount(basic), 12);
            Assert.Greater(spawner.SpawnCount, 1000);
        }

        [Test]
        public void AHitchStillOwesTheWaveItsEnemies()
        {
            spawner.Tick(5.5f, 0f, Dps);
            Assert.AreEqual(6, spawner.SpawnCount, 1);
        }

        [Test]
        public void AnEnormousHitchCannotHangTheFrame()
        {
            spawner.Tick(100000f, 0f, Dps);
            Assert.LessOrEqual(spawner.SpawnCount, 33);
        }

        [Test]
        public void TheIntervalIsRespected()
        {
            for (int i = 0; i < 60; i++)
            {
                spawner.Tick(1f / 60f, 0f, Dps);
            }
            Assert.AreEqual(1, spawner.SpawnCount, 1);
        }

        [Test]
        public void AnArchetypeWithNoPrefabIsSkippedRatherThanSpawnedAsNothing()
        {
            // A wiring mistake must not look like the threshold rule failing.
            var orphan = ScriptableObject.CreateInstance<EnemyDef>();
            orphan.maxHealth = 5f;
            curve.unlocks.Clear();
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = orphan, powerLevelThreshold = 0f });

            Assert.IsNull(spawner.SpawnOne(0f, Dps));
            Object.DestroyImmediate(orphan);
        }

        [Test]
        public void AKillableArchetypeIsPreferredOverAnUnkillableOne()
        {
            // DifficultyManager cannot scale an enemy below its authored
            // stats, so choosing well here is the only lever left.
            var tank = ScriptableObject.CreateInstance<EnemyDef>();
            tank.maxHealth = 5000f;
            var tankPrefab = MakePrefab("TankEnemy");
            spawner.Register(tank, tankPrefab);
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = tank, powerLevelThreshold = 0f });

            for (int i = 0; i < 100; i++)
            {
                GameObject spawned = spawner.SpawnOne(0f, 1f);
                Assert.IsFalse(spawned.name.StartsWith("TankEnemy"),
                    "spawned an enemy the player cannot kill inside the ceiling");
                spawned.SetActive(false);
            }

            Object.DestroyImmediate(tankPrefab);
            Object.DestroyImmediate(tank);
        }

        [Test]
        public void AnAllUnkillableWaveStillSpawnsAndSaysSo()
        {
            // An empty screen reads as the game having stopped, so the
            // weakest is spawned anyway -- and the condition is reported
            // rather than left silent.
            curve.unlocks.Clear();
            var tank = ScriptableObject.CreateInstance<EnemyDef>();
            tank.maxHealth = 5000f;
            var tankPrefab = MakePrefab("TankEnemy");
            spawner.Register(tank, tankPrefab);
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = tank, powerLevelThreshold = 0f });

            Assert.IsNotNull(spawner.SpawnOne(0f, 1f));
            Assert.AreEqual(1, spawner.SkippedAsUnkillable);

            Object.DestroyImmediate(tankPrefab);
            Object.DestroyImmediate(tank);
        }

        [Test]
        public void ASpawnerWithNoDifficultyManagerIsInertRatherThanThrowing()
        {
            spawner.Difficulty = null;
            Assert.IsNull(spawner.SpawnOne(10f, Dps));
        }
    }
}
