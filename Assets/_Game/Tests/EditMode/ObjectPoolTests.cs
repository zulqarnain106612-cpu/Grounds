using NUnit.Framework;
using UnityEngine;
using JetFighter.Shared;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The pool's bound, which is the cell's acceptance criterion. A pool that
    /// grows under sustained fire is a memory leak that looks exactly like a
    /// pool doing its job.
    /// </summary>
    public class ObjectPoolTests
    {
        private GameObject prefab;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Bullet");
            prefab.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void GetReusesAReleasedInstance()
        {
            var pool = new ObjectPool(prefab, 8);
            GameObject first = pool.Get();
            pool.Release(first);
            Assert.AreSame(first, pool.Get());
            Assert.AreEqual(1, pool.Count, "the pool allocated instead of reusing");
        }

        [Test]
        public void GetActivatesAndReleaseDeactivates()
        {
            var pool = new ObjectPool(prefab, 4);
            GameObject instance = pool.Get();
            Assert.IsTrue(instance.activeSelf);
            pool.Release(instance);
            Assert.IsFalse(instance.activeSelf);
        }

        [Test]
        public void ThePoolNeverGrowsPastItsCapacity()
        {
            var pool = new ObjectPool(prefab, 4);
            for (int i = 0; i < 500; i++)
            {
                pool.Get();
            }
            Assert.AreEqual(4, pool.Count);
        }

        [Test]
        public void ExhaustionRecyclesTheOldestLiveInstance()
        {
            // The oldest is the one that has had the longest to leave the
            // screen, so recycling it is the least visible choice.
            var pool = new ObjectPool(prefab, 2);
            GameObject oldest = pool.Get();
            pool.Get();
            Assert.AreSame(oldest, pool.Get());
            Assert.AreEqual(1, pool.RecycleCount);
        }

        [Test]
        public void PrewarmFillsWithoutHandingAnythingOut()
        {
            var pool = new ObjectPool(prefab, 6);
            pool.Prewarm(6);
            Assert.AreEqual(6, pool.Count);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void PrewarmRespectsTheCapacity()
        {
            var pool = new ObjectPool(prefab, 3);
            pool.Prewarm(50);
            Assert.AreEqual(3, pool.Count);
        }

        [Test]
        public void ADoubleReleaseDoesNotInventInstances()
        {
            var pool = new ObjectPool(prefab, 4);
            GameObject instance = pool.Get();
            pool.Release(instance);
            pool.Release(instance);
            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void ReleasingAStrangerIsIgnored()
        {
            var pool = new ObjectPool(prefab, 4);
            var stranger = new GameObject("NotFromThisPool");
            pool.Release(stranger);
            Assert.AreEqual(0, pool.Count);
            Object.DestroyImmediate(stranger);
        }

        [Test]
        public void ReleasingNullIsIgnored()
        {
            var pool = new ObjectPool(prefab, 4);
            Assert.DoesNotThrow(() => pool.Release(null));
        }

        [Test]
        public void ACapacityBelowOneIsRaisedRatherThanTrusted()
        {
            var pool = new ObjectPool(prefab, 0);
            Assert.AreEqual(1, pool.Capacity);
            Assert.IsNotNull(pool.Get());
        }

        [Test]
        public void ASoakOfGetAndReleaseStaysAtOneInstance()
        {
            var pool = new ObjectPool(prefab, 32);
            for (int i = 0; i < 5000; i++)
            {
                pool.Release(pool.Get());
            }
            Assert.AreEqual(1, pool.Count, "steady-state fire allocated more than it needed");
            Assert.AreEqual(0, pool.RecycleCount);
        }
    }
}
