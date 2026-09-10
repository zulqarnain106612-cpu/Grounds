using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Enemy
{
    /// <summary>
    /// The scaling constants, as data.
    ///
    /// `k` and the time-to-kill ceiling are the two numbers this whole system
    /// turns on, and both will be retuned repeatedly against real play. In
    /// code they would be a build per adjustment; here they are an asset edit.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Difficulty Curve", fileName = "DifficultyCurve")]
    public class DifficultyCurve : ScriptableObject
    {
        [Serializable]
        public struct ArchetypeUnlock
        {
            public EnemyDef archetype;

            [Tooltip("Player power level at or above which this archetype may spawn.")]
            [Min(0f)]
            public float powerLevelThreshold;
        }

        [Tooltip("Scaling constant. statMultiplier = 1 + k * playerPowerLevel, before clamping.")]
        [Min(0f)]
        public float k = 0.35f;

        [Tooltip("Hard ceiling on time-to-kill, in seconds. The defeatability floor.")]
        [Min(0.1f)]
        public float timeToKillCeilingSeconds = 8f;

        [Tooltip("Enemy stats never fall below their authored values, however weak the player is.")]
        [Min(0.1f)]
        public float minimumMultiplier = 1f;

        [Tooltip("Archetypes and the power level that unlocks each. Variety, not just bigger numbers.")]
        public List<ArchetypeUnlock> unlocks = new List<ArchetypeUnlock>();
    }
}
