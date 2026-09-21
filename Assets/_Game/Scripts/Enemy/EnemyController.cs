using UnityEngine;

namespace JetFighter.Enemy
{
    /// <summary>
    /// One movement pattern: approach the target, then hold at loiter
    /// distance.
    ///
    /// Enemy variety and any real FSM are Phase 3. This phase proves the
    /// damage/health/UI pipeline, so the movement here exists to give that
    /// pipeline something that moves, and no more.
    /// </summary>
    [RequireComponent(typeof(EnemyHealth))]
    public class EnemyController : MonoBehaviour
    {
        [SerializeField] private EnemyDef def;
        [SerializeField] private Transform target;

        private EnemyHealth health;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public EnemyDef Def
        {
            get => def;
            set => def = value;
        }

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>
        /// Advances one step. Takes deltaTime so approach behaviour can be
        /// tested over simulated seconds instead of real ones.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (def == null || target == null || (health != null && health.IsDead))
            {
                return;
            }
            transform.position = NextPosition(
                transform.position, target.position, def.moveSpeed, def.loiterDistance, deltaTime);
        }

        /// <summary>
        /// Where the enemy should be after one step.
        ///
        /// Pure and static so the loiter behaviour is a unit test rather than
        /// a thing someone watches. The subtle case is overshoot: moving at
        /// speed * dt toward the target can cross the loiter ring on a long
        /// frame, and an enemy that oscillates through its hold distance
        /// reads as jitter, not as AI.
        /// </summary>
        public static Vector3 NextPosition(Vector3 current, Vector3 targetPosition,
            float speed, float loiterDistance, float deltaTime)
        {
            if (deltaTime <= 0f || speed <= 0f)
            {
                return current;
            }

            Vector3 toTarget = targetPosition - current;
            float distance = toTarget.magnitude;
            if (distance <= loiterDistance || distance <= Mathf.Epsilon)
            {
                return current;
            }

            float step = Mathf.Min(speed * deltaTime, distance - loiterDistance);
            return current + toTarget / distance * step;
        }
    }
}
