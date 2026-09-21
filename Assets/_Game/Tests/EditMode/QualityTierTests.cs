using NUnit.Framework;
using JetFighter.Build;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The tier a device runs at, and the budgets that follow from it.
    ///
    /// None of this was testable before the seam: DetectTier read
    /// SystemInfo.systemMemorySize inline, so a suite could only ever
    /// exercise whichever tier the runner happened to be -- one of three, and
    /// never the boundaries. A misclassification is not a crash; it is a
    /// device quietly running the wrong budget, visible only to somebody
    /// holding it. That is exactly the acceptance this project refuses to
    /// depend on, so the classification is pinned here instead.
    ///
    /// The thresholds are Cycle 1 tuning against device captures. Pinning
    /// them does not freeze them -- it makes moving them a deliberate edit
    /// with a failing test attached, rather than a drift nobody notices.
    /// </summary>
    public class QualityTierTests
    {
        private const int LowCeilingMb = 3072;   // below this -> Low
        private const int HighFloorMb = 5120;    // at or above this -> High

        [Test]
        public void MemoryBelowTheLowCeilingIsLow()
        {
            Assert.AreEqual(QualityTierManager.Tier.Low,
                QualityTierManager.ClassifyMemory(LowCeilingMb - 1));
            Assert.AreEqual(QualityTierManager.Tier.Low,
                QualityTierManager.ClassifyMemory(2048));
        }

        [Test]
        public void TheLowCeilingItselfIsMedium()
        {
            // The off-by-one that decides whether a 3GB device is throttled to
            // 30fps. `<` rather than `<=` is the whole difference, and it is
            // not visible by reading the constant's name.
            Assert.AreEqual(QualityTierManager.Tier.Medium,
                QualityTierManager.ClassifyMemory(LowCeilingMb));
        }

        [Test]
        public void MemoryBetweenTheThresholdsIsMedium()
        {
            Assert.AreEqual(QualityTierManager.Tier.Medium,
                QualityTierManager.ClassifyMemory(4096));
            Assert.AreEqual(QualityTierManager.Tier.Medium,
                QualityTierManager.ClassifyMemory(HighFloorMb - 1));
        }

        [Test]
        public void TheHighFloorItselfIsHigh()
        {
            Assert.AreEqual(QualityTierManager.Tier.High,
                QualityTierManager.ClassifyMemory(HighFloorMb));
        }

        [Test]
        public void MemoryAboveTheHighFloorIsHigh()
        {
            Assert.AreEqual(QualityTierManager.Tier.High,
                QualityTierManager.ClassifyMemory(8192));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void AnUnknownMemoryFigureFallsBackToMedium(int memoryMb)
        {
            // SystemInfo returns 0 when it cannot answer. Guessing Low would
            // throttle a capable device on no evidence; guessing High would
            // hand a 2GB device a budget it cannot meet. Medium is the only
            // answer that is wrong by at most one step either way.
            Assert.AreEqual(QualityTierManager.Tier.Medium,
                QualityTierManager.ClassifyMemory(memoryMb));
        }

        [Test]
        public void TheAvailableDevicesLandWhereTheProcedureAssumes()
        {
            // docs/PERF_PROFILING_PROCEDURE.md wants a capture on the lowest
            // supported device at the low tier. None of the hardware on hand
            // reaches Low, so that capture needs the tier forced rather than
            // detected -- which is why ApplySettings is public and callable at
            // any time. This test is what makes that fact fail loudly if a
            // threshold moves and quietly invalidates the procedure.
            Assert.AreEqual(QualityTierManager.Tier.Medium,
                QualityTierManager.ClassifyMemory(3072), "iPhone X, 3GB");
            Assert.AreEqual(QualityTierManager.Tier.High,
                QualityTierManager.ClassifyMemory(6144), "iPhone 14 Pro Max, 6GB");
            Assert.AreEqual(QualityTierManager.Tier.High,
                QualityTierManager.ClassifyMemory(8192), "iPhone 17, 8GB");
        }

        [TestCase(QualityTierManager.Tier.Low, 12)]
        [TestCase(QualityTierManager.Tier.Medium, 24)]
        [TestCase(QualityTierManager.Tier.High, 48)]
        public void EachTierHasItsEnemyBudget(QualityTierManager.Tier tier, int expected)
        {
            Assert.AreEqual(expected, QualityTierManager.BudgetFor(tier));
        }

        [Test]
        public void TheBudgetRisesWithTheTier()
        {
            // The property, not the numbers: a tuning pass that left Low with
            // a larger budget than High would pass the cases above only by
            // being edited to match, and would still be nonsense.
            Assert.Less(QualityTierManager.BudgetFor(QualityTierManager.Tier.Low),
                        QualityTierManager.BudgetFor(QualityTierManager.Tier.Medium));
            Assert.Less(QualityTierManager.BudgetFor(QualityTierManager.Tier.Medium),
                        QualityTierManager.BudgetFor(QualityTierManager.Tier.High));
        }

        [Test]
        public void OnlyTheLowTierDropsToThirtyFps()
        {
            Assert.AreEqual(30,
                QualityTierManager.TargetFrameRateFor(QualityTierManager.Tier.Low));
            Assert.AreEqual(60,
                QualityTierManager.TargetFrameRateFor(QualityTierManager.Tier.Medium));
            Assert.AreEqual(60,
                QualityTierManager.TargetFrameRateFor(QualityTierManager.Tier.High));
        }

        [Test]
        public void DetectTierAgreesWithTheClassifierOnThisMachine()
        {
            // The seam is only worth having if the shipped path still goes
            // through it. Without this, DetectTier could stop calling
            // ClassifyMemory and every test above would keep passing while
            // the device ran on unverified code.
            Assert.AreEqual(
                QualityTierManager.ClassifyMemory(UnityEngine.SystemInfo.systemMemorySize),
                QualityTierManager.DetectTier());
        }

        [Test]
        public void CurrentAndEnemyBudgetNeverDisagree()
        {
            // Two statics that must move together. They are set from one place
            // -- ApplySettings -- and their initial values have to agree too,
            // because anything reading Current from its own Awake gets those:
            // ordering between MonoBehaviours is not guaranteed. A budget that
            // belongs to a different tier than Current reports is a spawner
            // running the wrong cap with nothing anywhere saying so.
            Assert.AreEqual(QualityTierManager.BudgetFor(QualityTierManager.Current),
                            QualityTierManager.EnemyBudget);
        }
    }
}
