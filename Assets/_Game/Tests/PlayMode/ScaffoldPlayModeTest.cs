using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JetFighter.Build;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The PlayMode counterpart to ScaffoldSmokeTest: proves a MonoBehaviour
    /// can be instantiated in a running player loop and that frames advance.
    /// Cycle 1's physics work needs exactly this seam -- a plane constraint
    /// cannot be verified from EditMode, where FixedUpdate never runs.
    /// </summary>
    public class ScaffoldPlayModeTest
    {
        [UnityTest]
        public IEnumerator QualityTierManager_AppliesATierOnAwake()
        {
            var go = new GameObject("QualityTierManagerHost");
            go.AddComponent<QualityTierManager>();
            yield return null;

            Assert.Greater(QualityTierManager.EnemyBudget, 0,
                "Awake must have resolved a tier and its budget");
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator FixedUpdate_Advances()
        {
            int start = Time.frameCount;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.Greater(Time.frameCount, start, "the player loop is not running");
        }
    }
}
