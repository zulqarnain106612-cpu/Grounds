using System;
using System.Collections.Generic;
using UnityEngine;
using JetFighter.PowerUp;

namespace JetFighter.Enemy
{
    /// <summary>
    /// What an enemy may drop, as weights.
    ///
    /// Balancing lives entirely in data: retuning drop rates must never need a
    /// code change, which is the cell's stated criterion. So the table is a
    /// serializable field on EnemyDef rather than a lookup keyed by enemy type
    /// somewhere in code -- a designer opens the asset that already describes
    /// the enemy and edits the numbers next to it.
    ///
    /// Weights rather than percentages. Percentages have to sum to 100 and
    /// break every other row when one is edited; weights are independent, so
    /// doubling one entry's weight is a local change with an obvious meaning.
    /// </summary>
    [Serializable]
    public class DropTable
    {
        [Serializable]
        public struct Entry
        {
            public PowerUpDef powerUp;

            [Tooltip("Relative weight. Doubling this doubles the entry's share; it is not a percentage.")]
            [Min(0f)]
            public float weight;
        }

        [Tooltip("Chance this enemy drops anything at all, 0..1. Applied before the table is rolled.")]
        [Range(0f, 1f)]
        public float dropChance = 0.25f;

        public List<Entry> entries = new List<Entry>();

        /// <summary>Sum of every usable weight. Zero means the table can never drop.</summary>
        public float TotalWeight
        {
            get
            {
                float total = 0f;
                foreach (Entry entry in entries)
                {
                    if (entry.powerUp != null && entry.weight > 0f)
                    {
                        total += entry.weight;
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// Rolls the table. Returns null when nothing drops.
        ///
        /// The roll is passed in rather than taken from UnityEngine.Random so
        /// the distribution is testable and so Phase 4 can drive both clients
        /// from one seed -- a host-authoritative drop that each client rolled
        /// separately is a desync with loot in it.
        /// </summary>
        public PowerUpDef Roll(float dropRoll, float weightRoll)
        {
            if (dropRoll >= dropChance)
            {
                return null;
            }
            return Select(weightRoll);
        }

        /// <summary>
        /// Picks an entry from the weighted set. <paramref name="weightRoll"/>
        /// is 0..1 and is scaled to the total, so callers never need to know
        /// the weights.
        /// </summary>
        public PowerUpDef Select(float weightRoll)
        {
            float total = TotalWeight;
            if (total <= 0f)
            {
                // An empty or all-zero table is a configuration state, not an
                // error: an enemy that drops nothing is a legitimate design.
                return null;
            }

            float target = Mathf.Clamp01(weightRoll) * total;
            float cursor = 0f;
            foreach (Entry entry in entries)
            {
                if (entry.powerUp == null || entry.weight <= 0f)
                {
                    continue;
                }
                cursor += entry.weight;
                if (target < cursor)
                {
                    return entry.powerUp;
                }
            }

            // Only reachable when weightRoll lands exactly on 1.0, where
            // `target < cursor` is false for the final entry. Returning the
            // last usable entry keeps the distribution exact rather than
            // silently dropping nothing once in every few million kills.
            return LastUsable();
        }

        private PowerUpDef LastUsable()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].powerUp != null && entries[i].weight > 0f)
                {
                    return entries[i].powerUp;
                }
            }
            return null;
        }
    }
}
