using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using JetFighter.Build;
using JetFighter.Editor;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// What the generated bootstrap scene contains.
    ///
    /// The scene is composed from code rather than committed as YAML, so it
    /// can be asserted here without booting a player -- which is the point of
    /// composing it from code. Compose() is exercised against a throwaway
    /// scene; nothing here writes a .unity file, so a developer running the
    /// suite does not end up with a generated asset in their working tree.
    /// </summary>
    public class BootstrapSceneTests
    {
        private Scene _scene;

        [SetUp]
        public void CreateScene()
        {
            _scene = SceneManager.CreateScene("BootstrapSceneTests");
            BootstrapSceneBuilder.Compose(_scene);
        }

        [TearDown]
        public void DestroyScene()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                Object.DestroyImmediate(root);
            }
        }

        private T Find<T>() where T : Component
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
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
            // assertion here passing on a scene with no objects in it.
            Assert.IsNotEmpty(_scene.GetRootGameObjects());
        }

        [Test]
        public void ThereIsExactlyOneCamera()
        {
            int cameras = 0;
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                cameras += root.GetComponentsInChildren<Camera>(true).Length;
            }
            // Two cameras render twice and halve the frame rate, which reads
            // as a performance problem rather than a scene problem.
            Assert.AreEqual(1, cameras);
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
            int listeners = 0;
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                listeners += root.GetComponentsInChildren<AudioListener>(true).Length;
            }
            // Unity logs a warning and picks one arbitrarily with more than
            // one, which makes positional audio non-deterministic.
            Assert.AreEqual(1, listeners);
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
            foreach (GameObject root in _scene.GetRootGameObjects())
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
            Scene second = SceneManager.CreateScene("BootstrapSceneTests2");
            try
            {
                BootstrapSceneBuilder.Compose(second);
                string[] first = NamesOf(_scene);
                string[] repeat = NamesOf(second);
                CollectionAssert.AreEqual(first, repeat);
            }
            finally
            {
                foreach (GameObject root in second.GetRootGameObjects())
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static string[] NamesOf(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
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
