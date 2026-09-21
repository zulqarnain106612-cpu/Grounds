#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace JetFighter.Editor
{
    /// <summary>
    /// Batchmode entry point that exports the Xcode project an archive is made
    /// from. `phase6/appstore-cert-checklist` closes on that archive, and
    /// nothing can archive what Unity has not exported.
    ///
    /// Two batchmode behaviours this exists to correct, both of which report
    /// success:
    ///
    ///   1. Unity exits 0 whatever the build reported. A failed build then
    ///      leaves the job green with no Xcode project in it, and the failure
    ///      surfaces at `xcodebuild` as a missing path -- which reads like a
    ///      workflow bug rather than a build one. EditorApplication.Exit below
    ///      carries the BuildReport's verdict out of the process.
    ///   2. A build with no scenes succeeds. It produces an app that launches
    ///      to a black screen, which only a human looking at a device can see.
    ///      That is precisely the acceptance this project refuses to rely on,
    ///      so zero scenes is a build failure here.
    ///
    /// Invoked as:
    ///     Unity -batchmode -quit -projectPath . \
    ///           -executeMethod JetFighter.Editor.IOSBuild.PerformBuild \
    ///           -buildPath build/iOS
    /// </summary>
    public static class IOSBuild
    {
        private const string BuildPathArgument = "-buildPath";
        private const string DefaultBuildPath = "build/iOS";

        /// <summary>Exit code for a refusal to start, distinct from a build failure.</summary>
        private const int RefusedExitCode = 2;
        private const int FailedExitCode = 1;

        /// <summary>The value after <c>-buildPath</c>, or the default.</summary>
        public static string ResolveBuildPath(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == BuildPathArgument && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            return DefaultBuildPath;
        }

        /// <summary>
        /// Scenes to build, in EditorBuildSettings order, enabled ones only.
        /// Disabled entries are excluded on purpose: the checkbox is how a
        /// scene is taken out of a build, and honouring it here keeps the
        /// editor's view and CI's view of the build the same.
        /// </summary>
        public static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        public static void PerformBuild()
        {
            // The player settings come from config/ios.build.json, never from
            // whatever ProjectSettings.asset happens to hold on the runner.
            // ADR-012: the JSON is the source of truth and the .asset is
            // Unity-generated output.
            IOSPlayerSettings.Apply();

            // The scene is generated, never committed: see
            // BootstrapSceneBuilder. Building it here rather than expecting it
            // keeps a fresh checkout -- which is every CI run -- buildable
            // without a manual editor step.
            BootstrapSceneBuilder.Build();

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError(
                    "[IOSBuild] EditorBuildSettings lists no enabled scene even " +
                    "after BootstrapSceneBuilder ran, so this build would produce " +
                    "an app that launches to a black screen. Refusing rather than " +
                    "exporting one: a silent black screen is only catchable by a " +
                    "human on a device, which is the acceptance this project does " +
                    "not rely on. The scene builder's own log line above says " +
                    "whether it wrote " + BootstrapSceneBuilder.ScenePath + ".");
                EditorApplication.Exit(RefusedExitCode);
                return;
            }

            string buildPath = ResolveBuildPath(Environment.GetCommandLineArgs());
            Directory.CreateDirectory(buildPath);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = buildPath,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                // Development is deliberately absent. A development player
                // carries the profiler and the script debugger, both of which
                // change the frame times `phase6/perf-profiling-pass` measures,
                // and neither can be uploaded to App Store Connect.
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log(
                $"[IOSBuild] {summary.result} -- {scenes.Length} scene(s), " +
                $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s), " +
                $"{summary.totalTime} into {buildPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                // Unity would exit 0 here. Every step after this one assumes an
                // Xcode project exists at buildPath, so the run has to stop.
                EditorApplication.Exit(FailedExitCode);
                return;
            }

            EditorApplication.Exit(0);
        }
    }
}
#endif
