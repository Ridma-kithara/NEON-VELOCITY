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
            EnsureRuntimeMaterialTemplates();
            EnsureAlwaysIncludedShaders();
            AssetDatabase.SaveAssets();
            Debug.Log($"[BUILD] Configured {Company} / {Product} v{Version} ({BundleId}).");
        }

        /// <summary>
        /// URP initialises optional post-processing passes even when no volume uses them.
        /// Those hidden shaders are normally stripped from player builds, which spams
        /// "stripped from the build" errors at runtime. Keep the passes functional.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders()
        {
            string[] required =
            {
                "Hidden/Universal Render Pipeline/Bloom",
                "Hidden/Universal Render Pipeline/GaussianDepthOfField",
                "Hidden/Universal Render Pipeline/BokehDepthOfField",
                "Hidden/Universal Render Pipeline/CameraMotionBlur",
                "Hidden/Universal Render Pipeline/PaniniProjection"
            };

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0)
            {
                Fail("Could not open ProjectSettings/GraphicsSettings.asset.");
                return;
            }
            var serialized = new SerializedObject(assets[0]);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null)
            {
                Fail("GraphicsSettings has no m_AlwaysIncludedShaders property.");
                return;
            }

            for (int s = 0; s < required.Length; s++)
            {
                Shader shader = Shader.Find(required[s]);
                if (shader == null)
                {
                    Fail($"Required post-processing shader '{required[s]}' was not found.");
                    continue;
                }

                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        present = true;
                        break;
                    }
                }
                if (present) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                Debug.Log($"[BUILD] Always include shader {required[s]}.");
            }
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// URP shaders are resolved by name at runtime, which the player build cannot see.
        /// These Resources materials pin the shaders and the keyword/blend variants the game
        /// uses so the player does not strip them. MaterialLibrary instantiates these templates.
        /// </summary>
        private static void EnsureRuntimeMaterialTemplates()
        {
            const string resources = "Assets/Game/Resources";
            const string folder = resources + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(resources, "Materials");
            Texture2D flatNormal = EnsureFlatNormalTexture();

            EnsureTemplate(folder + "/NV_Lit_Opaque.mat", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.2f);
                m.SetFloat("_Smoothness", 0.6f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.01f, 0.01f, 0.01f, 1f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });
            EnsureTemplate(folder + "/NV_Lit_Normal.mat", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.1f);
                m.SetFloat("_Smoothness", 0.65f);
                m.EnableKeyword("_NORMALMAP");
                if (flatNormal != null)
                {
                    m.SetTexture("_BumpMap", flatNormal);
                    m.SetTextureScale("_BumpMap", Vector2.one);
                }
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.01f, 0.01f, 0.01f, 1f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            });
            EnsureTemplate(folder + "/NV_Lit_Transparent.mat", "Universal Render Pipeline/Lit", m =>
            {
                MakeTransparent(m, UnityEngine.Rendering.BlendMode.SrcAlpha,
                    UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, false);
            });
            EnsureTemplate(folder + "/NV_Unlit_Opaque.mat", "Universal Render Pipeline/Unlit", m =>
            {
                m.SetColor("_BaseColor", Color.white);
            });
            EnsureTemplate(folder + "/NV_Unlit_Transparent.mat", "Universal Render Pipeline/Unlit", m =>
            {
                MakeTransparent(m, UnityEngine.Rendering.BlendMode.SrcAlpha,
                    UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, false);
            });
            EnsureTemplate(folder + "/NV_Unlit_Additive.mat", "Universal Render Pipeline/Unlit", m =>
            {
                MakeTransparent(m, UnityEngine.Rendering.BlendMode.SrcAlpha,
                    UnityEngine.Rendering.BlendMode.One, true);
            });
            EnsureTemplate(folder + "/NV_Particles_Additive.mat", "Universal Render Pipeline/Particles/Unlit", m =>
            {
                MakeTransparent(m, UnityEngine.Rendering.BlendMode.SrcAlpha,
                    UnityEngine.Rendering.BlendMode.One, true);
            });
        }

        /// <summary>
        /// Pins a flat normal texture into the Resources normal-map template. The runtime then
        /// swaps in biome-specific normal maps without relying on a shader variant that would
        /// otherwise be stripped from the player build.
        /// </summary>
        private static Texture2D EnsureFlatNormalTexture()
        {
            const string folder = "Assets/Game/Resources/Textures";
            const string path = folder + "/NV_FlatNormal.asset";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Game/Resources", "Textures");
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true)
            {
                name = "NV_FlatNormal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(128, 128, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, path);
            Debug.Log($"[BUILD] Created flat normal map {path}.");
            return texture;
        }

        private static void MakeTransparent(Material material, UnityEngine.Rendering.BlendMode source,
            UnityEngine.Rendering.BlendMode destination, bool disableCull)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", disableCull ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)source);
            material.SetFloat("_DstBlend", (float)destination);
            material.SetFloat("_ZWrite", 0f);
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (disableCull) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void EnsureTemplate(string path, string shaderName, Action<Material> configure)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Fail($"Required shader '{shaderName}' was not found while creating {path}.");
                    return;
                }
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                configure(material);
                AssetDatabase.CreateAsset(material, path);
                Debug.Log($"[BUILD] Created shader template {path} ({shaderName}).");
                return;
            }

            // Keep existing templates in sync after render-pipeline upgrades or keyword fixes.
            configure(material);
            EditorUtility.SetDirty(material);
        }

        [MenuItem("NEON VELOCITY/Build macOS", priority = 20)]
        public static void BuildMacOS()
        {
#if UNITY_EDITOR_OSX
            // Produce one app that runs on both Intel and Apple silicon Macs.
            UnityEditor.OSXStandalone.UserBuildSettings.architecture =
                UnityEditor.Build.OSArchitecture.x64ARM64;
#endif
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
