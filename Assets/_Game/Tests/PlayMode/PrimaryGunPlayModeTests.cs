using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JetFighter.Weapon;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The gun under the real player loop, where Update drives the cooldown
    /// and the pool holds real GameObjects.
    ///
    /// The EditMode suite covers cadence arithmetic; this covers what only a
    /// running scene shows -- that bullets appear at the muzzle, that the
    /// pool's objects are reused rather than replaced, and that nothing is
    /// destroyed on the fire path.
    /// </summary>
    public class PrimaryGunPlayModeTests
    {
        private GameObject gunObject;
        private GameObject prefab;
        private PrimaryGunController gun;
        private WeaponBase weapon;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Bullet");
            prefab.SetActive(false);

            weapon = ScriptableObject.CreateInstance<WeaponBase>();
            weapon.fireRatePerSecond = 20f;
            weapon.projectilePrefab = prefab;
            weapon.poolCapacity = 6;

            gunObject = new GameObject("Gun");
            gunObject.transform.position = new Vector3(2f, 3f, 0f);
            gun = gunObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;
            // Off by default here so each test places its own shots; the
            // autofire test turns it back on explicitly.
            gun.AutoFire = false;
            gun.EnsurePool();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gunObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(weapon);
        }

        [UnityTest]
        public IEnumerator ThePoolIsPrewarmedBeforeTheFirstShot()
        {
            // The first shot is the one moment the player is guaranteed to be
            // watching. An Instantiate spike there is the one they remember.
            yield return null;
            Assert.AreEqual(weapon.poolCapacity, gun.Pool.Count);
            Assert.AreEqual(0, gun.Pool.ActiveCount);
        }

        [UnityTest]
        public IEnumerator BulletsSpawnAtTheMuzzleNotAtTheOrigin()
        {
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gunObject.transform);
            muzzle.position = new Vector3(9f, 9f, 0f);
            gun.MuzzleTransform = muzzle;

            gun.Fire();
            yield return null;

            GameObject bullet = null;
            foreach (Transform child in gunObject.transform)
            {
                if (child.gameObject.activeSelf && child != muzzle)
                {
                    bullet = child.gameObject;
                }
            }
            Assert.IsNotNull(bullet, "nothing was taken from the pool");
            Assert.AreEqual(muzzle.position, bullet.transform.position);
        }

        [UnityTest]
        public IEnumerator SustainedFireNeverExceedsTheCapacity()
        {
            gun.WeaponDef = weapon;
            gun.EnsurePool();
            for (int i = 0; i < 400; i++)
            {
                gun.Tick(0.05f);
            }
            yield return null;
            Assert.AreEqual(weapon.poolCapacity, gun.Pool.Count);
            Assert.Greater(gun.ShotsFired, 300);
        }

        [UnityTest]
        public IEnumerator NothingOnTheFirePathIsDestroyed()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                gun.Fire();
                foreach (Transform child in gunObject.transform)
                {
                    seen.Add(child.gameObject.GetInstanceID());
                }
            }
            yield return null;

            // Distinct instance ids can never exceed the cap: a destroyed and
            // re-instantiated bullet would show up as a new id.
            Assert.LessOrEqual(seen.Count, weapon.poolCapacity);
        }

        [UnityTest]
        public IEnumerator AutoFireRunsFromUpdateWithoutBeingDriven()
        {
            gun.AutoFire = true;
            int before = gun.ShotsFired;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(gun.ShotsFired, before, "the gun only fires when a test calls Tick");
        }
    }
}
