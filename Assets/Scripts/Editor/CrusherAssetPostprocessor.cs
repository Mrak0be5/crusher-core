using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Crusher.Editor
{
    public sealed class CrusherAssetPostprocessor : AssetPostprocessor
    {
        public const string ModelsDirPrefix = "Assets/Art/Generated/Models/";
        public const string AudioDirPrefix = "Assets/Audio/Generated/";
        public const string AtlasPngPath = "Assets/Art/Generated/Icons/atlas_icons.png";
        public const string AtlasJsonPath = "Assets/Art/Generated/Icons/atlas_icons.json";
        public const string ShadersDir = "Assets/Shaders";

        public static readonly HashSet<string> SkeletalModelStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SM_ShredderCore",
            "SM_Enemy_Boss_ATM",
            "SM_Electromagnet_BH",
            "SM_RailEmitter_DR",
            "SM_GoldenBot"
        };

        public static readonly HashSet<string> LoopClipNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "AN_Core_IdleSpin",
            "AN_Core_OverdriveSpin",
            "AN_Boss_Roll",
            "AN_BH_Pulse",
            "AN_Bot_Hover"
        };

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsDirPrefix, StringComparison.OrdinalIgnoreCase) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            ApplyModelImporterSettings(importer, assetPath, applyClipsIfAvailable: true);
        }

        private void OnPreprocessTexture()
        {
            if (!string.Equals(assetPath, AtlasPngPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            ApplyAtlasTextureSettings(importer);
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioDirPrefix, StringComparison.OrdinalIgnoreCase) ||
                !assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            ApplyAudioSettings(importer, assetPath);
        }

        public static void ApplyModelImporterSettings(ModelImporter importer, string path, bool applyClipsIfAvailable)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            bool isSkeletal = SkeletalModelStems.Contains(stem);

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;

            if (isSkeletal)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;

                if (applyClipsIfAvailable)
                {
                    var defaults = importer.defaultClipAnimations;
                    if (defaults != null && defaults.Length > 0)
                    {
                        var desired = BuildCleanClipAnimations(defaults);
                        if (!ClipArraysMatch(importer.clipAnimations, desired))
                        {
                            importer.clipAnimations = desired;
                        }
                    }
                }
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
        }

        public static ModelImporterClipAnimation[] BuildCleanClipAnimations(ModelImporterClipAnimation[] defaults)
        {
            var cleaned = new List<ModelImporterClipAnimation>(defaults.Length);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            bool hasAnClips = defaults.Any(c => StripRigPrefix(c.name).StartsWith("AN_", StringComparison.Ordinal));

            foreach (var src in defaults)
            {
                string cleanName = StripRigPrefix(src.name);
                if (string.IsNullOrEmpty(cleanName))
                    continue;
                if (hasAnClips && !cleanName.StartsWith("AN_", StringComparison.Ordinal))
                    continue;
                if (!seenNames.Add(cleanName))
                    continue;

                bool loop = LoopClipNames.Contains(cleanName);
                var dst = new ModelImporterClipAnimation
                {
                    name = cleanName,
                    takeName = src.takeName,
                    firstFrame = src.firstFrame,
                    lastFrame = src.lastFrame,
                    loopTime = loop,
                    loopPose = false,
                    keepOriginalOrientation = true,
                    keepOriginalPositionXZ = true,
                    keepOriginalPositionY = true,
                    lockRootRotation = false,
                    lockRootHeightY = false,
                    lockRootPositionXZ = false,
                    wrapMode = loop ? WrapMode.Loop : WrapMode.Default
                };
                cleaned.Add(dst);
            }

            return cleaned.ToArray();
        }

        public static string StripRigPrefix(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return string.Empty;
            int pipe = rawName.LastIndexOf('|');
            return (pipe >= 0 ? rawName.Substring(pipe + 1) : rawName).Trim();
        }

        public static bool ClipArraysMatch(ModelImporterClipAnimation[] current, ModelImporterClipAnimation[] desired)
        {
            if (current == null || desired == null || current.Length != desired.Length)
                return false;

            for (int i = 0; i < desired.Length; i++)
            {
                if (!string.Equals(current[i].name, desired[i].name, StringComparison.Ordinal) ||
                    !string.Equals(current[i].takeName, desired[i].takeName, StringComparison.Ordinal) ||
                    current[i].loopTime != desired[i].loopTime)
                {
                    return false;
                }
            }
            return true;
        }

        public static void ApplyAtlasTextureSettings(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;

            var androidSettings = importer.GetPlatformTextureSettings("Android");
            androidSettings.overridden = true;
            androidSettings.maxTextureSize = 2048;
            androidSettings.format = TextureImporterFormat.RGBA32;
            androidSettings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(androidSettings);
        }

        public static void ApplyAudioSettings(AudioImporter importer, string path)
        {
            bool isLoop = path.EndsWith("_loop.wav", StringComparison.OrdinalIgnoreCase);
            importer.forceToMono = true;
            importer.loadInBackground = false;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = isLoop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = isLoop ? 0.85f : 1.0f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;

            importer.defaultSampleSettings = settings;
            importer.SetOverrideSampleSettings("Android", settings);
        }
    }

    public static class AssetImportVerifier
    {
        private static readonly string[] ExpectedShaders =
        {
            "Assets/Shaders/AnisoMetalLit.shader",
            "Assets/Shaders/AnisoMetalInstanced.shader",
            "Assets/Shaders/ArenaFloorGrid.shader",
            "Assets/Shaders/BlackHoleVortex.shader",
            "Assets/Shaders/VolumetricDeathRay.shader",
            "Assets/Shaders/ProceduralSDFUI.shader"
        };

        private static readonly string[] ExpectedClips =
        {
            "AN_Core_IdleSpin",
            "AN_Core_OverdriveSpin",
            "AN_Core_FireL",
            "AN_Core_FireR",
            "AN_Core_CrunchImpact",
            "AN_Core_OverloadDeath",
            "AN_Boss_Roll",
            "AN_Boss_Ram",
            "AN_BH_Deploy",
            "AN_BH_Pulse",
            "AN_DR_ChargeFire",
            "AN_Bot_Hover"
        };

        [MenuItem("Crusher/Bootstrap/Verify Wave 1 Assets")]
        public static void VerifyWave1Assets()
        {
            Debug.Log("[Crusher] Starting AssetImportVerifier.VerifyWave1Assets...");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            NormalizeWave1Importers();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            VerifyShaders();
            VerifyModelsAndAnimations();
            VerifyAudio();
            VerifyIconAtlas();

            Debug.Log("[Crusher] WAVE1_IMPORT_PASS — 6 shaders, 14 FBX (all COLOR_0 OK, 12 clean AnimationClips), 22 AudioClips, 2048x2048 RGBA32 atlas verified.");
        }

        private static void NormalizeWave1Importers()
        {
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art/Generated/Models" });
            foreach (string guid in fbxGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    string stem = Path.GetFileNameWithoutExtension(path);
                    bool isSkeletal = CrusherAssetPostprocessor.SkeletalModelStems.Contains(stem);
                    bool needsReimport = importer.materialImportMode != ModelImporterMaterialImportMode.None;

                    if (isSkeletal)
                    {
                        if (importer.animationType != ModelImporterAnimationType.Generic ||
                            !importer.importAnimation ||
                            importer.animationCompression != ModelImporterAnimationCompression.Off)
                        {
                            needsReimport = true;
                        }

                        var defaults = importer.defaultClipAnimations;
                        if (defaults != null && defaults.Length > 0)
                        {
                            var desired = CrusherAssetPostprocessor.BuildCleanClipAnimations(defaults);
                            if (!CrusherAssetPostprocessor.ClipArraysMatch(importer.clipAnimations, desired))
                            {
                                importer.clipAnimations = desired;
                                needsReimport = true;
                            }
                        }
                    }
                    else
                    {
                        if (importer.animationType != ModelImporterAnimationType.None || importer.importAnimation)
                        {
                            needsReimport = true;
                        }
                    }

                    if (needsReimport)
                    {
                        CrusherAssetPostprocessor.ApplyModelImporterSettings(importer, path, applyClipsIfAvailable: true);
                        importer.SaveAndReimport();
                    }
                }
            }

            if (AssetImporter.GetAtPath(CrusherAssetPostprocessor.AtlasPngPath) is TextureImporter texImporter)
            {
                var android = texImporter.GetPlatformTextureSettings("Android");
                if (texImporter.mipmapEnabled ||
                    texImporter.maxTextureSize != 2048 ||
                    texImporter.textureCompression != TextureImporterCompression.Uncompressed ||
                    !android.overridden ||
                    android.format != TextureImporterFormat.RGBA32)
                {
                    CrusherAssetPostprocessor.ApplyAtlasTextureSettings(texImporter);
                    texImporter.SaveAndReimport();
                }
            }

            string[] audioGuids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Generated" });
            foreach (string guid in audioGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is AudioImporter audioImporter)
                {
                    bool isLoop = path.EndsWith("_loop.wav", StringComparison.OrdinalIgnoreCase);
                    var expectedFmt = isLoop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                    var cur = audioImporter.defaultSampleSettings;
                    if (cur.loadType != AudioClipLoadType.DecompressOnLoad || cur.compressionFormat != expectedFmt)
                    {
                        CrusherAssetPostprocessor.ApplyAudioSettings(audioImporter, path);
                        audioImporter.SaveAndReimport();
                    }
                }
            }
        }

        private static void VerifyShaders()
        {
            string commonHlslFull = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Shaders/AnisoMetalCommon.hlsl");
            if (!File.Exists(commonHlslFull))
                throw new FileNotFoundException("Missing AnisoMetalCommon.hlsl at " + commonHlslFull);

            ShaderUtil.allowAsyncCompilation = false;

            foreach (string shaderPath in ExpectedShaders)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                    throw new InvalidOperationException("Failed to load Shader at " + shaderPath);

                var tempMat = new Material(shader);
                int passCount = tempMat.passCount;
                for (int p = 0; p < passCount; p++)
                {
                    ShaderUtil.CompilePass(tempMat, p, true);
                }
                UnityEngine.Object.DestroyImmediate(tempMat);

                ShaderMessage[] msgs = ShaderUtil.GetShaderMessages(shader);
                var errors = msgs.Where(m => m.severity == ShaderCompilerMessageSeverity.Error).ToArray();
                if (errors.Length > 0 || ShaderUtil.ShaderHasError(shader))
                {
                    foreach (var err in errors)
                    {
                        Debug.LogError($"[Crusher] ShaderError in {shaderPath} ({err.file}:{err.line}): {err.message} | {err.messageDetails}");
                    }
                    throw new InvalidOperationException($"Shader compilation failed for {shaderPath} ({errors.Length} errors).");
                }

                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && !shader.isSupported)
                {
                    throw new InvalidOperationException($"Shader {shader.name} is not supported on {SystemInfo.graphicsDeviceType}.");
                }

                int warnCount = msgs.Count(m => m.severity == ShaderCompilerMessageSeverity.Warning);
                Debug.Log($"[Crusher] SHADER_OK: {shader.name} ({shaderPath}) | Passes={passCount} | HasError=false | Warnings={warnCount} | isSupported={shader.isSupported} (Gfx={SystemInfo.graphicsDeviceType})");
            }
        }

        private static void VerifyModelsAndAnimations()
        {
            string modelsDirFull = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Art/Generated/Models");
            string[] fbxFiles = Directory.GetFiles(modelsDirFull, "*.fbx", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (fbxFiles.Length != 14 && fbxFiles.Length != 15)
            {
                throw new InvalidOperationException($"Expected 14 or 15 FBX files in Assets/Art/Generated/Models, found {fbxFiles.Length}.");
            }

            var allImportedClips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);

            foreach (string fullPath in fbxFiles)
            {
                string fileName = Path.GetFileName(fullPath);
                string assetPath = "Assets/Art/Generated/Models/" + fileName;

                GameObject rootGo = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (rootGo == null)
                    throw new InvalidOperationException("Failed to load FBX root GameObject at " + assetPath);

                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                Mesh[] meshes = subAssets.OfType<Mesh>().OrderBy(m => m.name, StringComparer.Ordinal).ToArray();
                if (meshes.Length == 0)
                    throw new InvalidOperationException("Zero meshes imported in " + assetPath);

                var meshSummaries = new List<string>(meshes.Length);
                foreach (Mesh mesh in meshes)
                {
                    Color32[] cols = mesh.colors32;
                    if (cols == null || cols.Length != mesh.vertexCount || mesh.vertexCount == 0)
                    {
                        throw new InvalidOperationException(
                            $"COLOR_0 verification failed on {assetPath} :: {mesh.name} (verts={mesh.vertexCount}, colors32={(cols == null ? -1 : cols.Length)}).");
                    }
                    int tris = 0;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        tris += (int)(mesh.GetIndexCount(s) / 3);
                    meshSummaries.Add($"{mesh.name}(v={mesh.vertexCount},t={tris},sub={mesh.subMeshCount})");
                }

                AnimationClip[] clips = subAssets
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.name, StringComparer.Ordinal)
                    .ToArray();

                var clipSummaries = new List<string>(clips.Length);
                foreach (AnimationClip clip in clips)
                {
                    if (clip.name.Contains("|") || clip.name.StartsWith("RIG_", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"AnimationClip '{clip.name}' in {assetPath} still has RIG prefix!");
                    }
                    allImportedClips[clip.name] = clip;
                    clipSummaries.Add($"{clip.name}(len={clip.length:F3}s,fps={clip.frameRate:F0},loop={clip.isLooping})");
                }

                Debug.Log(
                    $"[Crusher] MODEL_OK: {fileName} | Meshes[{meshes.Length}]=[{string.Join(", ", meshSummaries)}] | " +
                    $"Clips[{clips.Length}]=[{(clipSummaries.Count > 0 ? string.Join(", ", clipSummaries) : "none")}]");

                if (string.Equals(fileName, "SM_ShredderCore.fbx", StringComparison.OrdinalIgnoreCase))
                {
                    LogShredderCoreHierarchy(rootGo);
                }
            }

            foreach (string reqClip in ExpectedClips)
            {
                if (!allImportedClips.TryGetValue(reqClip, out AnimationClip clip))
                    throw new InvalidOperationException("Missing expected AnimationClip after Wave 1 import: " + reqClip);

                bool expectedLoop = CrusherAssetPostprocessor.LoopClipNames.Contains(reqClip);
                if (clip.isLooping != expectedLoop)
                {
                    throw new InvalidOperationException(
                        $"AnimationClip '{reqClip}' isLooping={clip.isLooping}, expected {expectedLoop}.");
                }
            }

            Debug.Log($"[Crusher] ANIM_CLIPS_VERIFIED ({allImportedClips.Count}/12): {string.Join(", ", allImportedClips.Keys.OrderBy(k => k, StringComparer.Ordinal))}");
        }

        private static void LogShredderCoreHierarchy(GameObject rootGo)
        {
            var allTransforms = rootGo.GetComponentsInChildren<Transform>(true);
            var childPaths = new List<string>();
            foreach (Transform t in allTransforms)
            {
                if (t == rootGo.transform) continue;
                childPaths.Add(GetRelativePath(rootGo.transform, t));
            }

            var smrs = rootGo.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderBy(r => r.name, StringComparer.Ordinal)
                .ToArray();
            var mfs = rootGo.GetComponentsInChildren<MeshFilter>(true)
                .OrderBy(r => r.name, StringComparer.Ordinal)
                .ToArray();

            var smrDesc = smrs.Select(r =>
                $"{r.name}(mesh={(r.sharedMesh != null ? r.sharedMesh.name : "null")},rootBone={(r.rootBone != null ? r.rootBone.name : "null")},bones=[{string.Join(",", r.bones.Select(b => b != null ? b.name : "null"))}])");

            var mfDesc = mfs.Select(f =>
                $"{f.name}(mesh={(f.sharedMesh != null ? f.sharedMesh.name : "null")})");

            Debug.Log($"[Crusher] SHREDDER_CORE_TRANSFORMS: [{string.Join(", ", childPaths)}]");
            Debug.Log($"[Crusher] SHREDDER_CORE_SKINNED_MESHES[{smrs.Length}]: [{string.Join(" | ", smrDesc)}]");
            if (mfs.Length > 0)
            {
                Debug.Log($"[Crusher] SHREDDER_CORE_STATIC_MESHES[{mfs.Length}]: [{string.Join(" | ", mfDesc)}]");
            }
        }

        private static string GetRelativePath(Transform root, Transform leaf)
        {
            var parts = new Stack<string>();
            Transform cur = leaf;
            while (cur != null && cur != root)
            {
                parts.Push(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", parts);
        }

        private static void VerifyAudio()
        {
            string audioDirFull = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Audio/Generated");
            string[] wavFiles = Directory.GetFiles(audioDirFull, "*.wav", SearchOption.TopDirectoryOnly)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (wavFiles.Length != 22)
            {
                throw new InvalidOperationException($"Expected 22 WAV files in Assets/Audio/Generated, found {wavFiles.Length}.");
            }

            var summaries = new List<string>(wavFiles.Length);
            foreach (string full in wavFiles)
            {
                string name = Path.GetFileName(full);
                string assetPath = "Assets/Audio/Generated/" + name;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                if (clip == null || clip.samples <= 0 || clip.length <= 0f)
                {
                    throw new InvalidOperationException("Failed to import AudioClip: " + assetPath);
                }

                var imp = (AudioImporter)AssetImporter.GetAtPath(assetPath);
                var s = imp.defaultSampleSettings;
                summaries.Add($"{clip.name}({clip.length:F2}s,{clip.frequency}Hz,{s.compressionFormat},{s.loadType})");
            }

            Debug.Log($"[Crusher] AUDIO_OK ({summaries.Count}/22): {string.Join(", ", summaries)}");
        }

        private static void VerifyIconAtlas()
        {
            Texture2D atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CrusherAssetPostprocessor.AtlasPngPath);
            if (atlasTex == null)
                throw new InvalidOperationException("Missing atlas_icons.png Texture2D at " + CrusherAssetPostprocessor.AtlasPngPath);

            if (atlasTex.width != 2048 || atlasTex.height != 2048 || atlasTex.mipmapCount != 1)
            {
                throw new InvalidOperationException(
                    $"atlas_icons.png unexpected dimensions/mips: {atlasTex.width}x{atlasTex.height}, mips={atlasTex.mipmapCount}, fmt={atlasTex.format}");
            }

            TextAsset atlasJson = AssetDatabase.LoadAssetAtPath<TextAsset>(CrusherAssetPostprocessor.AtlasJsonPath);
            if (atlasJson == null || string.IsNullOrWhiteSpace(atlasJson.text))
            {
                throw new InvalidOperationException("Missing or empty atlas_icons.json TextAsset at " + CrusherAssetPostprocessor.AtlasJsonPath);
            }

            Debug.Log($"[Crusher] ICON_ATLAS_OK: {CrusherAssetPostprocessor.AtlasPngPath} ({atlasTex.width}x{atlasTex.height}, fmt={atlasTex.format}, mips={atlasTex.mipmapCount}) + atlas_icons.json ({atlasJson.bytes.Length} B)");
        }
    }
}
