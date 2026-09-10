using UnityEngine;
using JetFighter.Shared;

namespace JetFighter.Weapon
{
    /// <summary>
    /// A pooled homing missile.
    ///
    /// Steering is arcade, not simulation: a capped turn rate toward the
    /// target rather than proportional navigation. The roadmap asks for feel
    /// over fidelity here, and a physically guided missile is also
    /// unpredictable to balance -- the designer needs "it hits in about a
    /// second", not a launch envelope.
    ///
    /// Shares Bullet's contract with the pool: it arms on Launch and hands
    /// itself back exactly once, because it is the only thing that knows the
    /// flight is over.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MissileController : MonoBehaviour
    {
        /// <summary>Called when the missile is finished. The pool's Release.</summary>
        public System.Action<GameObject> OnFinished;

        private Transform target;
        private float damage;
        private float speed;
        private float turnDegreesPerSecond;
        private float lifetimeRemaining;
        private float hitRadius;
        private bool spent;

        public Transform Target => target;

        public float Damage => damage;

        public bool IsSpent => spent;

        /// <summary>
        /// Arms the missile for one flight. Called on every Get from the
        /// pool, not once at spawn: a recycled missile still chasing the last
        /// target would fly off the moment it launched.
        /// </summary>
        public void Launch(Transform lockedTarget, float damageAmount, float travelSpeed,
            float turnRateDegreesPerSecond, float lifetimeSeconds, float impactRadius)
        {
            target = lockedTarget;
            damage = Mathf.Max(0f, damageAmount);
            speed = Mathf.Max(0f, travelSpeed);
            turnDegreesPerSecond = Mathf.Max(0f, turnRateDegreesPerSecond);
            lifetimeRemaining = Mathf.Max(0.01f, lifetimeSeconds);
            hitRadius = Mathf.Max(0.01f, impactRadius);
            spent = false;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <summary>
        /// One step of flight: steer, advance, then test for impact or
        /// expiry. Takes deltaTime so a soak runs in simulated time.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (spent || deltaTime <= 0f)
            {
                return;
            }

            lifetimeRemaining -= deltaTime;

            // A target that died or was pooled away mid-flight leaves the
            // missile with a null or inactive transform. It keeps its heading
            // and expires rather than freezing in the air or throwing.
            if (target != null && target.gameObject.activeInHierarchy)
            {
                transform.rotation = Steer(transform.rotation, transform.position,
                    target.position, turnDegreesPerSecond, deltaTime);

                if (Vector3.Distance(transform.position, target.position) <= hitRadius)
                {
                    Detonate();
                    return;
                }
            }

            transform.position += transform.forward * (speed * deltaTime);

            // Checked after the move as well: at 60 units/second and a 1-unit
            // radius, a 60fps step is exactly the radius, so a
            // before-move-only test tunnels straight through the target.
            if (target != null && target.gameObject.activeInHierarchy
                && Vector3.Distance(transform.position, target.position) <= hitRadius)
            {
                Detonate();
                return;
            }

            if (lifetimeRemaining <= 0f)
            {
                Finish();
            }
        }

        /// <summary>Applies damage and ends the flight.</summary>
        public void Detonate()
        {
            if (spent)
            {
                return;
            }
            var damageable = target != null ? target.GetComponentInParent<IDamageable>() : null;
            if (damageable != null && !damageable.IsDead)
            {
                damageable.ApplyDamage(damage);
            }
            Finish();
        }

        /// <summary>Ends the flight and hands the instance back, exactly once.</summary>
        public void Finish()
        {
            if (spent)
            {
                return;
            }
            spent = true;
            target = null;
            OnFinished?.Invoke(gameObject);
        }

        /// <summary>
        /// Rotation after one step of steering, capped at the turn rate.
        ///
        /// Pure and static so the guidance is a unit test rather than
        /// something someone watches. The cap is what makes a missile
        /// dodgeable and therefore a weapon rather than a guarantee; without
        /// it the missile simply teleports its heading onto the target every
        /// frame.
        /// </summary>
        public static Quaternion Steer(Quaternion current, Vector3 position, Vector3 targetPosition,
            float turnDegreesPerSecond, float deltaTime)
        {
            Vector3 toTarget = targetPosition - position;
            if (toTarget.sqrMagnitude <= Mathf.Epsilon)
            {
                return current;
            }
            Quaternion desired = Quaternion.LookRotation(toTarget);
            return Quaternion.RotateTowards(current, desired, turnDegreesPerSecond * deltaTime);
        }
    }
}
