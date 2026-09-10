using NUnit.Framework;
using UnityEngine;
using JetFighter.Build;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The measurement half of the profiling cell.
    ///
    /// The criterion is a device capture, which no test can produce. What
    /// these hold is that the capture measures the right thing -- worst frame
    /// and 1% low rather than the average -- and that the probe itself does
    /// not distort the number it reports.
    /// </summary>
    public class FrameBudgetTests
    {
        private GameObject root;
        private FrameBudgetProbe probe;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Probe");
            probe = root.AddComponent<FrameBudgetProbe>();
            probe.ResetStatistics();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        private void FeedSteady(int frames, float ms)
        {
            for (int i = 0; i < frames; i++)
            {
                probe.Sample(ms / 1000f);
            }
        }

        [Test]
        public void ASteadyRunReportsItsFrameTime()
        {
            FeedSteady(600, 16.6f);
            Assert.AreEqual(16.6f, probe.MedianMs(), 0.1f);
            Assert.AreEqual(16.6f, probe.OnePercentLowMs(), 0.5f);
        }

        [Test]
        public void OneHitchRuinsTheOnePercentLowButNotTheMedian()
        {
            // The whole reason the average is the wrong statistic: this run
            // averages fine and feels broken.
            FeedSteady(599, 16.6f);
            probe.Sample(0.200f);

            Assert.AreEqual(16.6f, probe.MedianMs(), 0.1f, "the median should be untouched");
            Assert.Greater(probe.OnePercentLowMs(), 30f, "the 1% low did not notice a 200ms stall");
            Assert.AreEqual(200f, probe.WorstFrameMs, 1f);
        }

        [Test]
        public void HitchesAreCountedAtTheThreshold()
        {
            probe.HitchThresholdMs = 33f;
            FeedSteady(100, 16.6f);
            probe.Sample(0.050f);
            probe.Sample(0.040f);

            Assert.AreEqual(2, probe.HitchCount);
        }

        [Test]
        public void AFrameExactlyAtTheThresholdIsNotAHitch()
        {
            probe.HitchThresholdMs = 33f;
            probe.Sample(0.033f);
            Assert.AreEqual(0, probe.HitchCount);
        }

        [Test]
        public void TheWorstFrameIsRemembered()
        {
            FeedSteady(10, 16.6f);
            probe.Sample(0.120f);
            FeedSteady(10, 16.6f);
            Assert.AreEqual(120f, probe.WorstFrameMs, 1f);
        }

        [Test]
        public void TheWindowRollsWithoutGrowing()
        {
            // A probe that allocated per frame would be measuring itself.
            FeedSteady(10000, 16.6f);
            Assert.AreEqual(10000, probe.SampleCount);
            Assert.AreEqual(16.6f, probe.MedianMs(), 0.1f);
        }

        [Test]
        public void OldFramesLeaveTheWindow()
        {
            // A stall ten minutes ago should not still be dragging the 1% low
            // down while someone tunes.
            probe.Sample(0.500f);
            FeedSteady(2000, 16.6f);
            Assert.AreEqual(16.6f, probe.OnePercentLowMs(), 1f);
        }

        [Test]
        public void ResettingClearsEverything()
        {
            FeedSteady(100, 16.6f);
            probe.Sample(0.300f);
            probe.ResetStatistics();

            Assert.AreEqual(0, probe.SampleCount);
            Assert.AreEqual(0f, probe.WorstFrameMs);
            Assert.AreEqual(0, probe.HitchCount);
            Assert.AreEqual(0f, probe.OnePercentLowMs());
        }

        [Test]
        public void NoSamplesIsNotADivideByZero()
        {
            Assert.AreEqual(0f, probe.OnePercentLowMs());
            Assert.AreEqual(0f, probe.MedianMs());
        }

        [Test]
        public void NonPositiveDeltasAreIgnored()
        {
            probe.Sample(0f);
            probe.Sample(-1f);
            Assert.AreEqual(0, probe.SampleCount);
        }

        [Test]
        public void ThePercentileNeedsAtLeastOneFrame()
        {
            probe.Sample(0.016f);
            Assert.AreEqual(16f, probe.OnePercentLowMs(), 0.5f);
        }
    }
}
