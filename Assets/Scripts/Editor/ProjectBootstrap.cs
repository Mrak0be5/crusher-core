using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;

namespace Crusher.Editor
{
    public static class ProjectBootstrap
    {
        private const string SettingsDir = "Assets/Settings";
        private const string UrpAssetPath = "Assets/Settings/CrusherURP.asset";
        private const string UrpRendererPath = "Assets/Settings/CrusherURP_Renderer.asset";

        private const string FontsDir = "Assets/Art/Generated/Fonts";
        private const string MainTtfAssetPath = "Assets/Art/Generated/Fonts/CrusherBoldSource.ttf";
        private const string SymTtfAssetPath = "Assets/Art/Generated/Fonts/CrusherSymbolsSource.ttf";
        private const string MainSdfAssetPath = "Assets/Art/Generated/Fonts/CrusherBoldSDF.asset";
        private const string SymSdfAssetPath = "Assets/Art/Generated/Fonts/CrusherSymbolsSDF.asset";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        private const string CyrillicUpper = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";
        private const string CyrillicLower = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        private const string ExtraMainSymbols = "×%$—+•→–₽";
        private const string FallbackStarSymbols = "★☆✓⚡⚠◆▲▼◀▶♦●○■□";

        [MenuItem("Crusher/Bootstrap/Init All (Phase 0)")]
        public static void InitAll()
        {
            Debug.Log("[ProjectBootstrap] Starting Phase 0 InitAll...");

            EnsureDirectory(SettingsDir);
            EnsureDirectory(FontsDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ConfigurePlayerSettings();
            var (urpAsset, rendererData) = ConfigureUrpPipeline();
            var mainFont = ImportTmpAndBuildCyrillicFont();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            VerifyStopGate0(urpAsset, rendererData, mainFont);
        }

        private static void EnsureDirectory(string assetRelativePath)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), assetRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
            }
        }

        private static void ConfigurePlayerSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }

            PlayerSettings.companyName = "CrusherCore";
            PlayerSettings.productName = "Crusher Core: Hyper Shredder";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.crushercore.hypershredder");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.crushercore.hypershredder");

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);
            PlayerSettings.stripEngineCode = true;

            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            Debug.Log("[ProjectBootstrap] PlayerSettings configured (Linear, IL2CPP, ARM64, API 26->35, Activity, Portrait).");
        }

        private static (UniversalRenderPipelineAsset, UniversalRendererData) ConfigureUrpPipeline()
        {
            if (AssetDatabase.LoadMainAssetAtPath(UrpAssetPath) != null)
                AssetDatabase.DeleteAsset(UrpAssetPath);
            if (AssetDatabase.LoadMainAssetAtPath(UrpRendererPath) != null)
                AssetDatabase.DeleteAsset(UrpRendererPath);

            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            var getDefaultPpd = typeof(PostProcessData).GetMethod(
                "GetDefaultPostProcessData",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getDefaultPpd != null)
            {
                rendererData.postProcessData = getDefaultPpd.Invoke(null, null) as PostProcessData;
            }
            rendererData.renderingMode = RenderingMode.Forward;

            AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
            ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(rendererData);

            var urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
            urpAsset.supportsHDR = true;
            urpAsset.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
            urpAsset.msaaSampleCount = 2;
            urpAsset.supportsCameraOpaqueTexture = false;
            urpAsset.supportsCameraDepthTexture = false;
            urpAsset.mainLightShadowmapResolution = 1024;
            urpAsset.shadowDistance = 25f;
            urpAsset.shadowCascadeCount = 1;

            AssetDatabase.CreateAsset(urpAsset, UrpAssetPath);
            ResourceReloader.ReloadAllNullIn(urpAsset, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(urpAsset);

            GraphicsSettings.defaultRenderPipeline = urpAsset;

            int originalQuality = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            for (int i = 0; i < qualityNames.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urpAsset;
            }
            QualitySettings.SetQualityLevel(originalQuality, false);

            var globalSettingsType = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            var ensureMethod = globalSettingsType?.GetMethod(
                "Ensure",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            ensureMethod?.Invoke(null, null);

            Debug.Log("[ProjectBootstrap] Created CrusherURP.asset + CrusherURP_Renderer.asset (HDR 32b, MSAA 2x, Forward+, OpaqueTex=false, Shadow 1024@25m c1).");
            return (urpAsset, rendererData);
        }

        private static TMP_FontAsset ImportTmpAndBuildCyrillicFont()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath) == null)
            {
                string tmpPackage = Path.Combine(
                    EditorApplication.applicationContentsPath,
                    "Resources/PackageManager/BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
                if (!File.Exists(tmpPackage))
                {
                    throw new FileNotFoundException("TMP Essential Resources.unitypackage not found at: " + tmpPackage);
                }

                Debug.Log("[ProjectBootstrap] Extracting TMP Essential Resources synchronously for batchmode...");
                ExtractUnityPackageSync(tmpPackage);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            var tmpSettingsAsset = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (tmpSettingsAsset == null)
            {
                throw new InvalidOperationException("TMP Settings.asset missing after extraction at " + TmpSettingsPath);
            }

            typeof(TMP_Settings)
                .GetField("s_Instance", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, tmpSettingsAsset);

            var mobileSdfShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader");
            if (mobileSdfShader != null)
            {
                typeof(ShaderUtilities)
                    .GetField("k_ShaderRef_MobileSDF", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.SetValue(null, mobileSdfShader);
            }

            CopySystemFontOrFallback(
                new[] { @"C:\Windows\Fonts\arialbd.ttf", @"C:\Windows\Fonts\segoeuib.ttf", @"C:\Windows\Fonts\segoeui.ttf" },
                MainTtfAssetPath);

            CopySystemFontOrFallback(
                new[] { @"C:\Windows\Fonts\seguisym.ttf", @"C:\Windows\Fonts\segoeui.ttf" },
                SymTtfAssetPath);

            EnsureFontIncludeData(MainTtfAssetPath);
            EnsureFontIncludeData(SymTtfAssetPath);

            var sbAscii = new StringBuilder(192);
            for (int c = 32; c <= 126; c++)
                sbAscii.Append((char)c);

            string mainCharset = sbAscii + CyrillicUpper + CyrillicLower + ExtraMainSymbols;

            TMP_FontAsset symFontAsset = CreateAndSaveSdfAsset(
                SymTtfAssetPath,
                SymSdfAssetPath,
                FallbackStarSymbols,
                pointSize: 44,
                padding: 5,
                atlasW: 256,
                atlasH: 256);

            TMP_FontAsset mainFontAsset = CreateAndSaveSdfAsset(
                MainTtfAssetPath,
                MainSdfAssetPath,
                mainCharset,
                pointSize: 54,
                padding: 6,
                atlasW: 1024,
                atlasH: 1024);

            mainFontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { symFontAsset };
            EditorUtility.SetDirty(mainFontAsset);

            var so = new SerializedObject(tmpSettingsAsset);
            var defaultFontProp = so.FindProperty("m_defaultFontAsset");
            if (defaultFontProp == null)
            {
                throw new InvalidOperationException("m_defaultFontAsset property not found on TMP_Settings.");
            }
            defaultFontProp.objectReferenceValue = mainFontAsset;

            var clearDynProp = so.FindProperty("m_clearDynamicDataOnBuild");
            if (clearDynProp != null) clearDynProp.boolValue = false;

            var warnProp = so.FindProperty("m_warningsDisabled");
            if (warnProp != null) warnProp.boolValue = true;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tmpSettingsAsset);

            Debug.Log("[ProjectBootstrap] Built CrusherBoldSDF.asset (+ CrusherSymbolsSDF fallback) and assigned as TMP_Settings default font.");
            return mainFontAsset;
        }

        private sealed class TarAssetEntry
        {
            public string Pathname;
            public byte[] AssetBytes;
            public byte[] MetaBytes;
        }

        private static void ExtractUnityPackageSync(string packagePath)
        {
            var entries = new Dictionary<string, TarAssetEntry>(StringComparer.OrdinalIgnoreCase);
            using (var fs = File.OpenRead(packagePath))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                byte[] header = new byte[512];
                while (ReadExactOrEof(gz, header, 512))
                {
                    if (header[0] == 0)
                        break;

                    string rawName = ReadNullTerminatedAscii(header, 0, 100);
                    string prefix = ReadNullTerminatedAscii(header, 345, 155);
                    string fullName = string.IsNullOrEmpty(prefix) ? rawName : (prefix + "/" + rawName);
                    long size = ParseOctal(header, 124, 12);
                    byte typeFlag = header[156];

                    byte[] payload = size > 0 ? new byte[size] : Array.Empty<byte>();
                    if (size > 0)
                        ReadExact(gz, payload, (int)size);

                    int rem = (int)(size % 512);
                    if (rem != 0)
                        SkipBytes(gz, 512 - rem);

                    if (typeFlag != (byte)'0' && typeFlag != 0)
                        continue;

                    string clean = fullName.TrimStart('.', '/');
                    int slash = clean.IndexOf('/');
                    if (slash <= 0)
                        continue;

                    string guid = clean.Substring(0, slash);
                    string leaf = clean.Substring(slash + 1);
                    if (!entries.TryGetValue(guid, out var entry))
                    {
                        entry = new TarAssetEntry();
                        entries[guid] = entry;
                    }

                    if (leaf == "pathname")
                    {
                        string text = Encoding.UTF8.GetString(payload);
                        int nl = text.IndexOfAny(new[] { '\r', '\n', '\0' });
                        entry.Pathname = (nl >= 0 ? text.Substring(0, nl) : text).Trim();
                    }
                    else if (leaf == "asset")
                    {
                        entry.AssetBytes = payload;
                    }
                    else if (leaf == "asset.meta")
                    {
                        entry.MetaBytes = payload;
                    }
                }
            }

            string projectRoot = Directory.GetCurrentDirectory();
            foreach (var entry in entries.Values)
            {
                if (string.IsNullOrEmpty(entry.Pathname) || entry.Pathname.IndexOf("TMP_SDF-HDRP", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (entry.AssetBytes == null)
                {
                    string dirFull = Path.Combine(projectRoot, entry.Pathname.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(dirFull);
                    if (entry.MetaBytes != null)
                        File.WriteAllBytes(dirFull + ".meta", entry.MetaBytes);
                }
            }

            foreach (var entry in entries.Values)
            {
                if (string.IsNullOrEmpty(entry.Pathname) || entry.Pathname.IndexOf("TMP_SDF-HDRP", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (entry.AssetBytes != null)
                {
                    string fileFull = Path.Combine(projectRoot, entry.Pathname.Replace('/', Path.DirectorySeparatorChar));
                    string parent = Path.GetDirectoryName(fileFull);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    File.WriteAllBytes(fileFull, entry.AssetBytes);
                    if (entry.MetaBytes != null)
                        File.WriteAllBytes(fileFull + ".meta", entry.MetaBytes);
                }
            }
        }

        private static bool ReadExactOrEof(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                {
                    if (offset == 0)
                        return false;
                    throw new EndOfStreamException("Unexpected EOF in unitypackage tar stream.");
                }
                offset += read;
            }
            return true;
        }

        private static void ReadExact(Stream stream, byte[] buffer, int count)
        {
            if (!ReadExactOrEof(stream, buffer, count))
                throw new EndOfStreamException("Unexpected EOF while reading tar entry payload.");
        }

        private static void SkipBytes(Stream stream, int count)
        {
            byte[] scratch = new byte[512];
            int remaining = count;
            while (remaining > 0)
            {
                int step = Math.Min(remaining, scratch.Length);
                ReadExact(stream, scratch, step);
                remaining -= step;
            }
        }

        private static string ReadNullTerminatedAscii(byte[] buffer, int offset, int maxLen)
        {
            int len = 0;
            while (len < maxLen && buffer[offset + len] != 0)
                len++;
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, offset, len);
        }

        private static long ParseOctal(byte[] buffer, int offset, int maxLen)
        {
            long value = 0;
            bool seenDigit = false;
            for (int i = offset; i < offset + maxLen; i++)
            {
                byte b = buffer[i];
                if (b >= (byte)'0' && b <= (byte)'7')
                {
                    seenDigit = true;
                    value = (value << 3) + (b - (byte)'0');
                }
                else if (seenDigit)
                {
                    break;
                }
            }
            return value;
        }

        private static void CopySystemFontOrFallback(string[] candidates, string destAssetPath)
        {
            string destFull = Path.Combine(Directory.GetCurrentDirectory(), destAssetPath.Replace('/', Path.DirectorySeparatorChar));
            foreach (string src in candidates)
            {
                if (File.Exists(src))
                {
                    File.Copy(src, destFull, true);
                    AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceSynchronousImport);
                    return;
                }
            }
            throw new FileNotFoundException("None of the candidate Windows fonts found for " + destAssetPath);
        }

        private static void EnsureFontIncludeData(string fontAssetPath)
        {
            if (AssetImporter.GetAtPath(fontAssetPath) is TrueTypeFontImporter importer)
            {
                if (!importer.includeFontData || importer.fontTextureCase != FontTextureCase.Dynamic)
                {
                    importer.includeFontData = true;
                    importer.fontTextureCase = FontTextureCase.Dynamic;
                    importer.SaveAndReimport();
                }
            }
        }

        private static TMP_FontAsset CreateAndSaveSdfAsset(
            string ttfAssetPath,
            string outSdfPath,
            string charsToBake,
            int pointSize,
            int padding,
            int atlasW,
            int atlasH)
        {
            if (AssetDatabase.LoadMainAssetAtPath(outSdfPath) != null)
            {
                AssetDatabase.DeleteAsset(outSdfPath);
            }

            Font srcFont = AssetDatabase.LoadAssetAtPath<Font>(ttfAssetPath);
            if (srcFont == null)
            {
                throw new InvalidOperationException("Failed to load source Font at " + ttfAssetPath);
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                srcFont,
                pointSize,
                padding,
                GlyphRenderMode.SDFAA,
                atlasW,
                atlasH,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: false);

            if (fontAsset == null)
            {
                throw new InvalidOperationException("TMP_FontAsset.CreateFontAsset returned null for " + ttfAssetPath);
            }

            AssetDatabase.CreateAsset(fontAsset, outSdfPath);

            string baseName = Path.GetFileNameWithoutExtension(outSdfPath);
            if (fontAsset.atlasTexture != null)
            {
                fontAsset.atlasTexture.name = baseName + " Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            }
            if (fontAsset.material != null)
            {
                fontAsset.material.name = baseName + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            fontAsset.TryAddCharacters(charsToBake, out string missing);
            if (!string.IsNullOrEmpty(missing))
            {
                Debug.Log($"[ProjectBootstrap] {baseName}: missing from primary face ({missing.Length}): {missing}");
            }

            var faSo = new SerializedObject(fontAsset);
            var clearProp = faSo.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearProp != null)
            {
                clearProp.boolValue = false;
                faSo.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(fontAsset);
            if (fontAsset.atlasTexture != null) EditorUtility.SetDirty(fontAsset.atlasTexture);
            if (fontAsset.material != null) EditorUtility.SetDirty(fontAsset.material);

            return fontAsset;
        }

        private static void VerifyStopGate0(
            UniversalRenderPipelineAsset urpAsset,
            UniversalRendererData rendererData,
            TMP_FontAsset mainFont)
        {
            var reloadedUrp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            var reloadedRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            var reloadedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainSdfAssetPath);
            var tmpSettingsObj = AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath);

            if (reloadedUrp == null || reloadedRenderer == null)
                throw new InvalidOperationException("URP assets missing on disk after SaveAssets.");
            if (reloadedFont == null || reloadedFont.atlasTexture == null || reloadedFont.material == null)
                throw new InvalidOperationException("CrusherBoldSDF.asset or its sub-assets (Atlas/Material) missing.");
            if (reloadedFont.material.shader == null || reloadedFont.material.shader.name == "Hidden/InternalErrorShader")
                throw new InvalidOperationException("CrusherBoldSDF material shader is missing or broken.");
            if (GraphicsSettings.defaultRenderPipeline != reloadedUrp)
                throw new InvalidOperationException("GraphicsSettings.defaultRenderPipeline is not CrusherURP.asset.");
            if (!reloadedUrp.supportsHDR || reloadedUrp.msaaSampleCount != 2 || reloadedUrp.supportsCameraOpaqueTexture)
                throw new InvalidOperationException("URP HDR/MSAA/OpaqueTexture mismatch.");
            if (reloadedUrp.shadowCascadeCount != 1 || !Mathf.Approximately(reloadedUrp.shadowDistance, 25f) || reloadedUrp.mainLightShadowmapResolution != 1024)
                throw new InvalidOperationException("URP Shadow settings mismatch.");
            if (reloadedRenderer.renderingMode != RenderingMode.Forward)
                throw new InvalidOperationException("UniversalRendererData.renderingMode is not Forward.");

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                throw new InvalidOperationException("ColorSpace is not Linear.");
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != "com.crushercore.hypershredder")
                throw new InvalidOperationException("ApplicationIdentifier mismatch.");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                throw new InvalidOperationException("ScriptingBackend is not IL2CPP.");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                throw new InvalidOperationException("TargetArchitectures is not ARM64.");
            if ((int)PlayerSettings.Android.minSdkVersion != 26 || (int)PlayerSettings.Android.targetSdkVersion != 35)
                throw new InvalidOperationException("Android min/target SDK mismatch.");
            if (PlayerSettings.Android.applicationEntry != AndroidApplicationEntry.Activity)
                throw new InvalidOperationException("AndroidApplicationEntry is not Activity.");
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
                throw new InvalidOperationException("Default orientation is not Portrait.");

            var sbReq = new StringBuilder(192);
            for (int c = 32; c <= 126; c++)
                sbReq.Append((char)c);
            sbReq.Append(CyrillicUpper).Append(CyrillicLower).Append("×%$★—+•→");

            var missingReq = new StringBuilder();
            foreach (char ch in sbReq.ToString())
            {
                if (!reloadedFont.HasCharacter(ch, searchFallbacks: true, tryAddCharacter: false))
                {
                    missingReq.Append(ch);
                }
            }
            if (missingReq.Length > 0)
            {
                throw new InvalidOperationException("STOP-GATE 0 FAILED: Missing required glyphs in CrusherBoldSDF (+fallback): " + missingReq);
            }

            var tmpSo = new SerializedObject(tmpSettingsObj);
            if (tmpSo.FindProperty("m_defaultFontAsset")?.objectReferenceValue != reloadedFont)
            {
                throw new InvalidOperationException("TMP Settings m_defaultFontAsset does not point to CrusherBoldSDF.asset.");
            }

            Debug.Log(
                $"[ProjectBootstrap] STOP_GATE_0_PASS | URP={UrpAssetPath} (Forward+, HDR, MSAA=2, Shadow=1024@25m) | " +
                $"Font={MainSdfAssetPath} (PrimaryGlyphs={reloadedFont.characterTable.Count}, VerifiedChars={sbReq.Length}, Missing=0) | " +
                $"AppId=com.crushercore.hypershredder (v{PlayerSettings.bundleVersion}, API 26->35, IL2CPP ARM64, Activity, Portrait)");
        }
    }
}
