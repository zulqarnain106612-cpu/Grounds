using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Shared
{
    /// <summary>
    /// Reuses GameObjects instead of allocating them.
    ///
    /// Built in Phase 1 rather than retrofitted: continuous 1/sec fire over an
    /// endless run makes Instantiate/Destroy churn a guaranteed frame-time
    /// problem, and by the time it shows up on a device the fire path has
    /// callers everywhere. Cheaper to build once.
    ///
    /// The pool is bounded. An unbounded pool under a fire-rate power-up
    /// (Phase 3) is a slow memory leak that looks exactly like a pool doing
    /// its job, so exhaustion recycles the oldest live instance rather than
    /// allocating past the cap.
    /// </summary>
    public class ObjectPool
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly int capacity;
        private readonly Stack<GameObject> idle = new Stack<GameObject>();
        private readonly Queue<GameObject> live = new Queue<GameObject>();

        /// <summary>Instances created so far. Never exceeds the capacity.</summary>
        public int Count => idle.Count + live.Count;

        /// <summary>Instances currently handed out.</summary>
        public int ActiveCount => live.Count;

        public int Capacity => capacity;

        /// <summary>Instances recycled while still live because the pool was full.</summary>
        public int RecycleCount { get; private set; }

        public ObjectPool(GameObject prefab, int capacity, Transform parent = null)
        {
            this.prefab = prefab;
            this.capacity = Mathf.Max(1, capacity);
            this.parent = parent;
        }

        /// <summary>
        /// Fills the pool up front so the first shots do not allocate. Firing
        /// is the one moment the player is guaranteed to be looking.
        /// </summary>
        public void Prewarm(int count)
        {
            for (int i = 0; i < count && Count < capacity; i++)
            {
                GameObject instance = Object.Instantiate(prefab, parent);
                instance.SetActive(false);
                idle.Push(instance);
            }
        }

        /// <summary>
        /// An active instance. Allocates only while below capacity; at the cap
        /// it steals the oldest live one, which is the one that has had the
        /// most time to leave the screen.
        /// </summary>
        public GameObject Get()
        {
            GameObject instance;
            if (idle.Count > 0)
            {
                instance = idle.Pop();
            }
            else if (Count < capacity)
            {
                instance = Object.Instantiate(prefab, parent);
            }
            else
            {
                instance = live.Dequeue();
                RecycleCount++;
            }

            instance.SetActive(true);
            live.Enqueue(instance);
            return instance;
        }

        /// <summary>
        /// Returns an instance. Releasing something already idle, or something
        /// this pool never handed out, is ignored rather than corrupting the
        /// counts -- a double release is a caller bug that must not turn into
        /// a pool that reports more instances than exist.
        /// </summary>
        public void Release(GameObject instance)
        {
            if (instance == null || idle.Contains(instance) || !live.Contains(instance))
            {
                return;
            }
            RemoveFromLive(instance);
            instance.SetActive(false);
            idle.Push(instance);
        }

        private void RemoveFromLive(GameObject instance)
        {
            int remaining = live.Count;
            for (int i = 0; i < remaining; i++)
            {
                GameObject candidate = live.Dequeue();
                if (candidate != instance)
                {
                    live.Enqueue(candidate);
                }
            }
        }
    }
}
