using UnityEngine;

namespace JetFighter.PowerUp
{
    /// <summary>
    /// One power-up, as data.
    ///
    /// Balancing lives here so retuning never needs a code change -- the same
    /// principle the drop tables and the difficulty curve follow.
    /// </summary>
    [CreateAssetMenu(menuName = "JetFighter/Power-Up", fileName = "PowerUpDef")]
    public class PowerUpDef : ScriptableObject
    {
        public enum PowerUpType
        {
            Speed = 0,
            FireRate = 1,
            Damage = 2,
        }

        public PowerUpType type = PowerUpType.FireRate;

        [Tooltip("Multiplier applied to the stat. 1.5 is +50%. Values at or below 1 are ignored at runtime.")]
        [Min(0.01f)]
        public float magnitude = 1.5f;

        [Tooltip("Seconds the effect lasts. 0 means permanent for the run (ADR-003).")]
        [Min(0f)]
        public float duration = 10f;

        /// <summary>ADR-003's sentinel, named so no call site has to remember it.</summary>
        public bool IsPermanentForRun => duration <= 0f;
    }
}
