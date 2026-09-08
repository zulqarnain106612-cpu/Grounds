using UnityEngine;

namespace JetFighter.Build
{
    /// <summary>
    /// Detects a device performance tier once at launch and applies the
    /// matching render budget. Cycle 0 ships detection and the application
    /// seam only; the per-tier budgets (particle density, shadow quality,
    /// on-screen enemy cap) are tuned in Cycle 1 against real device
    /// captures -- see docs/PHASE1_TECHNICAL_SPEC.md section 2.
    /// </summary>
    public class QualityTierManager : MonoBehaviour
    {
        public enum Tier
        {
            Low = 0,
            Medium = 1,
            High = 2
        }

        /// <summary>Tier resolved at launch. Medium until Awake runs.</summary>
        public static Tier Current { get; private set; } = Tier.Medium;

        /// <summary>Max simultaneously active enemies for the current tier.</summary>
        public static int EnemyBudget { get; private set; } = 24;

        // Thresholds in MB. A 2GB device is the lowest supported target
        // (roadmap section 10); 4GB and up gets the full budget.
        private const int LowMemoryCeilingMb = 3072;
        private const int HighMemoryFloorMb = 5120;

        private void Awake()
        {
            Current = DetectTier();
            ApplySettings(Current);
            Debug.Log($"[QualityTierManager] tier={Current} memory={SystemInfo.systemMemorySize}MB gfx={SystemInfo.graphicsDeviceType}");
        }

        /// <summary>
        /// Classifies the device from system memory. Memory is used rather
        /// than a GPU allow-list because an allow-list goes stale with every
        /// new device, while memory keeps ranking correctly on hardware that
        /// did not exist when this shipped.
        /// </summary>
        public static Tier DetectTier()
        {
            int memoryMb = SystemInfo.systemMemorySize;
            if (memoryMb <= 0)
            {
                return Tier.Medium;
            }
            if (memoryMb < LowMemoryCeilingMb)
            {
                return Tier.Low;
            }
            if (memoryMb >= HighMemoryFloorMb)
            {
                return Tier.High;
            }
            return Tier.Medium;
        }

        /// <summary>
        /// Applies the tier's budget. Also the entry point for the manual
        /// override required by the Phase 6 settings screen, so it must stay
        /// callable at any time, not only from Awake.
        /// </summary>
        public static void ApplySettings(Tier tier)
        {
            Current = tier;
            switch (tier)
            {
                case Tier.Low:
                    EnemyBudget = 12;
                    Application.targetFrameRate = 30;
                    break;
                case Tier.High:
                    EnemyBudget = 48;
                    Application.targetFrameRate = 60;
                    break;
                default:
                    EnemyBudget = 24;
                    Application.targetFrameRate = 60;
                    break;
            }
            QualitySettings.SetQualityLevel((int)tier, true);
        }
    }
}
