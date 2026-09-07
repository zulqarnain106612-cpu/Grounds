using NUnit.Framework;
using JetFighter.Build;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// Proves the Unity test runner executes and reports. Deleted once Cycle 1
    /// has real tests -- see docs/CYCLE0_BOOTSTRAP_SPEC.md section 2.
    ///
    /// It asserts something real rather than Assert.Pass(): a test that cannot
    /// fail proves the runner started, not that it reports failures, and the
    /// whole point of this branch is that a green job means something.
    /// </summary>
    public class ScaffoldSmokeTest
    {
        [Test]
        public void ApplySettings_SetsTheTierAndItsEnemyBudget()
        {
            QualityTierManager.ApplySettings(QualityTierManager.Tier.Low);
            Assert.AreEqual(QualityTierManager.Tier.Low, QualityTierManager.Current);
            int lowBudget = QualityTierManager.EnemyBudget;

            QualityTierManager.ApplySettings(QualityTierManager.Tier.High);
            Assert.AreEqual(QualityTierManager.Tier.High, QualityTierManager.Current);
            Assert.Greater(QualityTierManager.EnemyBudget, lowBudget,
                "a higher tier must not budget fewer enemies than a lower one");
        }

        [Test]
        public void DetectTier_ReturnsAKnownTier()
        {
            Assert.That(QualityTierManager.DetectTier(),
                Is.AnyOf(QualityTierManager.Tier.Low,
                         QualityTierManager.Tier.Medium,
                         QualityTierManager.Tier.High));
        }
    }
}
