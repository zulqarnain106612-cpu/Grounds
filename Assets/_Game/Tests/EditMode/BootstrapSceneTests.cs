using NUnit.Framework;
using UnityEngine;
using JetFighter.Build;
using JetFighter.Editor;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// What the generated bootstrap scene contains.
    ///
    /// The scene is composed from code rather than committed as YAML, so it
    /// can be asserted here without booting a player -- which is the point of
    /// composing it from code. Nothing here writes a .unity file, so running
    /// the suite leaves no generated asset in a working tree.
    ///
    /// Compose() returns its root objects rather than filling a Scene, and
    /// these tests hold them directly. The first version created a scene with
    /// SceneManager.CreateScene and all nine tests failed in SetUp before a
    /// single assertion ran: the scene APIs are runtime APIs, and every other
    /// EditMode suite in this project builds with a bare `new GameObject`.
    /// </summary>
    public class BootstrapSceneTests
    {
        private GameObject[] _roots;

        [SetUp]
        public void ComposeRoots()
        {
            _roots = BootstrapSceneBuilder.Compose();
        }

        [TearDown]
        public void DestroyRoots()
        {
            DestroyAll(_roots);
        }

        private static void DestroyAll(GameObject[] roots)
        {
            if (roots == null)
            {
                return;
            }
            foreach (GameObject root in roots)
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static int CountOf<T>(GameObject[] roots) where T : Component
        {
            int found = 0;
            foreach (GameObject root in roots)
            {
                found += root.GetComponentsInChildren<T>(true).Length;
            }
            return found;
        }

        private T Find<T>() where T : Component
        {
            foreach (GameObject root in _roots)
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        [Test]
        public void TheSceneIsNotEmpty()
        {
            // A Compose() that silently did nothing would leave every other
            // assertion here passing on nothing at all.
            Assert.IsNotEmpty(_roots);
        }

        [Test]
        public void ThereIsExactlyOneCamera()
        {
            // Two cameras render twice and halve the frame rate, which reads
            // as a performance problem rather than a scene problem.
            Assert.AreEqual(1, CountOf<Camera>(_roots));
        }

        [Test]
        public void TheCameraIsTaggedMainCamera()
        {
            // Camera.main returns null without the tag, and the code that
            // calls it then throws a NullReferenceException at runtime only.
            Assert.AreEqual("MainCamera", Find<Camera>().gameObject.tag);
        }

        [Test]
        public void TheCameraClearsToSomethingThatIsNotBlack()
        {
            Camera camera = Find<Camera>();
            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
            // A black first frame is indistinguishable from the scene-less
            // build IOSBuild refuses to produce. Telling those two apart on a
            // device is the entire reason that refusal exists.
            Assert.Greater(camera.backgroundColor.r + camera.backgroundColor.g
                           + camera.backgroundColor.b, 0.05f);
            Assert.AreEqual(1f, camera.backgroundColor.a);
        }

        [Test]
        public void ThereIsExactlyOneAudioListener()
        {
            // Unity logs a warning and picks one arbitrarily with more than
            // one, which makes positional audio non-deterministic.
            Assert.AreEqual(1, CountOf<AudioListener>(_roots));
        }

        [Test]
        public void TheQualityTierManagerIsPresent()
        {
            // Nothing else resolves the device tier. Without it every device
            // runs the Medium defaults, including the 2GB ones.
            Assert.IsNotNull(Find<QualityTierManager>());
        }

        [Test]
        public void EverythingComposedIsActive()
        {
            // An inactive root runs no Awake, so a manager that is present but
            // disabled is indistinguishable from one that is missing -- except
            // that it looks correct in the hierarchy.
            foreach (GameObject root in _roots)
            {
                Assert.IsTrue(root.activeSelf, root.name);
            }
        }

        [Test]
        public void ComposeIsDeterministic()
        {
            // Regenerated on every build, so two runs must agree. A scene that
            // differed between runs would make a build reproducible only by
            // accident.
            GameObject[] second = BootstrapSceneBuilder.Compose();
            try
            {
                CollectionAssert.AreEqual(NamesOf(_roots), NamesOf(second));
                Assert.AreEqual(CountOf<Camera>(_roots), CountOf<Camera>(second));
                Assert.AreEqual(CountOf<QualityTierManager>(_roots),
                                CountOf<QualityTierManager>(second));
            }
            finally
            {
                DestroyAll(second);
            }
        }

        private static string[] NamesOf(GameObject[] roots)
        {
            var names = new string[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                names[i] = roots[i].name;
            }
            return names;
        }

        [Test]
        public void TheGeneratedScenePathIsUnderTheIgnoredDirectory()
        {
            // The path is what .gitignore covers. If it moved, the scene would
            // start being committed -- silently, because a new .unity looks
            // like an authored asset rather than build output.
            Assert.IsTrue(
                BootstrapSceneBuilder.ScenePath.StartsWith("Assets/_Game/Generated/"),
                BootstrapSceneBuilder.ScenePath);
            Assert.IsTrue(BootstrapSceneBuilder.ScenePath.EndsWith(".unity"));
        }
    }
}
