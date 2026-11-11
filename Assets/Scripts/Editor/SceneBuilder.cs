using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Crusher.Audio;
using Crusher.Config;
using Crusher.Core;
using Crusher.Render;
using Crusher.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Crusher.Editor
{
    /// <summary>
    /// Programmatically builds all Materials, AnimatorControllers, VolumeProfile, CrusherAssetRegistry,
    /// AlwaysIncludedShaders, and Assets/Scenes/Main.unity with zero manual Inspector steps.
    /// </summary>
    public static class SceneBuilder
    {
        public const string MaterialsDir = "Assets/Materials";
        public const string AnimationsDir = "Assets/Animations";
        public const string SettingsDir = "Assets/Settings";
        public const string ScenesDir = "Assets/Scenes";

        public const string RegistryAssetPath = "Assets/Settings/CrusherAssetRegistry.asset";
        public const string VolumeProfilePath = "Assets/Settings/CrusherVolumeProfile.asset";
        public const string FloorMeshAssetPath = "Assets/Settings/ArenaFloorMesh.asset";
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Crusher/Build/Build Materials, Registry & Main Scene (V2 Ultra)")]
        public static void BuildAll()
        {
            Debug.Log("[SceneBuilder] Starting V2 Ultra SceneBuilder.BuildAll...");

            EnsureDir(MaterialsDir);
            EnsureDir(AnimationsDir);
            EnsureDir(SettingsDir);
            EnsureDir(ScenesDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            TuneUrpShadowDistance();
            RegisterAlwaysIncludedShaders();

            CrusherAssetRegistry registry = CreateOrLoadAsset<CrusherAssetRegistry>(RegistryAssetPath);
            PopulateFontsAndIcons(registry);
            PopulateMeshesAndModelPrefabs(registry);
            BuildAllMaterials(registry);
            BuildAllAnimatorControllers(registry);
            PopulateAudioClips(registry);

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            VolumeProfile volumeProfile = BuildGlobalVolumeProfile();
            Mesh floorMesh = BuildArenaFloorMeshAsset();

            BuildMainScene(registry, volumeProfile, floorMesh);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log("[SceneBuilder] SCENE_BUILD_PASS — Registry, 25 Materials, 5 AnimatorControllers, FoundryEnv, Dynamic Lights & Assets/Scenes/Main.unity built.");
        }

        private static void EnsureDir(string assetRelPath)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), assetRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
            }
        }

        private static T CreateOrLoadAsset<T>(string assetPath) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
            {
                return existing;
            }
            T created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        private static void TuneUrpShadowDistance()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/CrusherURP.asset");
            if (urp != null)
            {
                // Covers both 9:20 Portrait (~32m) and 16:9 Landscape Foundry Masts/Bunkers (~38m..55m)
                urp.shadowDistance = 58.0f;
                urp.maxAdditionalLightsCount = 4;

                var so = new SerializedObject(urp);
                SerializedProperty shadowResProp = so.FindProperty("m_MainLightShadowmapResolution");
                if (shadowResProp != null) shadowResProp.intValue = 2048;

                SerializedProperty addModeProp = so.FindProperty("m_AdditionalLightsRenderingMode");
                if (addModeProp != null) addModeProp.intValue = 1; // PerPixel

                SerializedProperty softShadowsProp = so.FindProperty("m_SoftShadowsSupported");
                if (softShadowsProp != null) softShadowsProp.boolValue = true;

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
            }
        }

        private static void RegisterAlwaysIncludedShaders()
        {
            string[] shaderPaths =
            {
                "Assets/Shaders/AnisoMetalLit.shader",
                "Assets/Shaders/AnisoMetalInstanced.shader",
                "Assets/Shaders/ArenaFloorGrid.shader",
                "Assets/Shaders/BlackHoleVortex.shader",
                "Assets/Shaders/VolumetricDeathRay.shader",
                "Assets/Shaders/ProceduralSDFUI.shader",
                "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader"
            };

            var shadersToAdd = new List<Shader>();
            foreach (string p in shaderPaths)
            {
                Shader s = AssetDatabase.LoadAssetAtPath<Shader>(p);
                if (s != null) shadersToAdd.Add(s);
            }
            Shader uiDefault = Shader.Find("UI/Default");
            if (uiDefault != null) shadersToAdd.Add(uiDefault);

            UnityEngine.Object gfxObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gfxObj == null) return;

            var so = new SerializedObject(gfxObj);
            SerializedProperty prop = so.FindProperty("m_AlwaysIncludedShaders");
            if (prop == null) return;

            foreach (Shader sh in shadersToAdd)
            {
                bool exists = false;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    if (prop.GetArrayElementAtIndex(i).objectReferenceValue == sh)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    int idx = prop.arraySize;
                    prop.InsertArrayElementAtIndex(idx);
                    prop.GetArrayElementAtIndex(idx).objectReferenceValue = sh;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gfxObj);
        }

        private static void PopulateFontsAndIcons(CrusherAssetRegistry reg)
        {
            reg.MainFont = MustLoad<TMP_FontAsset>("Assets/Art/Generated/Fonts/CrusherBoldSDF.asset");
            reg.SymbolsFallbackFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Generated/Fonts/CrusherSymbolsSDF.asset");
            reg.IconAtlasTexture = MustLoad<Texture2D>("Assets/Art/Generated/Icons/atlas_icons.png");
            reg.IconAtlasJson = MustLoad<TextAsset>("Assets/Art/Generated/Icons/atlas_icons.json");
        }

        private static void PopulateMeshesAndModelPrefabs(CrusherAssetRegistry reg)
        {
            reg.PrefabArenaFoundryEnv = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Generated/Models/SM_ArenaFoundryEnv.fbx");
            if (reg.PrefabArenaFoundryEnv == null)
            {
                Debug.LogWarning("[SceneBuilder] Optional V2 environment 'Assets/Art/Generated/Models/SM_ArenaFoundryEnv.fbx' not yet imported; scene will run without outer foundry props until re-built.");
            }

            reg.PrefabShredderCore = MustLoad<GameObject>("Assets/Art/Generated/Models/SM_ShredderCore.fbx");
            reg.PrefabBossAtm = MustLoad<GameObject>("Assets/Art/Generated/Models/SM_Enemy_Boss_ATM.fbx");
            reg.PrefabElectromagnetBh = MustLoad<GameObject>("Assets/Art/Generated/Models/SM_Electromagnet_BH.fbx");
            reg.PrefabRailEmitterDr = MustLoad<GameObject>("Assets/Art/Generated/Models/SM_RailEmitter_DR.fbx");
            reg.PrefabGoldenBot = MustLoad<GameObject>("Assets/Art/Generated/Models/SM_GoldenBot.fbx");

            reg.MeshEnemyBasic = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_Enemy_Basic.fbx", "SM_Enemy_Basic");
            reg.MeshEnemyFast = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_Enemy_Fast.fbx", "SM_Enemy_Fast");
            reg.MeshEnemyTank = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_Enemy_Tank.fbx", "SM_Enemy_Tank");
            reg.MeshEnemyRanged = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_Enemy_Ranged.fbx", "SM_Enemy_Ranged");
            reg.MeshEnemyVampire = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_Enemy_Vampire.fbx", "SM_Enemy_Vampire");
            reg.MeshGoldIngot = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_GoldIngot.fbx", "SM_GoldIngot");
            reg.MeshPlasmaSawOrb = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_PlasmaSawOrb.fbx", "SM_PlasmaSawOrb");
            reg.MeshProjectile = MustLoadMeshFromFbx("Assets/Art/Generated/Models/SM_Projectile.fbx", "SM_Projectile");

            UnityEngine.Object[] debrisSub = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Generated/Models/SM_Debris_VoronoiPack.fbx");
            reg.MeshesDebrisChunks = debrisSub
                .OfType<Mesh>()
                .OrderBy(m => m.name, StringComparer.Ordinal)
                .ToArray();

            if (reg.MeshesDebrisChunks.Length == 0)
            {
                throw new InvalidOperationException("Zero Voronoi debris meshes found in SM_Debris_VoronoiPack.fbx");
            }
        }

        private static void BuildAllMaterials(CrusherAssetRegistry reg)
        {
            Shader shLit = MustLoad<Shader>("Assets/Shaders/AnisoMetalLit.shader");
            Shader shInst = MustLoad<Shader>("Assets/Shaders/AnisoMetalInstanced.shader");
            Shader shFloor = MustLoad<Shader>("Assets/Shaders/ArenaFloorGrid.shader");
            Shader shVortex = MustLoad<Shader>("Assets/Shaders/BlackHoleVortex.shader");
            Shader shBeam = MustLoad<Shader>("Assets/Shaders/VolumetricDeathRay.shader");
            Shader shUi = MustLoad<Shader>("Assets/Shaders/ProceduralSDFUI.shader");

            // 1. Arena Floor (V2 cast-steel foundry plates with recessed furnace pit, rivets & specular oil slicks)
            reg.MatArenaFloor = SaveMaterial("M_ArenaFloor", shFloor, m =>
            {
                m.SetColor("_CenterColor", new Color(0.60f, 0.65f, 0.72f, 1f));
                m.SetColor("_EdgeColor", new Color(0.42f, 0.47f, 0.54f, 1f));
                m.SetColor("_GridColor", new Color(0.32f, 0.37f, 0.44f, 1f));
                m.SetColor("_MajorRingColor", new Color(0.26f, 0.31f, 0.38f, 1f));
                m.SetColor("_RangeColor", new Color(1.00f, 0.68f, 0.04f, 1f));
                m.SetColor("_OrbRingColor", new Color(0.08f, 0.68f, 0.98f, 1f));
                m.SetColor("_GoldenFloorGlow", new Color(2.40f, 1.35f, 0.18f, 1f));
                m.SetFloat("_TowerRange", GameIdsAndScale.RANGE_0_WORLD);
                m.SetFloat("_GridScale", GameIdsAndScale.WORLD_SCALE);
                m.SetFloat("_NoiseStrength", 0.055f);
                m.SetFloat("_VignetteRadius", 19.5f);
            });

            // 1b. Static Foundry Hall Environment (scrap bunkers, armored cable conduits, furnace pit grate & floodlight masts)
            reg.MatFoundryEnvLit = SaveMaterial("Mat_FoundryEnvLit", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.16f, 0.19f, 0.25f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.22f, 0.26f, 0.33f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.88f, 0.62f, 0.14f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(1.65f, 0.58f, 0.12f, 1f));
                m.SetFloat("_Metallic", 0.88f);
                m.SetFloat("_Roughness", 0.34f);
                m.SetFloat("_Anisotropy", 0.45f);
                m.SetFloat("_EmissionBoost", 0.85f);
                m.SetFloat("_GoldenMelt", 0.08f);
                m.SetFloat("_RimOutlineStrength", 0.80f);
                m.SetFloat("_OutlineWidth", 0.018f);
            });

            // 2. Shredder Core Chassis & 4 Visual Grades
            reg.MatCoreChassis = SaveMaterial("M_Core_Chassis", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.18f, 0.22f, 0.28f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.11f, 0.14f, 0.19f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.72f, 0.77f, 0.84f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(0.22f, 0.95f, 1.90f, 1f));
                m.SetFloat("_Metallic", 0.88f);
                m.SetFloat("_Roughness", 0.30f);
                m.SetFloat("_Anisotropy", 0.68f);
                m.SetFloat("_GoldenMelt", 1.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
                m.SetFloat("_OutlineWidth", 0.022f);
            });

            reg.MatCoreGrades = new Material[GameIdsAndScale.CORE_GRADE_COUNT];
            reg.MatCoreGrades[(int)CoreVisualGrade.G0_Rusty] = SaveMaterial("M_Core_G0_Rusty", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.30f, 0.24f, 0.20f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.20f, 0.16f, 0.14f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.58f, 0.60f, 0.65f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(1.35f, 0.58f, 0.12f, 1f));
                m.SetFloat("_Metallic", 0.74f);
                m.SetFloat("_Roughness", 0.48f);
                m.SetFloat("_RustAmount", 0.88f);
                m.SetFloat("_GoldenMelt", 1.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
                m.SetFloat("_OutlineWidth", 0.022f);
            });

            reg.MatCoreGrades[(int)CoreVisualGrade.G1_SteelCarbide] = SaveMaterial("M_Core_G1_SteelCarbide", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.26f, 0.32f, 0.40f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.14f, 0.19f, 0.26f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.84f, 0.89f, 0.95f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(0.25f, 1.25f, 2.40f, 1f));
                m.SetFloat("_Metallic", 0.90f);
                m.SetFloat("_Roughness", 0.27f);
                m.SetFloat("_Anisotropy", 0.74f);
                m.SetFloat("_GoldenMelt", 1.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
                m.SetFloat("_OutlineWidth", 0.022f);
            });

            reg.MatCoreGrades[(int)CoreVisualGrade.G2_TitaniumTwin] = SaveMaterial("M_Core_G2_TitaniumTwin", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.30f, 0.38f, 0.50f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.12f, 0.24f, 0.42f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.88f, 0.94f, 1.00f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(0.35f, 1.85f, 3.30f, 1f));
                m.SetFloat("_Metallic", 0.92f);
                m.SetFloat("_Roughness", 0.23f);
                m.SetFloat("_Anisotropy", 0.80f);
                m.SetFloat("_GoldenMelt", 1.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
                m.SetFloat("_OutlineWidth", 0.022f);
            });

            reg.MatCoreGrades[(int)CoreVisualGrade.G3_DiamondReactor] = SaveMaterial("M_Core_G3_DiamondReactor", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.18f, 0.21f, 0.28f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.92f, 0.68f, 0.14f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.92f, 0.96f, 1.00f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.60f, 1.80f, 0.40f, 1f));
                m.SetFloat("_Metallic", 0.94f);
                m.SetFloat("_Roughness", 0.20f);
                m.SetFloat("_Anisotropy", 0.86f);
                m.SetFloat("_EmissionBoost", 1.75f);
                m.SetFloat("_GoldenMelt", 1.0f);
                m.SetFloat("_RimOutlineStrength", 0.84f);
                m.SetFloat("_OutlineWidth", 0.022f);
            });

            // 3. Boss & Satellites
            reg.MatBossAtm = SaveMaterial("M_Boss_ATM", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.16f, 0.20f, 0.27f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.92f, 0.68f, 0.14f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.76f, 0.80f, 0.88f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.90f, 0.36f, 0.16f, 1f));
                m.SetFloat("_Metallic", 0.90f);
                m.SetFloat("_Roughness", 0.26f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.85f);
                m.SetFloat("_OutlineWidth", 0.024f);
            });

            reg.MatElectromagnetBh = SaveMaterial("M_Electromagnet_BH", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.16f, 0.18f, 0.24f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.78f, 0.42f, 0.14f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.80f, 0.84f, 0.92f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(1.75f, 0.60f, 3.80f, 1f));
                m.SetFloat("_EmissionBoost", 2.0f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
                m.SetFloat("_OutlineWidth", 0.020f);
            });

            reg.MatRailEmitterDr = SaveMaterial("M_RailEmitter_DR", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.20f, 0.24f, 0.30f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.12f, 0.15f, 0.20f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.86f, 0.90f, 0.96f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(3.40f, 0.80f, 0.18f, 1f));
                m.SetFloat("_EmissionBoost", 2.1f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
                m.SetFloat("_OutlineWidth", 0.020f);
            });

            reg.MatGoldenBot = SaveMaterial("M_GoldenBot", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.95f, 0.70f, 0.14f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.18f, 0.21f, 0.28f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(1.00f, 0.90f, 0.55f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.80f, 1.85f, 0.30f, 1f));
                m.SetFloat("_Metallic", 0.94f);
                m.SetFloat("_Roughness", 0.20f);
                m.SetFloat("_EmissionBoost", 1.6f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.84f);
                m.SetFloat("_OutlineWidth", 0.020f);
            });

            reg.MatGemDrone = SaveMaterial("M_GemDrone", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.58f, 0.15f, 0.88f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.16f, 0.09f, 0.26f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.92f, 0.70f, 1.00f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(3.20f, 0.65f, 4.40f, 1f));
                m.SetFloat("_Metallic", 0.86f);
                m.SetFloat("_Roughness", 0.18f);
                m.SetFloat("_EmissionBoost", 2.1f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.85f);
                m.SetFloat("_OutlineWidth", 0.020f);
            });

            reg.MatOrbSaw = SaveMaterial("M_OrbSaw", shLit, m =>
            {
                m.SetColor("_BaseColor", new Color(0.20f, 0.25f, 0.32f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.11f, 0.14f, 0.20f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.75f, 0.96f, 1.00f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(0.45f, 2.50f, 3.90f, 1f));
                m.SetFloat("_Metallic", 0.90f);
                m.SetFloat("_Roughness", 0.22f);
                m.SetFloat("_EmissionBoost", 2.0f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.84f);
                m.SetFloat("_OutlineWidth", 0.018f);
            });

            // 4. GPU-Instanced Enemy, Projectile & VFX Materials (enableInstancing = true)
            reg.MatEnemiesInstanced = new Material[GameIdsAndScale.ENEMY_TYPE_COUNT];
            reg.MatEnemiesInstanced[(int)EnemyType.Basic] = SaveMaterial("M_Enemy_Basic", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetColor("_BaseColor", new Color(0.12f, 0.46f, 0.62f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.09f, 0.15f, 0.22f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.75f, 0.88f, 0.95f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(0.30f, 1.70f, 2.60f, 1f));
                m.SetFloat("_RimOutlineStrength", 0.82f);
            });

            reg.MatEnemiesInstanced[(int)EnemyType.Fast] = SaveMaterial("M_Enemy_Fast", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetColor("_BaseColor", new Color(0.92f, 0.62f, 0.08f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.15f, 0.16f, 0.20f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.95f, 0.90f, 0.72f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.70f, 1.15f, 0.15f, 1f));
                m.SetFloat("_RollFactor", 1.0f);
                m.SetFloat("_RimOutlineStrength", 0.82f);
            });

            reg.MatEnemiesInstanced[(int)EnemyType.Tank] = SaveMaterial("M_Enemy_Tank", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetColor("_BaseColor", new Color(0.23f, 0.27f, 0.34f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.76f, 0.56f, 0.18f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.80f, 0.84f, 0.90f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.30f, 0.35f, 0.18f, 1f));
                m.SetFloat("_Metallic", 0.90f);
                m.SetFloat("_Roughness", 0.28f);
                m.SetFloat("_RimOutlineStrength", 0.85f);
            });

            reg.MatEnemiesInstanced[(int)EnemyType.Ranged] = SaveMaterial("M_Enemy_Ranged", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetColor("_BaseColor", new Color(0.60f, 0.14f, 0.44f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.15f, 0.12f, 0.20f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.92f, 0.80f, 0.96f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.80f, 0.45f, 2.30f, 1f));
                m.SetFloat("_RimOutlineStrength", 0.82f);
            });

            reg.MatEnemiesInstanced[(int)EnemyType.Vampire] = SaveMaterial("M_Enemy_Vampire", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetColor("_BaseColor", new Color(0.58f, 0.08f, 0.18f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.12f, 0.08f, 0.12f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.95f, 0.72f, 0.78f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(3.40f, 0.25f, 0.55f, 1f));
                m.SetFloat("_EmissionBoost", 1.8f);
                m.SetFloat("_RimOutlineStrength", 0.85f);
            });

            reg.MatProjectilesInstanced = SaveMaterial("M_Projectiles_Instanced", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetFloat("_UseInstColor", 1.0f);
                m.SetFloat("_UnlitSparkMode", 1.0f);
                m.SetFloat("_EmissionBoost", 1.35f);
            });

            reg.MatDebrisInstanced = SaveMaterial("M_Debris_Instanced", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetFloat("_UseInstColor", 1.0f);
                m.SetFloat("_UnlitSparkMode", 0.0f);
                m.SetColor("_ZoneArmorColor", new Color(0.16f, 0.18f, 0.22f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(0.84f, 0.88f, 0.92f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.90f, 1.05f, 0.18f, 1f));
                m.SetFloat("_EmissionBoost", 1.65f);
                m.SetFloat("_GoldenMelt", 0.0f);
            });

            reg.MatWeldingSparksInstanced = SaveMaterial("M_WeldingSparks_Instanced", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetFloat("_UseInstColor", 1.0f);
                m.SetFloat("_UnlitSparkMode", 1.0f);
                m.SetFloat("_EmissionBoost", 1.55f);
            });

            reg.MatGoldIngotInstanced = SaveMaterial("M_GoldIngot_Instanced", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetColor("_BaseColor", new Color(0.98f, 0.72f, 0.12f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.76f, 0.48f, 0.06f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(1.00f, 0.92f, 0.58f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(1.80f, 1.25f, 0.22f, 1f));
                m.SetFloat("_Metallic", 0.95f);
                m.SetFloat("_Roughness", 0.20f);
                m.SetFloat("_EmissionBoost", 1.15f);
                m.SetFloat("_GoldenMelt", 0.0f);
            });

            reg.MatBrassShellInstanced = SaveMaterial("M_BrassShell_Instanced", shInst, m =>
            {
                m.enableInstancing = true;
                m.SetFloat("_UseInstColor", 0.0f);
                m.SetFloat("_UnlitSparkMode", 0.0f);
                m.SetColor("_BaseColor", new Color(0.95f, 0.71f, 0.20f, 1f));
                m.SetColor("_ZoneArmorColor", new Color(0.74f, 0.49f, 0.12f, 1f));
                m.SetColor("_ZoneTeethColor", new Color(1.00f, 0.90f, 0.54f, 1f));
                m.SetColor("_ZoneCoilColor", new Color(2.50f, 1.15f, 0.22f, 1f));
                m.SetFloat("_Metallic", 0.95f);
                m.SetFloat("_Roughness", 0.20f);
                m.SetFloat("_Anisotropy", 0.75f);
                m.SetFloat("_EmissionBoost", 1.25f);
                m.SetFloat("_GoldenMelt", 0.0f);
                m.SetFloat("_RimOutlineStrength", 0.78f);
            });

            // 5. Vortex, Supersonic Shockwaves, Beams & Procedural SDF UI
            reg.MatBlackHoleVortex = SaveMaterial("M_BlackHole_Vortex", shVortex, m =>
            {
                m.SetColor("_InnerRimColor", new Color(1.60f, 3.80f, 6.20f, 1f));
                m.SetColor("_SwirlColor", new Color(1.55f, 0.55f, 3.80f, 1f));
                m.SetColor("_CoreColor", new Color(0.01f, 0.01f, 0.03f, 0.96f));
                m.SetFloat("_HorizonRadius", 0.19f);
                m.SetFloat("_FieldZoneMode", 0.0f);
            });

            reg.MatGoldenBotField = SaveMaterial("M_GoldenBot_Field", shVortex, m =>
            {
                m.SetColor("_InnerRimColor", new Color(3.40f, 2.40f, 0.45f, 1f));
                m.SetColor("_SwirlColor", new Color(2.20f, 1.45f, 0.15f, 1f));
                m.SetFloat("_SpinSpeed", 4.5f);
                m.SetFloat("_FieldZoneMode", 1.0f);
            });

            reg.MatShockwaveRing = SaveMaterial("M_Shockwave_Ring", shVortex, m =>
            {
                m.SetFloat("_FieldZoneMode", 2.0f);
                m.SetFloat("_HorizonRadius", 0.50f);
                m.SetColor("_InnerRimColor", new Color(1.80f, 4.20f, 6.20f, 1f));
                m.SetColor("_SwirlColor", new Color(0.65f, 1.80f, 4.20f, 1f));
                m.SetFloat("_Intensity", 1.25f);
            });

            reg.MatBeamDeathRay = SaveMaterial("M_Beam_DeathRay", shBeam, m =>
            {
                m.SetFloat("_BeamMode", 0.0f);
                m.SetColor("_CoreColor", new Color(6.20f, 5.50f, 4.50f, 1f));
                m.SetColor("_CoronaColor", new Color(3.80f, 0.65f, 0.15f, 1f));
                m.SetFloat("_CoreWidth", 0.24f);
            });

            reg.MatBeamVampireSiphon = SaveMaterial("M_Beam_VampireSiphon", shBeam, m =>
            {
                m.SetFloat("_BeamMode", 1.0f);
                m.SetColor("_CoreColor", new Color(4.20f, 0.55f, 1.25f, 1f));
                m.SetColor("_CoronaColor", new Color(2.60f, 0.12f, 0.45f, 1f));
                m.SetColor("_CounterRingColor", new Color(4.60f, 1.85f, 0.12f, 1f));
                m.SetFloat("_CoreWidth", 0.22f);
            });

            reg.MatBeamChainZap = SaveMaterial("M_Beam_ChainZap", shBeam, m =>
            {
                m.SetFloat("_BeamMode", 2.0f);
                m.SetColor("_CoreColor", new Color(4.80f, 5.60f, 6.80f, 1f));
                m.SetColor("_CoronaColor", new Color(0.65f, 2.40f, 4.80f, 1f));
                m.SetFloat("_CoreWidth", 0.26f);
                m.SetFloat("_NoiseAmp", 0.52f);
            });

            reg.MatSdfUi = SaveMaterial("M_ProceduralSDFUI", shUi, m =>
            {
                m.SetColor("_BgTopColor", new Color(0.15f, 0.19f, 0.25f, 0.96f));
                m.SetColor("_BgBottomColor", new Color(0.09f, 0.11f, 0.15f, 0.98f));
                m.SetColor("_BorderColor", new Color(0.32f, 0.42f, 0.54f, 1.0f));
                m.SetColor("_AccentColor", new Color(1.00f, 0.68f, 0.12f, 1.0f));
            });
        }

        private static Material SaveMaterial(string matName, Shader shader, Action<Material> configure)
        {
            string path = MaterialsDir + "/" + matName + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = matName };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildAllAnimatorControllers(CrusherAssetRegistry reg)
        {
            // 1. Shredder Core Controller (BaseSpin + Additive Action layer)
            string coreCtrlPath = AnimationsDir + "/AC_ShredderCore.controller";
            DeleteIfExists(coreCtrlPath);
            AnimatorController acCore = AnimatorController.CreateAnimatorControllerAtPath(coreCtrlPath);
            AnimatorStateMachine smBase = acCore.layers[0].stateMachine;
            AnimatorState stIdle = smBase.AddState("IdleSpin");
            stIdle.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_ShredderCore.fbx", "AN_Core_IdleSpin");
            smBase.defaultState = stIdle;

            AnimatorState stOver = smBase.AddState("OverdriveSpin");
            stOver.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_ShredderCore.fbx", "AN_Core_OverdriveSpin");

            AnimatorState stDeath = smBase.AddState("OverloadDeath");
            stDeath.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_ShredderCore.fbx", "AN_Core_OverloadDeath");

            var addLayer = new AnimatorControllerLayer
            {
                name = "ActionAdditive",
                defaultWeight = 1.0f,
                blendingMode = AnimatorLayerBlendingMode.Additive,
                stateMachine = new AnimatorStateMachine { name = "ActionAdditive", hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(addLayer.stateMachine, acCore);
            acCore.AddLayer(addLayer);

            AnimatorState stNone = addLayer.stateMachine.AddState("None");
            addLayer.stateMachine.defaultState = stNone;

            AddOneShotAdditiveState(addLayer.stateMachine, stNone, "FireL",
                MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_ShredderCore.fbx", "AN_Core_FireL"));
            AddOneShotAdditiveState(addLayer.stateMachine, stNone, "FireR",
                MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_ShredderCore.fbx", "AN_Core_FireR"));
            AddOneShotAdditiveState(addLayer.stateMachine, stNone, "CrunchImpact",
                MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_ShredderCore.fbx", "AN_Core_CrunchImpact"));
            EditorUtility.SetDirty(acCore);
            reg.AnimCore = acCore;

            // 2. Boss ATM Controller
            string bossCtrlPath = AnimationsDir + "/AC_Boss_ATM.controller";
            DeleteIfExists(bossCtrlPath);
            AnimatorController acBoss = AnimatorController.CreateAnimatorControllerAtPath(bossCtrlPath);
            AnimatorStateMachine smBoss = acBoss.layers[0].stateMachine;
            AnimatorState stRoll = smBoss.AddState("Roll");
            stRoll.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_Enemy_Boss_ATM.fbx", "AN_Boss_Roll");
            smBoss.defaultState = stRoll;
            AnimatorState stRam = smBoss.AddState("Ram");
            stRam.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_Enemy_Boss_ATM.fbx", "AN_Boss_Ram");
            EditorUtility.SetDirty(acBoss);
            reg.AnimBossAtm = acBoss;

            // 3. Electromagnet BH Controller
            string bhCtrlPath = AnimationsDir + "/AC_Electromagnet_BH.controller";
            DeleteIfExists(bhCtrlPath);
            AnimatorController acBh = AnimatorController.CreateAnimatorControllerAtPath(bhCtrlPath);
            AnimatorStateMachine smBh = acBh.layers[0].stateMachine;
            AnimatorState stDeploy = smBh.AddState("Deploy");
            stDeploy.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_Electromagnet_BH.fbx", "AN_BH_Deploy");
            AnimatorState stPulse = smBh.AddState("Pulse");
            stPulse.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_Electromagnet_BH.fbx", "AN_BH_Pulse");
            smBh.defaultState = stDeploy;
            AnimatorStateTransition trBh = stDeploy.AddTransition(stPulse);
            trBh.hasExitTime = true;
            trBh.exitTime = 0.98f;
            trBh.duration = 0.10f;
            EditorUtility.SetDirty(acBh);
            reg.AnimElectromagnetBh = acBh;

            // 4. Rail Emitter DR Controller
            string drCtrlPath = AnimationsDir + "/AC_RailEmitter_DR.controller";
            DeleteIfExists(drCtrlPath);
            AnimatorController acDr = AnimatorController.CreateAnimatorControllerAtPath(drCtrlPath);
            AnimatorStateMachine smDr = acDr.layers[0].stateMachine;
            AnimatorState stDrIdle = smDr.AddState("Idle");
            smDr.defaultState = stDrIdle;
            AddOneShotAdditiveState(smDr, stDrIdle, "ChargeFire",
                MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_RailEmitter_DR.fbx", "AN_DR_ChargeFire"));
            EditorUtility.SetDirty(acDr);
            reg.AnimRailEmitterDr = acDr;

            // 5. Golden Bot Controller
            string botCtrlPath = AnimationsDir + "/AC_GoldenBot.controller";
            DeleteIfExists(botCtrlPath);
            AnimatorController acBot = AnimatorController.CreateAnimatorControllerAtPath(botCtrlPath);
            AnimatorState stHover = acBot.layers[0].stateMachine.AddState("Hover");
            stHover.motion = MustLoadClipFromFbx("Assets/Art/Generated/Models/SM_GoldenBot.fbx", "AN_Bot_Hover");
            acBot.layers[0].stateMachine.defaultState = stHover;
            EditorUtility.SetDirty(acBot);
            reg.AnimGoldenBot = acBot;
        }

        private static void AddOneShotAdditiveState(AnimatorStateMachine sm, AnimatorState returnState, string name, AnimationClip clip)
        {
            AnimatorState st = sm.AddState(name);
            st.motion = clip;
            AnimatorStateTransition tr = st.AddTransition(returnState);
            tr.hasExitTime = true;
            tr.exitTime = 0.98f;
            tr.duration = 0.05f;
        }

        private static void PopulateAudioClips(CrusherAssetRegistry reg)
        {
            reg.ClipsCrunch = new[]
            {
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_crunch_01.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_crunch_02.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_crunch_03.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_crunch_04.wav")
            };
            reg.ClipBossShatter = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_boss_shatter.wav");
            reg.ClipRotorHumLoop = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_rotor_hum_loop.wav");
            reg.ClipsOrbSaw = new[]
            {
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_orb_saw_01.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_orb_saw_02.wav")
            };
            reg.ClipPneumoShot = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_pneumo_shot.wav");
            reg.ClipBlackHoleLoop = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_blackhole_loop.wav");
            reg.ClipGoldenStart = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_golden_start.wav");
            reg.ClipsIngotDing = new[]
            {
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ingot_ding_01.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ingot_ding_02.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ingot_ding_03.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ingot_ding_04.wav"),
                MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ingot_ding_05.wav")
            };
            reg.ClipChainZap = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_chain_zap.wav");
            reg.ClipPlasmaHit = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_plasma_hit.wav");
            reg.ClipUiTap = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ui_tap.wav");
            reg.ClipUiUpgrade = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ui_upgrade.wav");
            reg.ClipUiUnlock = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_ui_unlock.wav");
            reg.ClipOverloadDeath = MustLoad<AudioClip>("Assets/Audio/Generated/sfx_overload_death.wav");
        }

        private static VolumeProfile BuildGlobalVolumeProfile()
        {
            DeleteIfExists(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var tonemapping = profile.Add<Tonemapping>(overrides: true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(overrides: true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Mesh BuildArenaFloorMeshAsset()
        {
            DeleteIfExists(FloorMeshAssetPath);
            // 96m x 96m quad covers both 9:20 Portrait and 16:9 / 21:9 Landscape WebGL up to 1.22x zoom out with zero horizon clipping
            const float halfExtent = 48.0f;
            var mesh = new Mesh { name = "ArenaFloorQuad96m" };
            mesh.vertices = new[]
            {
                new Vector3(-halfExtent, 0f, -halfExtent),
                new Vector3(-halfExtent, 0f,  halfExtent),
                new Vector3( halfExtent, 0f,  halfExtent),
                new Vector3( halfExtent, 0f, -halfExtent)
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(halfExtent * 2f, 2f, halfExtent * 2f));
            AssetDatabase.CreateAsset(mesh, FloorMeshAssetPath);
            return mesh;
        }

        private static Light BuildScenePointLight(string objName, Transform parent, Vector3 pos, Color col, float range, float intensity, bool enabledInit)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var lt = go.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = col;
            lt.range = range;
            lt.intensity = intensity;
            lt.shadows = LightShadows.None;
            lt.enabled = enabledInit;
            go.AddComponent<UniversalAdditionalLightData>();
            return lt;
        }

        private static void BuildMainScene(CrusherAssetRegistry registry, VolumeProfile volumeProfile, Mesh floorMesh)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.62f, 0.70f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.47f, 0.54f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.28f, 0.24f, 1f);
            RenderSettings.fog = false;

            // 1. Industrial Foundry Cast-Steel Floor + Optional Outer Foundry Environment (SM_ArenaFoundryEnv)
            var floorGo = new GameObject("ArenaFloor");
            var floorMf = floorGo.AddComponent<MeshFilter>();
            floorMf.sharedMesh = floorMesh;
            var floorMr = floorGo.AddComponent<MeshRenderer>();
            floorMr.sharedMaterial = registry.MatArenaFloor;
            floorMr.shadowCastingMode = ShadowCastingMode.Off;
            floorMr.receiveShadows = true;

            if (registry.PrefabArenaFoundryEnv != null)
            {
                var foundryGo = (GameObject)PrefabUtility.InstantiatePrefab(registry.PrefabArenaFoundryEnv);
                if (foundryGo != null)
                {
                    foundryGo.name = "ArenaFoundryEnv";
                    foundryGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    foundryGo.transform.localScale = Vector3.one;

                    MeshFilter[] envMfs = foundryGo.GetComponentsInChildren<MeshFilter>(true);
                    for (int i = 0; i < envMfs.Length; i++)
                    {
                        Mesh m = envMfs[i].sharedMesh;
                        if (m == null) continue;
                        Vector3 sz = m.bounds.size;
                        // Guarantee Y-up world orientation (30.5m X * 5.47m Y * 31.3m Z) regardless of FBX node pre-rotation
                        if (sz.y < sz.z * 0.5f && sz.x > 15f && sz.z > 15f)
                        {
                            envMfs[i].transform.rotation = Quaternion.identity;
                        }
                        else if (sz.z < sz.y * 0.5f && sz.x > 15f && sz.y > 15f)
                        {
                            envMfs[i].transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                        }
                    }

                    Renderer[] envRenderers = foundryGo.GetComponentsInChildren<Renderer>(true);
                    Material envMat = registry.MatFoundryEnvLit != null ? registry.MatFoundryEnvLit : registry.MatCoreChassis;
                    for (int i = 0; i < envRenderers.Length; i++)
                    {
                        envRenderers[i].sharedMaterial = envMat;
                        envRenderers[i].shadowCastingMode = ShadowCastingMode.On;
                        envRenderers[i].receiveShadows = true;
                        Debug.Log($"[SceneBuilder] ArenaFoundryEnv[{i}] '{envRenderers[i].name}' worldBounds={envRenderers[i].bounds.size} center={envRenderers[i].bounds.center}");
                    }
                }
            }

            // 2. Studio Directional Key (2048 Soft Shadows) & Rim Lights
            var keyLightGo = new GameObject("DirectionalLight_Key");
            keyLightGo.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
            var keyLight = keyLightGo.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1.00f, 0.98f, 0.94f, 1f);
            keyLight.intensity = 1.06f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.85f;
            keyLightGo.AddComponent<UniversalAdditionalLightData>();

            var rimLightGo = new GameObject("DirectionalLight_Rim");
            rimLightGo.transform.rotation = Quaternion.Euler(34f, 148f, 0f);
            var rimLight = rimLightGo.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = new Color(0.55f, 0.78f, 1.00f, 1f);
            rimLight.intensity = 0.58f;
            rimLight.shadows = LightShadows.None;
            rimLightGo.AddComponent<UniversalAdditionalLightData>();

            // 3. Global Post-Processing Volume (ACES + HDR Bloom)
            var volGo = new GameObject("GlobalVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = volumeProfile;

            // 4. Adaptive V2 Camera Rig (9:20 Mobile Portrait + 16:9 WebGL Landscape + Foundry/Action Point Lights)
            var camRigGo = new GameObject("CameraRig");
            var camRig = camRigGo.AddComponent<CameraRigTrauma>();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(camRigGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.41f, 0.48f, 1f);
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 115.0f;
            cam.fieldOfView = CameraRigTrauma.VERTICAL_FOV_DEGREES;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.AddComponent<AudioListener>();

            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;

            Light pitLight = BuildScenePointLight("FoundryPit_PointLight", camRigGo.transform, new Vector3(0f, 1.25f, 0f), new Color(1.0f, 0.46f, 0.12f, 1f), 8.5f, 1.15f, true);
            Light muzzleLight = BuildScenePointLight("Dynamic_MuzzleFlashLight", camRigGo.transform, new Vector3(0f, 1.15f, 0.9f), new Color(1.0f, 0.74f, 0.30f, 1f), 7.5f, 0f, false);
            Light fieldLight = BuildScenePointLight("Dynamic_FieldOrBeamLight", camRigGo.transform, new Vector3(0f, 1.35f, 0f), new Color(0.55f, 0.42f, 1.0f, 1f), 11.5f, 0f, false);
            Light expLight = BuildScenePointLight("Dynamic_ExplosionLight", camRigGo.transform, new Vector3(0f, 1.50f, 0f), new Color(1.0f, 0.68f, 0.24f, 1f), 14.0f, 0f, false);

            camRig.BindSceneObjectsV2(cam, keyLight, rimLight, pitLight, muzzleLight, fieldLight, expLight);

            // 5. uGUI EventSystem
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();

            // 6. Runtime Render, VFX & Audio Systems
            var worldRoot = new GameObject("WorldSystems");

            var coreGo = new GameObject("CoreAnimatorView");
            coreGo.transform.SetParent(worldRoot.transform, false);
            var coreView = coreGo.AddComponent<CoreAnimatorView>();

            var enemyGo = new GameObject("EnemyRenderSystem");
            enemyGo.transform.SetParent(worldRoot.transform, false);
            var enemySys = enemyGo.AddComponent<EnemyRenderSystem>();

            var debrisGo = new GameObject("DebrisSimulationSystem");
            debrisGo.transform.SetParent(worldRoot.transform, false);
            var debrisSys = debrisGo.AddComponent<DebrisSimulationSystem>();

            var sparksGo = new GameObject("WeldingSparksSystem");
            sparksGo.transform.SetParent(worldRoot.transform, false);
            var sparksSys = sparksGo.AddComponent<WeldingSparksSystem>();

            var ingotGo = new GameObject("GoldIngotFlowSystem");
            ingotGo.transform.SetParent(worldRoot.transform, false);
            var ingotSys = ingotGo.AddComponent<GoldIngotFlowSystem>();

            var sfxGo = new GameObject("ProceduralSfxPlayer");
            sfxGo.transform.SetParent(worldRoot.transform, false);
            var sfxPlayer = sfxGo.AddComponent<ProceduralSfxPlayer>();

            // 7. Procedural UI Root
            var uiGo = new GameObject("CrusherUiRoot");
            var uiRoot = uiGo.AddComponent<CrusherUiRoot>();

            // 8. Root Bootstrap Coordinator
            var bootGo = new GameObject("Bootstrap");
            var bootstrap = bootGo.AddComponent<Bootstrap>();
            bootstrap.BindSceneReferences(
                registry,
                camRig,
                floorMr,
                coreView,
                enemySys,
                debrisSys,
                sparksSys,
                ingotSys,
                sfxPlayer,
                uiRoot);

            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScenePath, true)
            };
        }

        private static void DeleteIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static T MustLoad<T>(string assetPath) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new FileNotFoundException($"[SceneBuilder] Required asset missing at '{assetPath}' ({typeof(T).Name}).");
            }
            return asset;
        }

        private static Mesh MustLoadMeshFromFbx(string fbxPath, string meshName)
        {
            UnityEngine.Object[] subs = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            Mesh found = subs.OfType<Mesh>().FirstOrDefault(m => string.Equals(m.name, meshName, StringComparison.Ordinal))
                         ?? subs.OfType<Mesh>().FirstOrDefault();
            if (found == null)
            {
                throw new InvalidOperationException($"[SceneBuilder] Mesh '{meshName}' not found inside '{fbxPath}'.");
            }
            return found;
        }

        private static AnimationClip MustLoadClipFromFbx(string fbxPath, string clipName)
        {
            UnityEngine.Object[] subs = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            AnimationClip clip = subs.OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal) &&
                                     string.Equals(c.name, clipName, StringComparison.Ordinal));
            if (clip == null)
            {
                throw new InvalidOperationException($"[SceneBuilder] AnimationClip '{clipName}' not found inside '{fbxPath}'.");
            }
            return clip;
        }
    }
}
