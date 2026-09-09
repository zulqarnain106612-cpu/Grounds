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
            // Is.AnyOf does not exist in the NUnit version Unity ships, which
            // is a compile error rather than a failing test -- the suite never
            // runs at all. Enum.IsDefined asserts the same thing and keeps
            // working if a tier is added later.
            Assert.That(System.Enum.IsDefined(typeof(QualityTierManager.Tier),
                                              QualityTierManager.DetectTier()),
                        Is.True, "DetectTier must return a declared Tier value");
        }
    }
}
