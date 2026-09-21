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

        // Thresholds in MB, stated as what the code actually does: below 3GB
        // is Low, 5GB and above is High, and everything between is Medium.
        //
        // The comment these replace said "2GB is the lowest supported target;
        // 4GB and up gets the full budget", which neither number implements --
        // a 4GB device lands on Medium here. The numbers are kept rather than
        // moved to match the prose, because moving them would change which
        // budget real hardware gets on the strength of a comment rather than a
        // capture. They are Cycle 1 tuning (docs/PHASE1_TECHNICAL_SPEC.md
        // section 2) and QualityTierTests pins them, so tuning them is a
        // deliberate edit with a failing test attached rather than a drift.
        //
        // What the available hardware maps to under these: iPhone X (3GB) and
        // anything with 3-5GB is Medium; iPhone 14 Pro Max (6GB) and iPhone 17
        // (8GB) are High. Nothing on hand lands on Low, which is worth knowing
        // before phase6/perf-profiling-pass claims a "lowest supported device"
        // capture.
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
            return ClassifyMemory(SystemInfo.systemMemorySize);
        }

        /// <summary>
        /// The classification itself, given a memory figure in MB.
        ///
        /// Split out from <see cref="DetectTier"/> because SystemInfo cannot
        /// be varied from a test: with the read inlined, the only tier a
        /// suite could ever exercise was whichever one the runner happens to
        /// be, so every boundary below went unverified on every machine. A
        /// tier that misclassifies is not a crash -- it is a device quietly
        /// running the wrong budget, which is exactly the class of defect
        /// only a device capture would otherwise catch.
        ///
        /// A non-positive figure means SystemInfo could not answer. Medium is
        /// the deliberate response: guessing Low would throttle a capable
        /// device to 30fps on no evidence, and guessing High would hand a
        /// 2GB device a budget it cannot meet.
        /// </summary>
        public static Tier ClassifyMemory(int memoryMb)
        {
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
        /// Simultaneously active enemies allowed at a tier. Pure, so the
        /// numbers can be asserted without touching QualitySettings -- which
        /// is global editor state, and a test that wrote to it would leave
        /// the project dirty for every test after it.
        /// </summary>
        public static int BudgetFor(Tier tier)
        {
            switch (tier)
            {
                case Tier.Low:
                    return 12;
                case Tier.High:
                    return 48;
                default:
                    return 24;
            }
        }

        /// <summary>Target frame rate at a tier. Pure, for the same reason.</summary>
        public static int TargetFrameRateFor(Tier tier)
        {
            return tier == Tier.Low ? 30 : 60;
        }

        /// <summary>
        /// Applies the tier's budget. Also the entry point for the manual
        /// override required by the Phase 6 settings screen, so it must stay
        /// callable at any time, not only from Awake.
        /// </summary>
        public static void ApplySettings(Tier tier)
        {
            Current = tier;
            EnemyBudget = BudgetFor(tier);
            Application.targetFrameRate = TargetFrameRateFor(tier);
            QualitySettings.SetQualityLevel((int)tier, true);
        }
    }
}
