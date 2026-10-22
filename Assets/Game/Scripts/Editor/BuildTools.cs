using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NeonVelocity.EditorTools
{
    /// <summary>
    /// One-click project configuration and reproducible desktop/mobile build entry points.
    /// Every method is safe to call from the Unity menu or from a batch-mode command line:
    ///
    ///   Unity -batchmode -projectPath . -executeMethod NeonVelocity.EditorTools.BuildTools.BuildMacOS
    ///   Unity -batchmode -projectPath . -executeMethod NeonVelocity.EditorTools.BuildTools.BuildWebGL
    /// </summary>
    public static class BuildTools
    {
        public const string MainScene = "Assets/Game/Scenes/Main.unity";
        public const string Company = "RK VANTAGE GAMES";
        public const string Product = "NEON VELOCITY";
        public const string BundleId = "games.rkvantage.neonvelocity";
        public const string Version = "1.0.0";
        private const string BuildsRoot = "Builds";

        [MenuItem("NEON VELOCITY/Configure Project and Build Settings", priority = 0)]
        public static void ConfigureProject()
        {
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[BUILD] Configured {Company} / {Product} v{Version} ({BundleId}).");
        }

        [MenuItem("NEON VELOCITY/Build macOS", priority = 20)]
        public static void BuildMacOS()
        {
            Run(BuildTarget.StandaloneOSX, Path.Combine(BuildsRoot, "macOS", "NeonVelocity.app"));
        }

        [MenuItem("NEON VELOCITY/Build Windows 64-bit", priority = 21)]
        public static void BuildWindows()
        {
            Run(BuildTarget.StandaloneWindows64, Path.Combine(BuildsRoot, "Windows", "NeonVelocity.exe"));
        }

        [MenuItem("NEON VELOCITY/Build Linux 64-bit", priority = 22)]
        public static void BuildLinux()
        {
            Run(BuildTarget.StandaloneLinux64, Path.Combine(BuildsRoot, "Linux", "NeonVelocity.x86_64"));
        }

        [MenuItem("NEON VELOCITY/Build WebGL", priority = 23)]
        public static void BuildWebGL()
        {
            Run(BuildTarget.WebGL, Path.Combine(BuildsRoot, "WebGL"));
        }

        [MenuItem("NEON VELOCITY/Build Android APK", priority = 24)]
        public static void BuildAndroid()
        {
            ConfigureProject();
            EditorUserBuildSettings.buildAppBundle = false;
            Run(BuildTarget.Android, Path.Combine(BuildsRoot, "Android", "NeonVelocity.apk"));
        }

        [MenuItem("NEON VELOCITY/Build Android App Bundle (AAB)", priority = 25)]
        public static void BuildAndroidBundle()
        {
            ConfigureProject();
            EditorUserBuildSettings.buildAppBundle = true;
            Run(BuildTarget.Android, Path.Combine(BuildsRoot, "Android", "NeonVelocity.aab"));
        }

        [MenuItem("NEON VELOCITY/Build iOS Xcode Project", priority = 26)]
        public static void BuildIOS()
        {
            Run(BuildTarget.iOS, Path.Combine(BuildsRoot, "iOS"));
        }

        [MenuItem("NEON VELOCITY/Build Active Target", priority = 40)]
        public static void BuildActiveTarget()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            switch (target)
            {
                case BuildTarget.StandaloneOSX:
                    BuildMacOS();
                    break;
                case BuildTarget.StandaloneWindows64:
                    BuildWindows();
                    break;
                case BuildTarget.StandaloneLinux64:
                    BuildLinux();
                    break;
                case BuildTarget.WebGL:
                    BuildWebGL();
                    break;
                case BuildTarget.Android:
                    BuildAndroid();
                    break;
                case BuildTarget.iOS:
                    BuildIOS();
                    break;
                default:
                    Fail($"No output path configured for {target}.");
                    break;
            }
        }

        private static void Run(BuildTarget target, string outputPath)
        {
            ConfigureProject();
            if (!EditorBuildSettings.scenes.Length.Equals(1))
                Debug.LogWarning("[BUILD] Unexpected scene list; Main should be the only enabled scene.");
            if (!File.Exists(MainScene))
            {
                Fail($"Main scene missing at {MainScene}.");
                return;
            }

            // Install the player module before builds are attempted so batch CI fails clearly.
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            {
                Fail($"{target} support is not installed for this Unity editor. " +
                     "Install the module in Unity Hub > Installs > Add modules.");
                return;
            }

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None
            };
            Debug.Log($"[BUILD] {target} -> {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"{target} build {summary.result}: {summary.totalErrors} error(s).");
                return;
            }
            Debug.Log($"[BUILD] Succeeded: {outputPath} ({summary.totalSize / (1024f * 1024f):0.0} MiB, " +
                      $"{summary.totalTime.TotalSeconds:0.0}s).");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[BUILD] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
