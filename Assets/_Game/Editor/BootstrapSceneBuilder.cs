#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using JetFighter.Build;

namespace JetFighter.Editor
{
    /// <summary>
    /// Builds the bootstrap scene from code, at build time, into a directory
    /// nothing tracks.
    ///
    /// A .unity file is Unity-generated YAML with GUID references, exactly
    /// like ProjectSettings.asset -- it merges badly, cannot be asserted on
    /// without booting the editor, and cannot be hand-authored safely. ADR-012
    /// already settled that class of file for the player settings: keep the
    /// intent in code or JSON and let Unity own the artefact. Committing a
    /// hand-written scene would be the same mistake with a worse failure mode,
    /// because a broken GUID reference produces an empty GameObject rather
    /// than an error.
    ///
    /// So the scene is not committed. It is composed here, deterministically,
    /// from the same runtime types the tests already cover, and regenerated on
    /// every build. Two consequences, both wanted: the scene cannot drift away
    /// from the code, and reviewing what is in it means reading this file
    /// rather than diffing YAML.
    ///
    /// Scope: the always-on managers and a camera. That is what makes an
    /// export produce an app that launches, which is what
    /// phase6/appstore-cert-checklist needs to reach an archive. Gameplay
    /// content belongs to its own cells and is not invented here.
    /// </summary>
    public static class BootstrapSceneBuilder
    {
        /// <summary>Generated, gitignored, regenerated on every build.</summary>
        public const string ScenePath = "Assets/_Game/Generated/Bootstrap.unity";

        private const string MenuPath = "JetFighter/Rebuild Bootstrap Scene";

        /// <summary>
        /// Writes the scene and makes it the only entry in Build Settings.
        /// Returns its asset path.
        /// </summary>
        [MenuItem(MenuPath)]
        public static string Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

            // Single, so the new scene becomes the active one and everything
            // Compose() creates lands in it without a MoveGameObjectToScene
            // call -- which is a runtime API and is not reliable in the editor.
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Compose();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new IOException($"[BootstrapSceneBuilder] could not save {ScenePath}");
            }

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, enabled: true),
            };

            Debug.Log($"[BootstrapSceneBuilder] wrote {ScenePath} and made it scene 0");
            return ScenePath;
        }

        /// <summary>
        /// Everything the scene contains, created in the active scene and
        /// returned as its root objects.
        ///
        /// Separated from the file writing so the contents can be asserted in
        /// an EditMode test without leaving a .unity behind on a developer's
        /// machine -- and taking no Scene, because every other EditMode suite
        /// here builds with a bare `new GameObject(...)`. The scene APIs are
        /// runtime APIs; reaching for them is what made the first version of
        /// these tests fail in SetUp, all nine of them, before a single
        /// assertion ran.
        /// </summary>
        public static GameObject[] Compose()
        {
            // NewSceneSetup.EmptyScene is deliberate: the default setup ships a
            // camera and a directional light whose settings are Unity's
            // defaults rather than this project's, and a scene that is partly
            // authored elsewhere is the drift this file exists to avoid.
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Not black. A black first frame is indistinguishable from the
            // scene-less build IOSBuild refuses, and telling those apart on a
            // device is the whole point of refusing.
            camera.backgroundColor = new Color(0.05f, 0.08f, 0.15f, 1f);
            cameraObject.AddComponent<AudioListener>();

            // The tier has to be resolved before anything reads its budget,
            // and Awake order between MonoBehaviours is not guaranteed, so it
            // is the first object composed here rather than merely present.
            var managers = new GameObject("Managers");
            managers.AddComponent<QualityTierManager>();

            return new[] { cameraObject, managers };
        }
    }
}
#endif
