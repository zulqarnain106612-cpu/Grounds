using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JetFighter.Enemy;
using JetFighter.Weapon;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// Gun to pool to bullet to enemy, in a running scene.
    ///
    /// The EditMode suite covers the arithmetic. This covers what only a
    /// scene shows: that spent bullets actually come back to the pool, that
    /// the pool therefore stays bounded under sustained fire at real targets,
    /// and that a real trigger collision reaches IDamageable.
    /// </summary>
    public class BulletPipelinePlayModeTests
    {
        private GameObject gunObject;
        private GameObject prefab;
        private GameObject enemyObject;
        private PrimaryGunController gun;
        private EnemyHealth enemy;
        private WeaponBase weapon;
        private EnemyDef def;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("BulletPrefab");
            prefab.AddComponent<Rigidbody>().isKinematic = true;
            prefab.AddComponent<SphereCollider>().isTrigger = true;
            prefab.AddComponent<Bullet>();
            prefab.SetActive(false);

            weapon = ScriptableObject.CreateInstance<WeaponBase>();
            weapon.fireRatePerSecond = 10f;
            weapon.damage = 2f;
            weapon.projectilePrefab = prefab;
            weapon.poolCapacity = 5;
            weapon.projectileSpeed = 0f;
            weapon.projectileLifetime = 0.5f;

            def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 10f;

            enemyObject = new GameObject("Enemy");
            enemy = enemyObject.AddComponent<EnemyHealth>();
            enemy.Def = def;

            gunObject = new GameObject("Gun");
            gun = gunObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;
            // Every shot in this file is placed deliberately; autofire would
            // add bullets between the assertions.
            gun.AutoFire = false;
            gun.EnsurePool();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gunObject);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(weapon);
            Object.DestroyImmediate(def);
        }

        [UnityTest]
        public IEnumerator AFiredBulletCarriesTheWeaponsDamage()
        {
            gun.Fire();
            yield return null;

            Bullet fired = gunObject.GetComponentInChildren<Bullet>();
            Assert.IsNotNull(fired, "nothing came out of the pool");
            Assert.AreEqual(2f, fired.Damage, 1e-4f);
        }

        [UnityTest]
        public IEnumerator TheFiredBulletCarriesThePlayersMultiplier()
        {
            gun.Stats = new DoubleDamage();
            gun.Fire();
            yield return null;

            Bullet fired = gunObject.GetComponentInChildren<Bullet>();
            Assert.AreEqual(4f, fired.Damage, 1e-4f, "the power-up did not reach the shot");
        }

        private sealed class DoubleDamage : JetFighter.Player.IPlayerStats
        {
            public float DamageMultiplier => 2f;
            public float FireRateMultiplier => 1f;
        }

        [UnityTest]
        public IEnumerator ASpentBulletGoesBackToThePool()
        {
            gun.Fire();
            yield return null;
            Assert.AreEqual(1, gun.Pool.ActiveCount);

            gunObject.GetComponentInChildren<Bullet>().Finish();
            yield return null;
            Assert.AreEqual(0, gun.Pool.ActiveCount, "the bullet never came back");
        }

        [UnityTest]
        public IEnumerator MissedBulletsExpireSoThePoolNeverStarves()
        {
            // Nothing is hit here. Without the lifetime the pool drains and
            // the gun starts recycling live bullets in front of the player.
            for (int i = 0; i < 40; i++)
            {
                gun.Tick(0.1f);
                yield return null;
            }
            Assert.AreEqual(weapon.poolCapacity, gun.Pool.Count);
            Assert.AreEqual(0, gun.Pool.RecycleCount,
                "the pool had to steal live bullets, so expiry is not returning them");
        }

        [UnityTest]
        public IEnumerator ARealTriggerCollisionDamagesTheEnemy()
        {
            enemyObject.AddComponent<BoxCollider>();
            var body = enemyObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            enemyObject.transform.position = Vector3.zero;

            weapon.projectileSpeed = 0f;
            gun.Fire();
            Bullet fired = gunObject.GetComponentInChildren<Bullet>();
            fired.transform.position = Vector3.zero;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Less(enemy.CurrentHealth, 10f, "the collision never reached IDamageable");
        }

        [UnityTest]
        public IEnumerator TheGunKillsTheEnemyInThePredictedNumberOfShots()
        {
            // 10 health, 2 damage: five shots. The criterion, end to end.
            for (int i = 0; i < 5; i++)
            {
                gun.Fire();
                Bullet fired = gunObject.GetComponentInChildren<Bullet>();
                fired.HandleHit(enemy);
                yield return null;
            }
            Assert.IsTrue(enemy.IsDead);
        }
    }
}
