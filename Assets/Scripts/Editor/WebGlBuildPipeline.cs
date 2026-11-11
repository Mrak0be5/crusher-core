using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Crusher.Editor
{
    public static class WebGlBuildPipeline
    {
        private const string DefaultScenePath = "Assets/Scenes/Main.unity";
        private const string WebGlOutputRelativePath = "Builds/WebGL";
        private const string ShellGeneratorRelativePath = "tools/gen_webgl_shell.py";

        [MenuItem("Crusher/Build/3. Build Release WebGL (Brotli + DecompressionFallback, WebGL2)")]
        public static void BuildReleaseWebGl()
        {
            Debug.Log("[WebGlBuildPipeline] Configuring Release WebGL profile (Linear, OpenGLES3/WebGL2, Brotli + decompressionFallback=true)...");
            EnsureWebGlTarget();

            PlayerSettings.bundleVersion = "2.0.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.threadsSupport = false; // GitHub Pages lacks COOP/COEP headers for SharedArrayBuffer
            PlayerSettings.WebGL.template = "APPLICATION:Default";

            string[] scenes = ResolveScenesToBuild();
            string projectRoot = Directory.GetCurrentDirectory();
            string fullOutDir = Path.GetFullPath(Path.Combine(projectRoot, WebGlOutputRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (Directory.Exists(fullOutDir))
            {
                Directory.Delete(fullOutDir, recursive: true);
            }
            Directory.CreateDirectory(fullOutDir);

            AssetDatabase.SaveAssets();

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOutDir,
                targetGroup = BuildTargetGroup.WebGL,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            Debug.Log($"[WebGlBuildPipeline] Building RELEASE_WEBGL: scenes=[{string.Join(", ", scenes)}] -> {fullOutDir}");
            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"[WebGlBuildPipeline] RELEASE_WEBGL FAILED: result={summary.result}, totalErrors={summary.totalErrors}, time={summary.totalTime.TotalSeconds:F1}s");
            }

            string buildSubDir = Path.Combine(fullOutDir, "Build");
            if (!Directory.Exists(buildSubDir))
            {
                throw new DirectoryNotFoundException("[WebGlBuildPipeline] Missing Build/ subfolder in WebGL output: " + buildSubDir);
            }

            FileInfo[] builtFiles = new DirectoryInfo(buildSubDir).GetFiles("*", SearchOption.TopDirectoryOnly);
            long totalBytes = builtFiles.Sum(f => f.Length);
            double totalMb = totalBytes / (1024.0 * 1024.0);

            WrapWithIndustrialShell(projectRoot, fullOutDir);

            string indexHtml = Path.Combine(fullOutDir, "index.html");
            if (!File.Exists(indexHtml) || new FileInfo(indexHtml).Length < 2048)
            {
                throw new InvalidOperationException("[WebGlBuildPipeline] Missing or invalid index.html after shell wrapping: " + indexHtml);
            }

            Debug.Log(
                $"[WebGlBuildPipeline] Build succeeded (RELEASE_WEBGL) | Dir={fullOutDir} | " +
                $"Payload={totalMb:F2} MB ({totalBytes} B across {builtFiles.Length} files) | " +
                $"Time={summary.totalTime.TotalSeconds:F1}s");
        }

        private static void EnsureWebGlTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    throw new InvalidOperationException("Failed to switch active BuildTarget to WebGL.");
                }
            }
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

        private static void WrapWithIndustrialShell(string projectRoot, string fullWebGlOutDir)
        {
            string scriptPath = Path.GetFullPath(Path.Combine(projectRoot, ShellGeneratorRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("[WebGlBuildPipeline] Shell generator script missing: " + scriptPath);
            }

            string pythonExe = ResolvePythonExe();
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" --build-dir \"{fullWebGlOutDir}\"",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            string stdOut = proc.StandardOutput.ReadToEnd();
            string stdErr = proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(60_000))
            {
                try { proc.Kill(); } catch { }
                throw new TimeoutException("[WebGlBuildPipeline] gen_webgl_shell.py timed out.");
            }

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"[WebGlBuildPipeline] gen_webgl_shell.py exited with {proc.ExitCode}: {stdOut}\n{stdErr}");
            }

            Debug.Log("[WebGlBuildPipeline] Industrial HTML shell generated: " + stdOut.Trim());
        }

        private static string ResolvePythonExe()
        {
            string[] candidates =
            {
                @"C:\Users\hebp\AppData\Local\Programs\Python\Python310\python.exe",
                @"C:\Users\USER\AppData\Local\Programs\Python\Python312\python.exe",
                @"C:\Users\USER\AppData\Local\Programs\Python\Python311\python.exe",
                @"C:\Users\USER\AppData\Local\Programs\Python\Python310\python.exe"
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c))
                {
                    return c;
                }
            }
            return "python";
        }
    }
}
