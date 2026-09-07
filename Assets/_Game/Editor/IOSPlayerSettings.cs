#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace JetFighter.Editor
{
    /// <summary>
    /// Applies iOS Player Settings from config/ios.build.json.
    ///
    /// The settings live in JSON rather than only in ProjectSettings.asset
    /// because that file is Unity-generated YAML that merges badly and cannot
    /// be asserted on without booting the editor. Keeping the values in JSON
    /// lets CI check them on every PR (tests/test_ios_scaffold.py) while Unity
    /// still owns the .asset it writes. See docs/DECISIONS.md ADR-012.
    /// </summary>
    public static class IOSPlayerSettings
    {
        private const string ConfigRelativePath = "config/ios.build.json";
        private const string MenuPath = "JetFighter/Apply iOS Player Settings";

        [System.Serializable]
        private class PlayerSettingsBlock
        {
            public string bundle_identifier;
            public string product_name;
            public string company_name;
            public string target_minimum_ios_version;
            public string scripting_backend;
            public string graphics_api;
            public string target_device;
            public bool strip_engine_code;
            public bool allow_unsafe_code;
        }

        [System.Serializable]
        private class BuildConfig
        {
            public PlayerSettingsBlock player_settings;
        }

        /// <summary>Path to the JSON config, resolved from the project root.</summary>
        public static string ConfigPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, ConfigRelativePath);
        }

        [MenuItem(MenuPath)]
        public static void Apply()
        {
            string path = ConfigPath();
            if (!File.Exists(path))
            {
                Debug.LogError($"[IOSPlayerSettings] missing {ConfigRelativePath}; refusing to guess player settings");
                return;
            }

            BuildConfig config = JsonUtility.FromJson<BuildConfig>(File.ReadAllText(path));
            PlayerSettingsBlock settings = config.player_settings;

            PlayerSettings.productName = settings.product_name;
            PlayerSettings.companyName = settings.company_name;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, settings.bundle_identifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
            PlayerSettings.iOS.targetOSVersionString = settings.target_minimum_ios_version;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.stripEngineCode = settings.strip_engine_code;
            PlayerSettings.allowUnsafeCode = settings.allow_unsafe_code;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });

            AssetDatabase.SaveAssets();
            Debug.Log($"[IOSPlayerSettings] applied {ConfigRelativePath}: {settings.bundle_identifier} min iOS {settings.target_minimum_ios_version}");
        }
    }
}
#endif
