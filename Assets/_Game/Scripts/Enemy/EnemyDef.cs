using UnityEngine;

namespace JetFighter.Enemy
{
    /// <summary>
    /// One enemy archetype, as data.
    ///
    /// Phase 3 adds a drop table and difficulty scaling reads from here. That
    /// field is deliberately absent now: this phase proves the
    /// damage/health/UI pipeline, and a data shape carrying fields nothing
    /// reads is how a schema quietly becomes fiction.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Enemy", fileName = "EnemyDef")]
    public class EnemyDef : ScriptableObject
    {
        [Min(1f)]
        public float maxHealth = 10f;

        [Tooltip("Damage this enemy deals to the player on contact.")]
        [Min(0f)]
        public float damagePerHit = 1f;

        [Tooltip("Approach speed, in units/second.")]
        [Min(0f)]
        public float moveSpeed = 4f;

        [Tooltip("Distance from its target at which the enemy stops approaching and loiters.")]
        [Min(0f)]
        public float loiterDistance = 8f;
    }
}
