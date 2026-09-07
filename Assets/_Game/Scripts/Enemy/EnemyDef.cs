using UnityEngine;

namespace JetFighter.Enemy
{
    /// <summary>
    /// One enemy archetype, as data.
    ///
    /// The drop table arrives here in Cycle 3 rather than in a lookup keyed
    /// by enemy type somewhere in code: a designer retuning drop rates opens
    /// the asset that already describes the enemy and edits the numbers next
    /// to it, with no code change anywhere.
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

        [Tooltip("What this enemy may drop, as weights. Data only -- retuning needs no code change.")]
        public DropTable dropTable = new DropTable();
    }
}
