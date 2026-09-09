using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using JetFighter.Enemy;
using JetFighter.UI;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The bar's criterion: it updates on the event, and never by polling.
    ///
    /// RefreshCount is what makes the negative half assertable -- "no
    /// per-frame polling" is otherwise something you look for in a profiler
    /// and forget to check again.
    /// </summary>
    public class EnemyHealthBarUITests
    {
        private const float Healthy = 0.6f;
        private const float Critical = 0.3f;

        private GameObject enemyObject;
        private GameObject barObject;
        private EnemyHealth health;
        private EnemyHealthBarUI bar;
        private Image fill;
        private EnemyDef def;

        [SetUp]
        public void SetUp()
        {
            def = ScriptableObject.CreateInstance<EnemyDef>();
            def.maxHealth = 100f;

            enemyObject = new GameObject("Enemy");
            health = enemyObject.AddComponent<EnemyHealth>();
            health.Def = def;

            barObject = new GameObject("HealthBar", typeof(RectTransform));
            fill = barObject.AddComponent<Image>();
            fill.type = Image.Type.Filled;
            bar = barObject.AddComponent<EnemyHealthBarUI>();

            bar.FillImage = fill;
            bar.Health = health;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(barObject);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void TheBarFillsToTheRemainingFraction()
        {
            health.ApplyDamage(25f);
            Assert.AreEqual(0.75f, fill.fillAmount, 1e-4f);
        }

        [Test]
        public void TheBarUpdatesOncePerDamageEventAndNotOtherwise()
        {
            int baseline = bar.RefreshCount;
            health.ApplyDamage(10f);
            Assert.AreEqual(baseline + 1, bar.RefreshCount);

            // Nothing happening must cost nothing. A per-frame poll would
            // show up here as a rising count with no damage dealt.
            health.ApplyDamage(0f);
            health.ApplyDamage(-5f);
            Assert.AreEqual(baseline + 1, bar.RefreshCount);
        }

        [Test]
        public void ReassigningTheEnemyDoesNotLeaveTwoSubscriptions()
        {
            // Pooled enemies enable and disable repeatedly. A doubled
            // listener is invisible until profiling.
            bar.Health = health;
            bar.Health = health;
            int baseline = bar.RefreshCount;
            health.ApplyDamage(10f);
            Assert.AreEqual(baseline + 1, bar.RefreshCount, "the bar refreshed twice for one hit");
        }

        [Test]
        public void FullHealthIsGreen()
        {
            Assert.AreEqual(Color.green, EnemyHealthBarUI.ColorFor(1f, Healthy, Critical));
        }

        [Test]
        public void TheHealthyThresholdItselfIsStillGreen()
        {
            Assert.AreEqual(Color.green, EnemyHealthBarUI.ColorFor(Healthy, Healthy, Critical));
        }

        [Test]
        public void CriticalHealthIsRed()
        {
            Assert.AreEqual(Color.red, EnemyHealthBarUI.ColorFor(0.05f, Healthy, Critical));
            Assert.AreEqual(Color.red, EnemyHealthBarUI.ColorFor(Critical, Healthy, Critical));
        }

        [Test]
        public void TheMiddleOfTheWarningBandIsYellowNotOlive()
        {
            // A single red-to-green lerp passes through a muddy olive, which
            // reads as a rendering fault rather than as a warning.
            Color mid = EnemyHealthBarUI.ColorFor((Healthy + Critical) * 0.5f, Healthy, Critical);
            Assert.AreEqual(Color.yellow.r, mid.r, 0.05f);
            Assert.AreEqual(Color.yellow.g, mid.g, 0.05f);
            Assert.Less(mid.b, 0.1f);
        }

        [Test]
        public void TheColourRampIsMonotonicInGreen()
        {
            float previous = -1f;
            for (float pct = 0f; pct <= 1.0001f; pct += 0.02f)
            {
                float g = EnemyHealthBarUI.ColorFor(pct, Healthy, Critical).g;
                Assert.GreaterOrEqual(g, previous - 1e-4f, $"the bar got greener as health fell, at {pct}");
                previous = g;
            }
        }

        [Test]
        public void OutOfRangeFractionsAreClampedRatherThanExtrapolated()
        {
            Assert.AreEqual(Color.green, EnemyHealthBarUI.ColorFor(5f, Healthy, Critical));
            Assert.AreEqual(Color.red, EnemyHealthBarUI.ColorFor(-5f, Healthy, Critical));
            bar.Refresh(9f);
            Assert.AreEqual(1f, fill.fillAmount);
        }

        [Test]
        public void ThresholdsDraggedPastEachOtherDoNotInvertTheRamp()
        {
            // The inspector allows it; the ramp must not flip.
            Assert.AreEqual(Color.green, EnemyHealthBarUI.ColorFor(1f, Critical, Healthy));
            Assert.AreEqual(Color.red, EnemyHealthBarUI.ColorFor(0f, Critical, Healthy));
        }
    }
}
