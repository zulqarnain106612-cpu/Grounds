using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Player;
using JetFighter.Build;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// Architecture fitness functions for the C# side -- the same technique
    /// tests/test_architecture.py applies to the gateway, using reflection
    /// over the loaded assemblies instead of a Python import graph.
    ///
    /// These catch what no behavioural test can: an assembly boundary quietly
    /// eroding. The classic one in Unity is a runtime type acquiring a
    /// `UnityEditor` dependency -- the editor compiles and every test passes,
    /// and the iOS player build fails, which is the slowest and most expensive
    /// place to find out.
    ///
    /// The other half is wiring: `[RequireComponent]`, `[CreateAssetMenu]` and
    /// the serialized fields a prefab depends on are contracts the compiler
    /// does not check. A renamed field breaks every prefab referencing it and
    /// produces no error anywhere.
    /// </summary>
    public class AssemblyWiringTests
    {
        private static Assembly RuntimeAssembly => typeof(JetController).Assembly;

        // --- assembly boundaries -----------------------------------------

        [Test]
        public void TheRuntimeAssemblyIsNamedWhatTheAsmdefSays()
        {
            Assert.AreEqual("JetFighter.Runtime", RuntimeAssembly.GetName().Name,
                "the runtime types are not in the assembly the test asmdefs reference");
        }

        /// <summary>
        /// The one that stops a green editor run from shipping a broken
        /// player build. UnityEditor does not exist in a build, so a runtime
        /// assembly referencing it fails at IL2CPP time, not here.
        /// </summary>
        [Test]
        public void TheRuntimeAssemblyDoesNotDependOnTheEditor()
        {
            var editorRefs = RuntimeAssembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(name => name.StartsWith("UnityEditor") || name.StartsWith("nunit.framework"))
                .ToArray();

            Assert.IsEmpty(editorRefs,
                $"JetFighter.Runtime references editor-or-test-only assemblies: {string.Join(", ", editorRefs)}");
        }

        [Test]
        public void NoRuntimeTypeLivesInAnEditorOrTestNamespace()
        {
            var misplaced = RuntimeAssembly.GetTypes()
                .Where(t => t.Namespace != null
                            && (t.Namespace.Contains(".Editor") || t.Namespace.Contains(".Tests")))
                .Select(t => t.FullName)
                .ToArray();

            Assert.IsEmpty(misplaced,
                $"editor/test types compiled into the runtime assembly: {string.Join(", ", misplaced)}");
        }

        [Test]
        public void EveryRuntimeTypeSitsUnderTheProjectRootNamespace()
        {
            var strays = RuntimeAssembly.GetTypes()
                .Where(t => t.IsPublic && (t.Namespace == null || !t.Namespace.StartsWith("JetFighter")))
                .Select(t => t.FullName)
                .ToArray();

            Assert.IsEmpty(strays,
                $"public runtime types outside the JetFighter namespace: {string.Join(", ", strays)}");
        }

        // --- component wiring ---------------------------------------------

        /// <summary>
        /// `[RequireComponent]` is the only thing guaranteeing `GetComponent`
        /// in `Awake` returns something. Losing the attribute leaves a
        /// NullReferenceException that only appears on a GameObject somebody
        /// forgot to set up by hand.
        /// </summary>
        [Test]
        public void JetControllerStillDeclaresTheRigidbodyItAssumes()
        {
            var required = typeof(JetController)
                .GetCustomAttributes<RequireComponent>()
                .SelectMany(a => new[] { a.m_Type0, a.m_Type1, a.m_Type2 })
                .Where(t => t != null)
                .ToArray();

            CollectionAssert.Contains(required, typeof(Rigidbody),
                "JetController calls GetComponent<Rigidbody>() in Awake with nothing guaranteeing it exists");
        }

        /// <summary>
        /// Adding the component must satisfy its own requirement. This is the
        /// wiring test proper: the attribute and the runtime behaviour agree.
        /// </summary>
        [Test]
        public void AddingJetControllerAlsoProducesItsRigidbody()
        {
            var probe = new GameObject("WiringProbe");
            try
            {
                probe.AddComponent<JetController>();
                Assert.IsNotNull(probe.GetComponent<Rigidbody>(),
                    "RequireComponent did not bring the Rigidbody along");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// Serialized field names are a contract with every prefab and scene
        /// that references them. Renaming one silently drops the reference --
        /// there is no compile error and no runtime error, just a null.
        /// </summary>
        [TestCase("config")]
        [TestCase("visual")]
        public void JetControllerKeepsItsSerializedFieldNames(string fieldName)
        {
            var field = typeof(JetController).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(field,
                $"'{fieldName}' is gone; every prefab serializing it now has a null reference. " +
                $"Rename with [FormerlySerializedAs] if this was deliberate.");
        }

        [Test]
        public void TheFlightConfigIsStillCreatableAsAnAsset()
        {
            Assert.IsNotNull(typeof(JetFlightConfig).GetCustomAttribute<CreateAssetMenuAttribute>(),
                "JetFlightConfig lost [CreateAssetMenu]; a tuning pass can no longer make one");
        }

        // --- the seam the tests themselves depend on ----------------------

        /// <summary>
        /// The flight model's arithmetic is deliberately static and free of
        /// Rigidbody and Time so EditMode tests can reach it without a scene.
        /// If these stop being public statics, every EditMode flight test
        /// fails to compile -- which is a worse signal than one named failure
        /// saying exactly which seam moved.
        /// </summary>
        [TestCase("ComputeAcceleration")]
        [TestCase("ComputeTargetBankAngle")]
        [TestCase("StepTowardBank")]
        public void ThePureFlightFunctionsStayPublicAndStatic(string methodName)
        {
            var method = typeof(JetController).GetMethod(
                methodName, BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(method, $"{methodName} is no longer a public static");
        }

        [Test]
        public void TheQualityTierSeamStaysCallableAtAnyTime()
        {
            // Phase 6's settings screen calls ApplySettings outside Awake, so
            // it has to remain reachable without a MonoBehaviour instance.
            var apply = typeof(QualityTierManager).GetMethod(
                "ApplySettings", BindingFlags.Public | BindingFlags.Static);
            var detect = typeof(QualityTierManager).GetMethod(
                "DetectTier", BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(apply, "QualityTierManager.ApplySettings is not a public static");
            Assert.IsNotNull(detect, "QualityTierManager.DetectTier is not a public static");
            Assert.AreEqual(typeof(QualityTierManager.Tier), detect.ReturnType);
        }

        /// <summary>
        /// Every MonoBehaviour must actually be attachable. A type that has
        /// drifted abstract or generic still compiles and still looks like a
        /// component in code review.
        /// </summary>
        [Test]
        public void EveryRuntimeMonoBehaviourCanBeAttachedToAGameObject()
        {
            var broken = RuntimeAssembly.GetTypes()
                .Where(t => typeof(MonoBehaviour).IsAssignableFrom(t))
                .Where(t => t.IsAbstract || t.IsGenericTypeDefinition)
                .Select(t => t.FullName)
                .ToArray();

            Assert.IsEmpty(broken,
                $"MonoBehaviours that cannot be added to a GameObject: {string.Join(", ", broken)}");
        }
    }
}
