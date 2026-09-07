using UnityEngine;

namespace JetFighter.Weapon
{
    /// <summary>
    /// Data for one weapon. A ScriptableObject so balance is retunable
    /// without a code change -- Phase 3's fire-rate power-ups and Phase 2's
    /// damage variety both edit this, not the controller.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Weapon", fileName = "WeaponBase")]
    public class WeaponBase : ScriptableObject
    {
        [Tooltip("Shots per second. Phase 1 ships 1.0.")]
        [Min(0.01f)]
        public float fireRatePerSecond = 1f;

        [Tooltip("Pooled projectile.")]
        public GameObject projectilePrefab;

        [Tooltip("Pool identity, so two weapons sharing a projectile share a pool.")]
        public string poolTag = "primary_bullet";

        [Tooltip("Placeholder until Phase 2's damage pipeline exists.")]
        [Min(0f)]
        public float damage = 1f;

        [Tooltip("Hard cap on simultaneously live projectiles from this weapon.")]
        [Min(1)]
        public int poolCapacity = 32;

        [Tooltip("Projectile travel speed, in units/second.")]
        [Min(0.1f)]
        public float projectileSpeed = 40f;

        [Tooltip("Seconds before an unspent projectile returns itself to the pool.")]
        [Min(0.1f)]
        public float projectileLifetime = 3f;

        /// <summary>Seconds between shots. Guarded so a zero rate cannot divide by zero.</summary>
        public float CooldownSeconds => 1f / Mathf.Max(0.01f, fireRatePerSecond);
    }
}
