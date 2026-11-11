using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Editor
{
    public static class AndroidBuildPipeline
    {
        private const string DefaultScenePath = "Assets/Scenes/Main.unity";
        private const string SmokeApkRelativePath = "Builds/CrusherCore-smoke-universal.apk";
        private const string ReleaseApkRelativePath = "Builds/CrusherCore-v1.0.0-arm64.apk";
        private const string KeystoreMetaRelativePath = "keystore/keystore_meta.json";

        [Serializable]
        private sealed class KeystoreMeta
        {
            public string keystorePath;
            public string alias;
            public string storePass;
            public string keyPass;
        }

        [MenuItem("Crusher/Build/1. Build Smoke Universal APK (ARM64 + X86_64, Vulkan + GLES3)")]
        public static void BuildSmokeApk()
        {
            Debug.Log("[AndroidBuildPipeline] Configuring Smoke Universal APK profile (ARM64 | X86_64, Vulkan -> OpenGLES3)...");
            EnsureAndroidTarget();

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.crushercore.hypershredder");
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

            ApplyKeystoreSigning();
            RunBuild(SmokeApkRelativePath, BuildOptions.CompressWithLz4, "SMOKE_APK");
        }

        [MenuItem("Crusher/Build/2. Build Release ARM64 APK (Vulkan First, LZ4HC)")]
        public static void BuildReleaseApk()
        {
            Debug.Log("[AndroidBuildPipeline] Restoring Release ARM64 APK profile (ARM64, Vulkan -> OpenGLES3, IL2CPP, LZ4HC)...");
            EnsureAndroidTarget();

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.crushercore.hypershredder");
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;

            ApplyKeystoreSigning();
            RunBuild(ReleaseApkRelativePath, BuildOptions.CompressWithLz4HC, "RELEASE_APK");
        }

        private static void EnsureAndroidTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    throw new InvalidOperationException("Failed to switch active BuildTarget to Android.");
                }
            }
        }

        private static void ApplyKeystoreSigning()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string metaFull = Path.Combine(projectRoot, KeystoreMetaRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(metaFull))
            {
                throw new FileNotFoundException("Keystore metadata missing at: " + metaFull);
            }

            var meta = JsonUtility.FromJson<KeystoreMeta>(File.ReadAllText(metaFull));
            if (meta == null || string.IsNullOrEmpty(meta.keystorePath) || string.IsNullOrEmpty(meta.alias) || string.IsNullOrEmpty(meta.storePass))
            {
                throw new InvalidOperationException("Invalid keystore_meta.json structure.");
            }

            string ksFull = Path.GetFullPath(Path.Combine(projectRoot, meta.keystorePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(ksFull))
            {
                throw new FileNotFoundException("Release keystore file not found at: " + ksFull);
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ksFull;
            PlayerSettings.Android.keystorePass = meta.storePass;
            PlayerSettings.Android.keyaliasName = meta.alias;
            PlayerSettings.Android.keyaliasPass = string.IsNullOrEmpty(meta.keyPass) ? meta.storePass : meta.keyPass;

            Debug.Log($"[AndroidBuildPipeline] Signing configured via custom keystore (alias={meta.alias}).");
        }

        private static string[] ResolveScenesToBuild()
        {
            string[] enabled = EditorBuildSettings.scenes
                .Where(s => s != null && s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path))
                .Select(s => s.path)
                .ToArray();

            if (enabled.Length > 0)
            {
                return enabled;
            }

            if (File.Exists(DefaultScenePath))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(DefaultScenePath, true) };
                return new[] { DefaultScenePath };
            }

            string[] anyScenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes", "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (anyScenes.Length > 0)
            {
                EditorBuildSettings.scenes = anyScenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
                return anyScenes;
            }

            throw new InvalidOperationException("No scenes found in EditorBuildSettings or Assets/Scenes/Main.unity.");
        }

        private static void RunBuild(string relativeApkPath, BuildOptions options, string tag)
        {
            string[] scenes = ResolveScenesToBuild();
            string projectRoot = Directory.GetCurrentDirectory();
            string fullApkPath = Path.GetFullPath(Path.Combine(projectRoot, relativeApkPath.Replace('/', Path.DirectorySeparatorChar)));
            string buildsDir = Path.GetDirectoryName(fullApkPath);
            if (!string.IsNullOrEmpty(buildsDir) && !Directory.Exists(buildsDir))
            {
                Directory.CreateDirectory(buildsDir);
            }
            if (File.Exists(fullApkPath))
            {
                File.Delete(fullApkPath);
            }

            AssetDatabase.SaveAssets();

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullApkPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = options
            };

            Debug.Log($"[AndroidBuildPipeline] Building {tag}: scenes=[{string.Join(", ", scenes)}] -> {fullApkPath}");
            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"[AndroidBuildPipeline] {tag} FAILED: result={summary.result}, totalErrors={summary.totalErrors}, time={summary.totalTime.TotalSeconds:F1}s");
            }

            var fi = new FileInfo(fullApkPath);
            if (!fi.Exists || fi.Length < 1024 * 1024)
            {
                throw new InvalidOperationException(
                    $"[AndroidBuildPipeline] {tag} output APK missing or unexpectedly small: {fullApkPath} ({(fi.Exists ? fi.Length : 0)} bytes)");
            }

            double sizeMb = fi.Length / (1024.0 * 1024.0);
            Debug.Log(
                $"[AndroidBuildPipeline] Build succeeded ({tag}) | APK={fullApkPath} | Size={sizeMb:F2} MB ({fi.Length} B) | " +
                $"Arch={PlayerSettings.Android.targetArchitectures} | Time={summary.totalTime.TotalSeconds:F1}s");
        }
    }
}
