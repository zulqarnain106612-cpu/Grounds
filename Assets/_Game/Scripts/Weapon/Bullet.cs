using UnityEngine;
using JetFighter.Shared;

namespace JetFighter.Weapon
{
    /// <summary>
    /// A pooled projectile: travels, damages the first thing it hits, returns
    /// itself to its pool.
    ///
    /// Damage and lifetime are handed in by whatever fired it rather than read
    /// from a WeaponBase here. The gun already applies the player's damage
    /// multiplier (IPlayerStats), and a bullet that re-read the asset would
    /// silently ignore every power-up -- the shot's damage is decided when it
    /// is fired, not when it lands.
    ///
    /// Returning to the pool is this class's own responsibility because it is
    /// the only thing that knows the shot is over.
    ///
    /// The bullet does not advance itself. Whatever fired it calls Step with
    /// the same deltaTime it was ticked with, so the shot and the gun that
    /// fired it share one clock -- a bullet reading Time.deltaTime while the
    /// gun ran on a simulated step would outlive every soak.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Bullet : MonoBehaviour
    {
        /// <summary>Called when the bullet is finished. The pool's Release.</summary>
        public System.Action<GameObject> OnFinished;

        private float damage;
        private float speed;
        private float lifetimeRemaining;
        private bool spent;

        /// <summary>Damage this particular shot carries, fixed at fire time.</summary>
        public float Damage => damage;

        /// <summary>Already hit something or timed out, and awaiting release.</summary>
        public bool IsSpent => spent;

        /// <summary>
        /// Arms the bullet for one flight. Called by the gun on every Get, not
        /// once at spawn: a recycled bullet still carrying the previous shot's
        /// countdown would expire mid-screen.
        /// </summary>
        public void Launch(float damageAmount, float travelSpeed, float lifetimeSeconds)
        {
            damage = Mathf.Max(0f, damageAmount);
            speed = travelSpeed;
            lifetimeRemaining = Mathf.Max(0.01f, lifetimeSeconds);
            spent = false;
        }

        /// <summary>
        /// Advances the bullet and expires it when its lifetime runs out.
        /// Takes deltaTime so a soak can run in simulated time.
        ///
        /// The lifetime is not an optimisation. Without it a bullet that hits
        /// nothing never returns to the pool, so the pool drains, and the gun
        /// starts recycling live bullets in front of the player.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (spent)
            {
                return;
            }
            transform.position += transform.forward * (speed * deltaTime);
            lifetimeRemaining -= deltaTime;
            if (lifetimeRemaining <= 0f)
            {
                Finish();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleHit(other != null ? other.GetComponentInParent<IDamageable>() : null);
        }

        /// <summary>
        /// Applies this shot to one target. Public so the damage pipeline is
        /// testable without a physics collision, which needs colliders, layers
        /// and a running scene to produce.
        /// </summary>
        public void HandleHit(IDamageable target)
        {
            // A spent bullet passing through a second enemy on the same frame
            // would deal its damage twice, and the hit count the criterion
            // measures would stop matching the arithmetic.
            if (spent)
            {
                return;
            }
            if (target == null || target.IsDead)
            {
                // Terrain and corpses stop the bullet but take nothing. Not
                // stopping would let one shot chain through a whole formation.
                Finish();
                return;
            }
            target.ApplyDamage(damage);
            Finish();
        }

        /// <summary>Ends the flight and hands the instance back.</summary>
        public void Finish()
        {
            if (spent)
            {
                return;
            }
            spent = true;
            OnFinished?.Invoke(gameObject);
        }
    }
}
