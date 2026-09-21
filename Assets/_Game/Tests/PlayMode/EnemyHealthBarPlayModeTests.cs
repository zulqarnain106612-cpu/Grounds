using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using JetFighter.Enemy;
using JetFighter.UI;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The bar's lifecycle, which only exists in PlayMode: OnEnable and
    /// OnDisable do not run in the editor, and this component's whole
    /// subscription story lives there.
    ///
    /// Also the negative half of the criterion -- that idle frames cost
    /// nothing. That is the assertion a profiler screenshot is usually
    /// substituted for, and a screenshot does not fail a build.
    /// </summary>
    public class EnemyHealthBarPlayModeTests
    {
        private GameObject enemyObject;
        private GameObject barObject;
        private EnemyHealth health;
        private EnemyHealthBarUI bar;
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
            bar = barObject.AddComponent<EnemyHealthBarUI>();
            bar.FillImage = barObject.AddComponent<Image>();
            bar.Group = barObject.AddComponent<CanvasGroup>();
            bar.Health = health;
            bar.FollowTarget = enemyObject.transform;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(barObject);
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(def);
        }

        [UnityTest]
        public IEnumerator IdleFramesDoNotRefreshTheBar()
        {
            // The criterion's negative half, stated as an assertion rather
            // than as a profiler screenshot.
            yield return null;
            int baseline = bar.RefreshCount;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
            }
            Assert.AreEqual(baseline, bar.RefreshCount, "the bar is polling health every frame");
        }

        [UnityTest]
        public IEnumerator DamageRefreshesTheBarExactlyOnce()
        {
            yield return null;
            int baseline = bar.RefreshCount;
            health.ApplyDamage(10f);
            yield return null;
            Assert.AreEqual(baseline + 1, bar.RefreshCount);
            Assert.AreEqual(0.9f, bar.FillImage.fillAmount, 1e-4f);
        }

        [UnityTest]
        public IEnumerator ADisabledBarStopsListening()
        {
            yield return null;
            barObject.SetActive(false);
            int baseline = bar.RefreshCount;
            health.ApplyDamage(10f);
            yield return null;
            Assert.AreEqual(baseline, bar.RefreshCount);
        }

        [UnityTest]
        public IEnumerator ReEnablingDoesNotDoubleTheSubscription()
        {
            // Pooled enemies enable and disable constantly. A doubled
            // listener is invisible until profiling.
            yield return null;
            barObject.SetActive(false);
            yield return null;
            barObject.SetActive(true);
            yield return null;

            int baseline = bar.RefreshCount;
            health.ApplyDamage(10f);
            yield return null;
            Assert.AreEqual(baseline + 1, bar.RefreshCount);
        }

        [UnityTest]
        public IEnumerator TheBarFollowsTheEnemy()
        {
            yield return null;
            enemyObject.transform.position = new Vector3(5f, 2f, 0f);
            yield return null;
            Assert.AreEqual(5f, barObject.transform.position.x, 1e-3f);
            Assert.Greater(barObject.transform.position.y, 2f, "the bar sits on the enemy, not above it");
        }

        [UnityTest]
        public IEnumerator TheBarHidesOnDeath()
        {
            yield return null;
            health.ApplyDamage(1000f);
            yield return null;
            Assert.AreEqual(0f, bar.Group.alpha);
        }

        [UnityTest]
        public IEnumerator AnUndamagedEnemyShowsNoBar()
        {
            yield return null;
            Assert.AreEqual(0f, bar.Group.alpha, "a full bar over every enemy is visual noise");

            health.ApplyDamage(1f);
            yield return null;
            Assert.AreEqual(1f, bar.Group.alpha);
        }
    }
}
