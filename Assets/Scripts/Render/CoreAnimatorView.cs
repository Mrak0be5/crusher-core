using System;
using Crusher.Audio;
using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Drives the central Magnetic Rotor-Shredder Core (grades G0..G3, Molten Core skin, barrel recoil, turret yaw),
    /// orbiting Plasma Saw Orbs, Electromagnet Black Hole vortex, Rail Emitter Death Ray, Vampire siphon beams
    /// (+ Magnetic Garlic counter-rings), Chain Lightning arcs, Golden Bot field, Gem Drone, and Arena Floor rings.
    /// </summary>
    public sealed class CoreAnimatorView : MonoBehaviour
    {
        private const int MAX_ORB_VISUALS = CombatSim.ORB_CAPACITY;
        private const int MAX_SHOCKWAVES = 8;
        private const int MAX_RIBBON_SEGMENTS = 40;
        private const int MAX_RIBBON_VERTS = MAX_RIBBON_SEGMENTS * 4;
        private const int MAX_RIBBON_INDICES = MAX_RIBBON_SEGMENTS * 6;

        public static float LatestAimYawRad { get; private set; }

        private static readonly int PropGlobalGolden = Shader.PropertyToID("_GlobalGoldenOverdrive");
        private static readonly int PropGlobalHeat = Shader.PropertyToID("_GlobalRotorHeat");
        private static readonly int PropTowerRange = Shader.PropertyToID("_TowerRange");
        private static readonly int PropOrbTrackActive = Shader.PropertyToID("_OrbTrackActive");
        private static readonly int PropRustAmount = Shader.PropertyToID("_RustAmount");
        private static readonly int PropMoltenSkin = Shader.PropertyToID("_MoltenSkin");
        private static readonly int PropHeatGlow = Shader.PropertyToID("_HeatGlow");
        private static readonly int PropHitFlash = Shader.PropertyToID("_HitFlash");
        private static readonly int PropIntensity = Shader.PropertyToID("_Intensity");
        private static readonly int PropHorizonRadius = Shader.PropertyToID("_HorizonRadius");
        private static readonly int PropInnerRimColor = Shader.PropertyToID("_InnerRimColor");
        private static readonly int PropSwirlColor = Shader.PropertyToID("_SwirlColor");
        private static readonly int PropGarlicIntensity = Shader.PropertyToID("_GarlicIntensity");
        private static readonly int PropFade = Shader.PropertyToID("_Fade");

        private static readonly int HashIdleSpin = Animator.StringToHash("IdleSpin");
        private static readonly int HashOverdriveSpin = Animator.StringToHash("OverdriveSpin");
        private static readonly int HashOverloadDeath = Animator.StringToHash("OverloadDeath");
        private static readonly int HashFireL = Animator.StringToHash("FireL");
        private static readonly int HashFireR = Animator.StringToHash("FireR");
        private static readonly int HashCrunch = Animator.StringToHash("CrunchImpact");

        [SerializeField] private CrusherAssetRegistry _registry;
        [SerializeField] private MeshRenderer _arenaFloorRenderer;

        private GameObject _coreRoot;
        private Animator _coreAnimator;
        private Transform _turretYawBone;
        private Renderer _chassisRenderer;
        private readonly GameObject[] _gradeObjects = new GameObject[GameIdsAndScale.CORE_GRADE_COUNT];
        private readonly Renderer[] _gradeRenderers = new Renderer[GameIdsAndScale.CORE_GRADE_COUNT];

        private readonly Transform[] _orbTransforms = new Transform[MAX_ORB_VISUALS];
        private readonly GameObject[] _orbObjects = new GameObject[MAX_ORB_VISUALS];

        private GameObject _bhEmitterObj;
        private Animator _bhAnimator;
        private GameObject _bhVortexQuad;
        private MeshRenderer _bhVortexRenderer;

        private GameObject _drEmitterObj;
        private Animator _drAnimator;

        private GameObject _goldenBotObj;
        private Animator _goldenBotAnimator;
        private GameObject _goldenBotFieldQuad;
        private MeshRenderer _goldenBotFieldRenderer;

        private GameObject _gemDroneObj;
        private Animator _gemDroneAnimator;
        private GameObject _foundryEnvObj;

        private struct ShockwaveState
        {
            public bool Active;
            public Vector3 Position;
            public float MaxDiameter;
            public float Age;
            public float Duration;
            public Color RimColor;
            public Color SwirlColor;
        }

        private readonly GameObject[] _shockwaveQuads = new GameObject[MAX_SHOCKWAVES];
        private readonly MeshRenderer[] _shockwaveRenderers = new MeshRenderer[MAX_SHOCKWAVES];
        private readonly ShockwaveState[] _shockwaves = new ShockwaveState[MAX_SHOCKWAVES];
        private int _shockwaveCursor;

        // Pre-allocated dynamic ribbon meshes for DeathRay, VampireSiphon, and ChainZap
        private Mesh _deathRayMesh;
        private MeshRenderer _deathRayRenderer;
        private Mesh _vampireSiphonMesh;
        private MeshRenderer _vampireSiphonRenderer;
        private Mesh _chainZapMesh;
        private MeshRenderer _chainZapRenderer;

        private readonly Vector3[] _ribVerts = new Vector3[MAX_RIBBON_VERTS];
        private readonly Vector2[] _ribUvs = new Vector2[MAX_RIBBON_VERTS];
        private readonly Color32[] _ribCols = new Color32[MAX_RIBBON_VERTS];
        private readonly int[] _ribIndices = new int[MAX_RIBBON_INDICES];

        private MaterialPropertyBlock _coreMpb;
        private MaterialPropertyBlock _floorMpb;
        private MaterialPropertyBlock _vortexMpb;
        private MaterialPropertyBlock _botFieldMpb;
        private MaterialPropertyBlock _shockwaveMpb;
        private MaterialPropertyBlock _siphonMpb;

        private CoreVisualGrade _activeGrade = (CoreVisualGrade)(-1);
        private float _currentAimYawDeg;
        private float _targetAimYawDeg;
        private float _goldenBlend;
        private float _rotorHeatBlend;
        private float _killStreakHeat;
        private float _coreHitFlash;
        private bool _fireLeftNext = true;
        private bool _wasBhActive;
        private bool _wasDrActive;
        private bool _wasGtActive;
        private double _prevCoreHp;
        private bool _initialized;

        public void Initialize(CrusherAssetRegistry registry, MeshRenderer arenaFloorRenderer)
        {
            _registry = registry;
            _arenaFloorRenderer = arenaFloorRenderer;
            if (_initialized || _registry == null) return;
            _initialized = true;

            _coreMpb = new MaterialPropertyBlock();
            _floorMpb = new MaterialPropertyBlock();
            _vortexMpb = new MaterialPropertyBlock();
            _botFieldMpb = new MaterialPropertyBlock();
            _shockwaveMpb = new MaterialPropertyBlock();
            _siphonMpb = new MaterialPropertyBlock();

            PrecomputeRibbonIndices();
            BuildCoreHierarchy();
            BuildOrbPool();
            BuildSatelliteHierarchy();
            BuildShockwavePool();
            BuildRibbonRenderers();

            Shader.SetGlobalFloat(PropGlobalGolden, 0f);
            Shader.SetGlobalFloat(PropGlobalHeat, 0f);
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(PropGlobalGolden, 0f);
            Shader.SetGlobalFloat(PropGlobalHeat, 0f);
            if (_deathRayMesh != null) DestroyImmediate(_deathRayMesh);
            if (_vampireSiphonMesh != null) DestroyImmediate(_vampireSiphonMesh);
            if (_chainZapMesh != null) DestroyImmediate(_chainZapMesh);
        }

        private void BuildCoreHierarchy()
        {
            if (_registry.PrefabShredderCore == null) return;

            _coreRoot = Instantiate(_registry.PrefabShredderCore, transform);
            _coreRoot.name = "ShredderCore_Rig";
            _coreRoot.transform.localPosition = Vector3.zero;
            _coreRoot.transform.localRotation = Quaternion.identity;
            _coreRoot.transform.localScale = Vector3.one;

            _coreAnimator = _coreRoot.GetComponent<Animator>();
            if (_coreAnimator == null) _coreAnimator = _coreRoot.AddComponent<Animator>();
            _coreAnimator.runtimeAnimatorController = _registry.AnimCore;
            _coreAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            string[] gradeNames = { "G0_Rusty", "G1_SteelCarbide", "G2_TitaniumTwin", "G3_DiamondReactor" };
            Transform[] all = _coreRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t.name == "bn_turret_yaw")
                {
                    _turretYawBone = t;
                }
                else if (t.name == "Core_Chassis")
                {
                    _chassisRenderer = t.GetComponent<Renderer>();
                    if (_chassisRenderer != null && _registry.MatCoreChassis != null)
                    {
                        _chassisRenderer.sharedMaterial = _registry.MatCoreChassis;
                        _chassisRenderer.shadowCastingMode = ShadowCastingMode.On;
                    }
                }
                else
                {
                    for (int g = 0; g < gradeNames.Length; g++)
                    {
                        if (t.name == gradeNames[g])
                        {
                            _gradeObjects[g] = t.gameObject;
                            _gradeRenderers[g] = t.GetComponent<Renderer>();
                            if (_gradeRenderers[g] != null && _registry.MatCoreGrades != null && g < _registry.MatCoreGrades.Length && _registry.MatCoreGrades[g] != null)
                            {
                                _gradeRenderers[g].sharedMaterial = _registry.MatCoreGrades[g];
                                _gradeRenderers[g].shadowCastingMode = ShadowCastingMode.On;
                            }
                        }
                    }
                }
            }

            ApplyCoreGrade(CoreVisualGrade.G0_Rusty);
        }

        private void BuildOrbPool()
        {
            for (int i = 0; i < MAX_ORB_VISUALS; i++)
            {
                var go = new GameObject("PlasmaSawOrb_" + i);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _registry.MeshPlasmaSawOrb;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _registry.MatOrbSaw;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = false;
                go.SetActive(false);
                _orbObjects[i] = go;
                _orbTransforms[i] = go.transform;
            }
        }

        private void BuildSatelliteHierarchy()
        {
            // 1. Black Hole Electromagnet + Accretion Vortex Disk
            if (_registry.PrefabElectromagnetBh != null)
            {
                _bhEmitterObj = Instantiate(_registry.PrefabElectromagnetBh, transform);
                _bhEmitterObj.name = "Electromagnet_BH";
                _bhAnimator = EnsureAnimator(_bhEmitterObj, _registry.AnimElectromagnetBh);
                AssignMaterialRecursive(_bhEmitterObj, _registry.MatElectromagnetBh);
                _bhEmitterObj.SetActive(false);
            }

            _bhVortexQuad = CreateHorizontalDiskQuad("BlackHole_VortexDisk", _registry.MatBlackHoleVortex, out _bhVortexRenderer);
            _bhVortexQuad.SetActive(false);

            // 2. Rail Emitter for CNC Death Ray
            if (_registry.PrefabRailEmitterDr != null)
            {
                _drEmitterObj = Instantiate(_registry.PrefabRailEmitterDr, transform);
                _drEmitterObj.name = "RailEmitter_DR";
                _drAnimator = EnsureAnimator(_drEmitterObj, _registry.AnimRailEmitterDr);
                AssignMaterialRecursive(_drEmitterObj, _registry.MatRailEmitterDr);
                _drEmitterObj.SetActive(false);
            }

            // 3. Golden Bot + Soft Bonus Field Disk
            if (_registry.PrefabGoldenBot != null)
            {
                _goldenBotObj = Instantiate(_registry.PrefabGoldenBot, transform);
                _goldenBotObj.name = "GoldenBot_Unit";
                _goldenBotAnimator = EnsureAnimator(_goldenBotObj, _registry.AnimGoldenBot);
                AssignMaterialRecursive(_goldenBotObj, _registry.MatGoldenBot);
                _goldenBotObj.SetActive(false);

                // 4. Gem Drone (reuses SM_GoldenBot rig with vibrant purple crystal HDR material)
                _gemDroneObj = Instantiate(_registry.PrefabGoldenBot, transform);
                _gemDroneObj.name = "GemDrone_Unit";
                _gemDroneAnimator = EnsureAnimator(_gemDroneObj, _registry.AnimGoldenBot);
                AssignMaterialRecursive(_gemDroneObj, _registry.MatGemDrone);
                _gemDroneObj.SetActive(false);
            }

            _goldenBotFieldQuad = CreateHorizontalDiskQuad("GoldenBot_FieldDisk", _registry.MatGoldenBotField, out _goldenBotFieldRenderer);
            _goldenBotFieldQuad.SetActive(false);

            // 5. Industrial Foundry Hall & Recessed Pit Static Environment (SM_ArenaFoundryEnv)
            GameObject existingEnv = GameObject.Find("ArenaFoundryEnv");
            if (existingEnv != null)
            {
                _foundryEnvObj = existingEnv;
                if (_registry.MatFoundryEnvLit != null)
                {
                    AssignMaterialRecursive(_foundryEnvObj, _registry.MatFoundryEnvLit);
                }
            }
            else if (_registry.PrefabArenaFoundryEnv != null)
            {
                _foundryEnvObj = Instantiate(_registry.PrefabArenaFoundryEnv, transform);
                _foundryEnvObj.name = "ArenaFoundryEnv";
                _foundryEnvObj.transform.localPosition = Vector3.zero;
                _foundryEnvObj.transform.localRotation = Quaternion.identity;
                _foundryEnvObj.transform.localScale = Vector3.one;
                AssignMaterialRecursive(_foundryEnvObj, _registry.MatFoundryEnvLit != null ? _registry.MatFoundryEnvLit : _registry.MatCoreChassis);
            }
        }

        private void BuildShockwavePool()
        {
            Material swMat = _registry.MatShockwaveRing != null ? _registry.MatShockwaveRing : _registry.MatGoldenBotField;
            for (int i = 0; i < MAX_SHOCKWAVES; i++)
            {
                _shockwaveQuads[i] = CreateHorizontalDiskQuad("ShockwaveRing_" + i, swMat, out _shockwaveRenderers[i]);
                _shockwaveQuads[i].SetActive(false);
            }
        }

        public void ClearShockwaves()
        {
            for (int i = 0; i < MAX_SHOCKWAVES; i++)
            {
                _shockwaves[i].Active = false;
                if (_shockwaveQuads[i] != null && _shockwaveQuads[i].activeSelf)
                {
                    _shockwaveQuads[i].SetActive(false);
                }
            }
        }

        public void SpawnShockwaveRing(Vector3 worldPos, float maxDiameter, float duration, Color hdrRim, Color hdrSwirl, float initialProgress01 = 0f)
        {
            if (!_initialized) return;
            int idx = _shockwaveCursor;
            _shockwaveCursor = (_shockwaveCursor + 1) % MAX_SHOCKWAVES;
            float dur = Mathf.Max(0.12f, duration);
            _shockwaves[idx] = new ShockwaveState
            {
                Active = true,
                Position = new Vector3(worldPos.x, 0.12f, worldPos.z),
                MaxDiameter = Mathf.Max(1.5f, maxDiameter),
                Age = Mathf.Clamp01(initialProgress01) * dur,
                Duration = dur,
                RimColor = hdrRim,
                SwirlColor = hdrSwirl
            };
        }

        private GameObject CreateHorizontalDiskQuad(string objName, Material mat, out MeshRenderer mr)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            Mesh quad = new Mesh { name = objName + "_Mesh" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f, -0.5f)
            };
            quad.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            quad.colors32 = new[]
            {
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255)
            };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateNormals();
            quad.bounds = new Bounds(Vector3.zero, new Vector3(2f, 1f, 2f));
            mf.sharedMesh = quad;

            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private void BuildRibbonRenderers()
        {
            _deathRayMesh = CreateDynamicRibbonMesh("Ribbon_DeathRay", _registry.MatBeamDeathRay, out _deathRayRenderer);
            _vampireSiphonMesh = CreateDynamicRibbonMesh("Ribbon_VampireSiphon", _registry.MatBeamVampireSiphon, out _vampireSiphonRenderer);
            _chainZapMesh = CreateDynamicRibbonMesh("Ribbon_ChainZap", _registry.MatBeamChainZap, out _chainZapRenderer);
        }

        private Mesh CreateDynamicRibbonMesh(string objName, Material mat, out MeshRenderer mr)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            var mesh = new Mesh { name = objName + "_DynamicMesh" };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;

            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            return mesh;
        }

        private void PrecomputeRibbonIndices()
        {
            for (int s = 0; s < MAX_RIBBON_SEGMENTS; s++)
            {
                int v = s * 4;
                int t = s * 6;
                _ribIndices[t + 0] = v + 0;
                _ribIndices[t + 1] = v + 1;
                _ribIndices[t + 2] = v + 2;
                _ribIndices[t + 3] = v + 0;
                _ribIndices[t + 4] = v + 2;
                _ribIndices[t + 5] = v + 3;
            }
        }

        private static Animator EnsureAnimator(GameObject root, RuntimeAnimatorController ctrl)
        {
            var anim = root.GetComponent<Animator>();
            if (anim == null) anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return anim;
        }

        private static void AssignMaterialRecursive(GameObject root, Material mat)
        {
            if (root == null || mat == null) return;
            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                rends[i].sharedMaterial = mat;
                rends[i].shadowCastingMode = ShadowCastingMode.On;
            }
        }

        private void ApplyCoreGrade(CoreVisualGrade grade)
        {
            if (_activeGrade == grade) return;
            _activeGrade = grade;
            int activeIdx = (int)grade;
            for (int i = 0; i < _gradeObjects.Length; i++)
            {
                if (_gradeObjects[i] != null)
                {
                    _gradeObjects[i].SetActive(i == activeIdx);
                }
            }
        }

        public void TickFromSim(CombatSim sim, GameSession session, Camera worldCam, float unscaledDt)
        {
            if (!_initialized || sim == null || session == null) return;

            float dt = Mathf.Max(0.0001f, unscaledDt);
            CoreVisualGrade grade = session.GetVisualCoreGrade();
            ApplyCoreGrade(grade);

            bool inRun = session.InRun;
            bool gtActive = inRun && sim.GoldenTower.Active;

            // 1. Global shader uniforms (Golden Smelter Overdrive & Dynamic Kill-Streak Rotor Heat)
            _goldenBlend = Mathf.MoveTowards(_goldenBlend, gtActive ? 1f : 0f, dt * 3.5f);
            _killStreakHeat = Mathf.Max(0f, _killStreakHeat - dt * 0.30f);

            if (inRun)
            {
                for (int k = 0; k < sim.KillEventCount; k++)
                {
                    ref KillEvent ev = ref sim.KillEvents[k];
                    _killStreakHeat = Mathf.Min(1.35f, _killStreakHeat + (ev.Type == EnemyType.Boss ? 0.65f : 0.14f));

                    Vector3 killPos = new Vector3(GameIdsAndScale.SimToWorld(ev.PosX), 0.12f, GameIdsAndScale.SimToWorld(ev.PosY));
                    if (ev.Type == EnemyType.Boss)
                    {
                        SpawnShockwaveRing(
                            killPos, 10.5f, 0.52f,
                            new Color(4.80f, 2.20f, 0.45f, 1f),
                            new Color(3.60f, 0.55f, 0.18f, 1f));
                    }
                    else if ((ev.Type == EnemyType.Tank || ev.Type == EnemyType.Vampire) && ev.KilledByOrbOrRay)
                    {
                        SpawnShockwaveRing(
                            killPos, 4.8f, 0.32f,
                            new Color(1.20f, 3.80f, 5.40f, 1f),
                            new Color(0.55f, 1.40f, 3.20f, 1f));
                    }
                }

                for (int s = 0; s < sim.SfxCount; s++)
                {
                    if (sim.SfxQueue[s] == SfxId.PlasmaHit)
                    {
                        Vector3 hitPos = FindPlasmaImpactWorldPos(sim);
                        SpawnShockwaveRing(
                            hitPos, 6.2f, 0.38f,
                            new Color(1.60f, 4.40f, 6.20f, 1f),
                            new Color(0.65f, 1.85f, 4.20f, 1f));
                    }
                }

                if (gtActive && !_wasGtActive)
                {
                    SpawnShockwaveRing(
                        Vector3.zero, 12.5f, 0.58f,
                        new Color(4.60f, 3.10f, 0.55f, 1f),
                        new Color(2.80f, 1.45f, 0.18f, 1f));
                }
            }
            _wasGtActive = gtActive;

            float targetHeat = 0.0f;
            if (inRun)
            {
                targetHeat = 0.06f + 0.05f * Mathf.Clamp(session.EffectiveTimeScale - 1f, 0f, 4f) + _killStreakHeat * 0.72f;
                if (sim.HeatingUpActive) targetHeat += 0.42f;
                if (gtActive) targetHeat = Mathf.Max(targetHeat, 0.62f);
            }
            _rotorHeatBlend = Mathf.MoveTowards(_rotorHeatBlend, targetHeat, dt * 2.8f);

            Shader.SetGlobalFloat(PropGlobalGolden, _goldenBlend);
            Shader.SetGlobalFloat(PropGlobalHeat, _rotorHeatBlend);

            // 2. Arena floor dynamic magnetic range & plasma saw groove
            float rangeWorld = GameIdsAndScale.SimToWorld(inRun ? sim.Stats.RangeSim : session.ComputeCurrentStats().RangeSim);
            int orbCount = inRun ? sim.ActiveOrbCount : session.ComputeCurrentStats().OrbsCount;
            if (_arenaFloorRenderer != null)
            {
                _arenaFloorRenderer.GetPropertyBlock(_floorMpb);
                _floorMpb.SetFloat(PropTowerRange, Mathf.Max(6.5f, rangeWorld));
                _floorMpb.SetFloat(PropOrbTrackActive, orbCount > 0 ? 1.0f : 0.0f);
                _arenaFloorRenderer.SetPropertyBlock(_floorMpb);
            }

            // 3. Core material properties (Rust on G0, Molten Core skin, Damage Hit Flash)
            if (inRun && sim.CoreHp < _prevCoreHp - 0.1)
            {
                _coreHitFlash = 0.75f;
            }
            _prevCoreHp = sim.CoreHp;
            _coreHitFlash = Mathf.Max(0f, _coreHitFlash - dt * 6.5f);

            float rust = grade == CoreVisualGrade.G0_Rusty ? 0.88f : 0.0f;
            float molten = session.MoltenCoreSkinEquipped ? 1.0f : 0.0f;
            ApplyCoreMaterialBlock(_chassisRenderer, rust * 0.55f, molten, 0f, _coreHitFlash);
            int gIdx = (int)grade;
            if ((uint)gIdx < (uint)_gradeRenderers.Length)
            {
                ApplyCoreMaterialBlock(_gradeRenderers[gIdx], rust, molten, _rotorHeatBlend * 0.35f, _coreHitFlash);
            }

            // 4. Core Animator triggers & turret yaw target
            UpdateCoreAnimationAndAim(sim, session, dt);

            // 5. Orbiting Plasma Saw Orbs
            UpdateOrbs(sim, inRun, dt);

            // 6. Satellites: Electromagnet Black Hole, Rail Emitter Death Ray, Golden Bot, Gem Drone
            UpdateBlackHole(sim, inRun, dt);
            UpdateDeathRayEmitter(sim, inRun, dt);
            UpdateGoldenBotAndGemDrone(sim, session, worldCam, inRun, dt);

            // 7. Dynamic Volumetric Beams (Death Ray, Vampire Siphon + Garlic, Chain Lightning) & Shockwave Rings
            UpdateDynamicBeams(sim, inRun, rangeWorld);
            UpdateShockwaveRings(dt);

            // Step Animators explicitly when running offscreen in EditMode
            if (!Application.isPlaying)
            {
                StepAllAnimatorsForOffscreen(dt);
                ApplyTurretYawWorld();
            }
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                ApplyTurretYawWorld();
            }
        }

        private void ApplyTurretYawWorld()
        {
            if (_turretYawBone == null) return;
            _turretYawBone.rotation = Quaternion.AngleAxis(_currentAimYawDeg, Vector3.up) * _turretYawBone.rotation;
        }

        private void StepAllAnimatorsForOffscreen(float dt)
        {
            if (_coreAnimator != null && _coreAnimator.gameObject.activeInHierarchy) _coreAnimator.Update(dt);
            if (_bhAnimator != null && _bhEmitterObj != null && _bhEmitterObj.activeSelf) _bhAnimator.Update(dt);
            if (_drAnimator != null && _drEmitterObj != null && _drEmitterObj.activeSelf) _drAnimator.Update(dt);
            if (_goldenBotAnimator != null && _goldenBotObj != null && _goldenBotObj.activeSelf) _goldenBotAnimator.Update(dt);
            if (_gemDroneAnimator != null && _gemDroneObj != null && _gemDroneObj.activeSelf) _gemDroneAnimator.Update(dt);
        }

        private void ApplyCoreMaterialBlock(Renderer rend, float rust, float molten, float heat, float hitFlash)
        {
            if (rend == null) return;
            rend.GetPropertyBlock(_coreMpb);
            _coreMpb.SetFloat(PropRustAmount, rust);
            _coreMpb.SetFloat(PropMoltenSkin, molten);
            _coreMpb.SetFloat(PropHeatGlow, heat);
            _coreMpb.SetFloat(PropHitFlash, hitFlash);
            rend.SetPropertyBlock(_coreMpb);
        }

        private void UpdateCoreAnimationAndAim(CombatSim sim, GameSession session, float dt)
        {
            if (_coreAnimator != null && _coreAnimator.runtimeAnimatorController != null)
            {
                float speedScale = session.InRun ? Mathf.Clamp(session.EffectiveTimeScale, 1f, 5f) : 0.75f;
                _coreAnimator.speed = 0.85f + 0.25f * speedScale;

                bool wantOverdrive = session.InRun && (sim.GoldenTower.Active || session.EffectiveTimeScale >= 3.0f);
                AnimatorStateInfo baseInfo = _coreAnimator.GetCurrentAnimatorStateInfo(0);
                if (sim.IsGameOver)
                {
                    if (baseInfo.shortNameHash != HashOverloadDeath)
                        _coreAnimator.Play(HashOverloadDeath, 0, 0f);
                }
                else if (wantOverdrive)
                {
                    if (baseInfo.shortNameHash != HashOverdriveSpin)
                        _coreAnimator.CrossFade(HashOverdriveSpin, 0.18f, 0);
                }
                else
                {
                    if (baseInfo.shortNameHash != HashIdleSpin)
                        _coreAnimator.CrossFade(HashIdleSpin, 0.18f, 0);
                }

                // Trigger additive recoil & crunch on layer 1
                if (_coreAnimator.layerCount > 1 && session.InRun)
                {
                    bool firedShot = false;
                    bool hadCrunch = false;
                    for (int i = 0; i < sim.SfxCount; i++)
                    {
                        if (sim.SfxQueue[i] == SfxId.PneumoShot || sim.SfxQueue[i] == SfxId.PlasmaHit) firedShot = true;
                        else if (sim.SfxQueue[i] == SfxId.Crunch) hadCrunch = true;
                    }

                    if (hadCrunch)
                    {
                        _coreAnimator.Play(HashCrunch, 1, 0f);
                    }
                    else if (firedShot)
                    {
                        _coreAnimator.Play(_fireLeftNext ? HashFireL : HashFireR, 1, 0f);
                        _fireLeftNext = !_fireLeftNext;
                    }
                }
            }

            // Aim turret toward priority nearest / Vampire / Boss enemy
            if (session.InRun && sim.ActiveEnemyCount > 0)
            {
                int bestIdx = 0;
                float bestScore = float.MaxValue;
                for (int i = 0; i < sim.ActiveEnemyCount; i++)
                {
                    float s = sim.Enemies[i].RadiusDist;
                    if (sim.Enemies[i].IsVampireBeaming) s -= 3.0f;
                    else if (sim.Enemies[i].Type == EnemyType.Boss) s -= 0.6f;
                    if (s < bestScore)
                    {
                        bestScore = s;
                        bestIdx = i;
                    }
                }
                float wx = GameIdsAndScale.SimToWorld(sim.Enemies[bestIdx].PosX);
                float wz = GameIdsAndScale.SimToWorld(sim.Enemies[bestIdx].PosY);
                if (wx * wx + wz * wz > 0.01f)
                {
                    _targetAimYawDeg = Mathf.Atan2(wx, wz) * Mathf.Rad2Deg;
                }
            }
            else
            {
                _targetAimYawDeg = Mathf.Repeat(_targetAimYawDeg + dt * 18.0f, 360f);
            }

            _currentAimYawDeg = Mathf.MoveTowardsAngle(_currentAimYawDeg, _targetAimYawDeg, dt * 540.0f);
            LatestAimYawRad = _currentAimYawDeg * Mathf.Deg2Rad;
        }

        private Vector3 FindPlasmaImpactWorldPos(CombatSim sim)
        {
            for (int i = 0; i < sim.ActiveEnemyCount; i++)
            {
                if (sim.Enemies[i].Type == EnemyType.Boss)
                {
                    return new Vector3(
                        GameIdsAndScale.SimToWorld(sim.Enemies[i].PosX),
                        0.12f,
                        GameIdsAndScale.SimToWorld(sim.Enemies[i].PosY));
                }
            }
            float yawRad = _currentAimYawDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yawRad) * 3.8f, 0.12f, Mathf.Cos(yawRad) * 3.8f);
        }

        private void UpdateShockwaveRings(float dt)
        {
            for (int i = 0; i < MAX_SHOCKWAVES; i++)
            {
                if (!_shockwaves[i].Active) continue;
                ref ShockwaveState sw = ref _shockwaves[i];

                sw.Age += dt;
                if (sw.Age >= sw.Duration)
                {
                    sw.Active = false;
                    if (_shockwaveQuads[i] != null && _shockwaveQuads[i].activeSelf)
                    {
                        _shockwaveQuads[i].SetActive(false);
                    }
                    continue;
                }

                float t = Mathf.Clamp01(sw.Age / sw.Duration);
                float easeOut = 1.0f - (1.0f - t) * (1.0f - t);
                float frontUv = Mathf.Lerp(0.14f, 0.92f, easeOut);
                float fade = (1.0f - t) * Mathf.Sqrt(1.0f - t);

                GameObject quad = _shockwaveQuads[i];
                MeshRenderer mr = _shockwaveRenderers[i];
                if (quad != null && mr != null)
                {
                    if (!quad.activeSelf) quad.SetActive(true);
                    quad.transform.localPosition = sw.Position;
                    quad.transform.localScale = new Vector3(sw.MaxDiameter, 1f, sw.MaxDiameter);

                    mr.GetPropertyBlock(_shockwaveMpb);
                    _shockwaveMpb.SetFloat(PropHorizonRadius, frontUv);
                    _shockwaveMpb.SetFloat(PropIntensity, 1.45f * fade);
                    _shockwaveMpb.SetColor(PropInnerRimColor, sw.RimColor);
                    _shockwaveMpb.SetColor(PropSwirlColor, sw.SwirlColor);
                    mr.SetPropertyBlock(_shockwaveMpb);
                }
            }
        }

        private void UpdateOrbs(CombatSim sim, bool inRun, float dt)
        {
            int count = inRun ? Mathf.Min(sim.ActiveOrbCount, MAX_ORB_VISUALS) : 0;
            for (int i = 0; i < MAX_ORB_VISUALS; i++)
            {
                bool active = i < count && sim.Orbs[i].Active;
                if (_orbObjects[i].activeSelf != active)
                {
                    _orbObjects[i].SetActive(active);
                }
                if (active)
                {
                    float wx = GameIdsAndScale.SimToWorld(sim.Orbs[i].PosX);
                    float wz = GameIdsAndScale.SimToWorld(sim.Orbs[i].PosY);
                    float scale = sim.Orbs[i].IsExtraOuter ? 1.15f : 1.0f;
                    Transform tr = _orbTransforms[i];
                    tr.localPosition = new Vector3(wx, 0.46f, wz);
                    tr.localScale = new Vector3(scale, scale, scale);
                    // High-RPM circular blade spin
                    float spinDir = sim.Orbs[i].IsExtraOuter ? -920f : 820f;
                    tr.Rotate(0f, spinDir * dt, 0f, Space.Self);
                }
            }
        }

        private void UpdateBlackHole(CombatSim sim, bool inRun, float dt)
        {
            bool bhUnlocked = inRun && sim.BlackHole.Unlocked;
            bool bhActive = bhUnlocked && sim.BlackHole.Active;

            if (_bhEmitterObj != null)
            {
                if (_bhEmitterObj.activeSelf != bhActive)
                    _bhEmitterObj.SetActive(bhActive);

                if (bhActive)
                {
                    float wx = GameIdsAndScale.SimToWorld(sim.BlackHole.PosX);
                    float wz = GameIdsAndScale.SimToWorld(sim.BlackHole.PosY);
                    _bhEmitterObj.transform.localPosition = new Vector3(wx, 0.05f, wz);
                    float faceCenterYaw = Mathf.Atan2(-wx, -wz) * Mathf.Rad2Deg;
                    _bhEmitterObj.transform.localRotation = Quaternion.Euler(0f, faceCenterYaw, 0f);

                    if (!_wasBhActive && _bhAnimator != null)
                    {
                        _bhAnimator.Play("Deploy", 0, 0f);
                    }
                }
            }

            if (_bhVortexQuad != null)
            {
                if (_bhVortexQuad.activeSelf != bhActive)
                    _bhVortexQuad.SetActive(bhActive);

                if (bhActive)
                {
                    float wx = GameIdsAndScale.SimToWorld(sim.BlackHole.PosX);
                    float wz = GameIdsAndScale.SimToWorld(sim.BlackHole.PosY);
                    float diam = Mathf.Max(4.4f, GameIdsAndScale.SimToWorld(sim.BlackHole.RadiusSim) * 2.35f);
                    _bhVortexQuad.transform.localPosition = new Vector3(wx, 0.14f, wz);
                    _bhVortexQuad.transform.localScale = new Vector3(diam, 1f, diam);

                    float fade = Mathf.Clamp01(sim.BlackHole.TimeRemaining * 2.5f);
                    _bhVortexRenderer.GetPropertyBlock(_vortexMpb);
                    _vortexMpb.SetFloat(PropIntensity, 1.30f * fade);
                    _bhVortexRenderer.SetPropertyBlock(_vortexMpb);
                }
            }

            _wasBhActive = bhActive;
        }

        private void UpdateDeathRayEmitter(CombatSim sim, bool inRun, float dt)
        {
            bool drEquipped = inRun && sim.DeathRay.Equipped;
            bool drActive = drEquipped && sim.DeathRay.Active;

            if (_drEmitterObj != null)
            {
                if (_drEmitterObj.activeSelf != drEquipped)
                    _drEmitterObj.SetActive(drEquipped);

                if (drEquipped)
                {
                    float angle = drActive ? sim.DeathRay.Angle : (_currentAimYawDeg * Mathf.Deg2Rad);
                    float dirX = Mathf.Cos(angle);
                    float dirZ = Mathf.Sin(angle);
                    float mountR = 1.15f;
                    _drEmitterObj.transform.localPosition = new Vector3(dirX * mountR, 0.85f, dirZ * mountR);
                    _drEmitterObj.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(dirX, dirZ) * Mathf.Rad2Deg, 0f);

                    if (drActive && !_wasDrActive && _drAnimator != null)
                    {
                        _drAnimator.Play("ChargeFire", 0, 0f);
                    }
                }
            }
            _wasDrActive = drActive;
        }

        private void UpdateGoldenBotAndGemDrone(CombatSim sim, GameSession session, Camera worldCam, bool inRun, float dt)
        {
            // Golden Bot
            bool botActive = inRun && sim.GoldenBot.Unlocked;
            if (_goldenBotObj != null)
            {
                if (_goldenBotObj.activeSelf != botActive)
                    _goldenBotObj.SetActive(botActive);

                if (botActive)
                {
                    float wx = GameIdsAndScale.SimToWorld(sim.GoldenBot.PosX);
                    float wz = GameIdsAndScale.SimToWorld(sim.GoldenBot.PosY);
                    _goldenBotObj.transform.localPosition = new Vector3(wx, 1.15f, wz);
                    _goldenBotObj.transform.Rotate(0f, 65f * dt, 0f, Space.Self);
                }
            }

            bool pulseField = botActive && sim.GoldenBot.PulseActive;
            if (_goldenBotFieldQuad != null)
            {
                if (_goldenBotFieldQuad.activeSelf != pulseField)
                    _goldenBotFieldQuad.SetActive(pulseField);

                if (pulseField)
                {
                    float wx = GameIdsAndScale.SimToWorld(sim.GoldenBot.PosX);
                    float wz = GameIdsAndScale.SimToWorld(sim.GoldenBot.PosY);
                    float diam = GameIdsAndScale.SimToWorld(sim.GoldenBot.RadiusSim) * 2.0f;
                    _goldenBotFieldQuad.transform.localPosition = new Vector3(wx, 0.08f, wz);
                    _goldenBotFieldQuad.transform.localScale = new Vector3(diam, 1f, diam);

                    _goldenBotFieldRenderer.GetPropertyBlock(_botFieldMpb);
                    _botFieldMpb.SetFloat(PropIntensity, 1.15f);
                    _goldenBotFieldRenderer.SetPropertyBlock(_botFieldMpb);
                }
            }

            // Gem Drone (+5 Gems clickable purple drone)
            bool droneActive = inRun && sim.GemDrone.Active;
            if (_gemDroneObj != null)
            {
                if (_gemDroneObj.activeSelf != droneActive)
                    _gemDroneObj.SetActive(droneActive);

                if (droneActive)
                {
                    float wx = GameIdsAndScale.SimToWorld(sim.GemDrone.PosX);
                    float wz = GameIdsAndScale.SimToWorld(sim.GemDrone.PosY);
                    float bob = 1.45f + 0.16f * Mathf.Sin(Time.unscaledTime * 5.0f);
                    Vector3 droneWorld = new Vector3(wx, bob, wz);
                    _gemDroneObj.transform.localPosition = droneWorld;
                    _gemDroneObj.transform.Rotate(0f, -120f * dt, 0f, Space.Self);

                    // Direct 3D arena tap claim support
                    if (Application.isPlaying && worldCam != null && TryGetPointerDownScreenPos(out Vector2 screenTap))
                    {
                        Vector3 proj = worldCam.WorldToScreenPoint(droneWorld);
                        if (proj.z > 0f)
                        {
                            float tapRadiusPx = 92f * Mathf.Max(0.5f, Screen.width / 1080f);
                            if ((new Vector2(proj.x, proj.y) - screenTap).sqrMagnitude <= tapRadiusPx * tapRadiusPx)
                            {
                                if (session.ClaimActiveGemDrone())
                                {
                                    ProceduralSfxPlayer.Instance?.PlayUI(SfxId.UiUnlock);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static bool TryGetPointerDownScreenPos(out Vector2 pos)
        {
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    pos = t.position;
                    return true;
                }
            }
            if (Input.GetMouseButtonDown(0))
            {
                pos = Input.mousePosition;
                return true;
            }
            pos = default;
            return false;
        }

        private void UpdateDynamicBeams(CombatSim sim, bool inRun, float rangeWorld)
        {
            // 1. CNC Death Ray Beam
            if (inRun && sim.DeathRay.Equipped && sim.DeathRay.Active)
            {
                float a = sim.DeathRay.Angle;
                float dx = Mathf.Cos(a);
                float dz = Mathf.Sin(a);
                Vector3 start = new Vector3(dx * 1.25f, 0.78f, dz * 1.25f);
                Vector3 end = new Vector3(dx * (rangeWorld * 1.35f), 0.78f, dz * (rangeWorld * 1.35f));
                WriteSingleRibbon(_deathRayMesh, _deathRayRenderer, start, end, 1.35f, new Color32(255, 255, 255, 255));
            }
            else if (_deathRayRenderer != null)
            {
                _deathRayRenderer.enabled = false;
            }

            // 2. Vampire Siphon Beams (+ Magnetic Garlic Counter-Rings)
            int siphonSegs = 0;
            if (inRun && sim.AnyVampireBeaming)
            {
                Vector3 coreAnchor = new Vector3(0f, 0.76f, 0f);
                for (int i = 0; i < sim.ActiveEnemyCount && siphonSegs < MAX_RIBBON_SEGMENTS; i++)
                {
                    if (!sim.Enemies[i].IsVampireBeaming) continue;
                    Vector3 vampPos = new Vector3(
                        GameIdsAndScale.SimToWorld(sim.Enemies[i].PosX),
                        0.72f,
                        GameIdsAndScale.SimToWorld(sim.Enemies[i].PosY));
                    AppendRibbonQuad(ref siphonSegs, vampPos, coreAnchor, 0.92f, new Color32(255, 255, 255, 240));
                }
            }

            if (siphonSegs > 0 && _vampireSiphonRenderer != null)
            {
                CommitRibbonSegments(_vampireSiphonMesh, _vampireSiphonRenderer, siphonSegs);
                float garlicIntensity = Mathf.Clamp01(sim.Stats.MagneticGarlicLevel * 0.36f);
                _vampireSiphonRenderer.GetPropertyBlock(_siphonMpb);
                _siphonMpb.SetFloat(PropGarlicIntensity, garlicIntensity);
                _siphonMpb.SetFloat(PropFade, 1.15f);
                _vampireSiphonRenderer.SetPropertyBlock(_siphonMpb);
            }
            else if (_vampireSiphonRenderer != null)
            {
                _vampireSiphonRenderer.enabled = false;
            }

            // 3. Chain Lightning Arcs
            int zapSegs = 0;
            if (inRun && sim.ActiveChainArcCount > 0)
            {
                for (int i = 0; i < sim.ActiveChainArcCount && zapSegs < MAX_RIBBON_SEGMENTS; i++)
                {
                    ref ChainArcState arc = ref sim.ChainArcs[i];
                    Vector3 from = new Vector3(GameIdsAndScale.SimToWorld(arc.FromX), 0.74f, GameIdsAndScale.SimToWorld(arc.FromY));
                    Vector3 to = new Vector3(GameIdsAndScale.SimToWorld(arc.ToX), 0.74f, GameIdsAndScale.SimToWorld(arc.ToY));
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(arc.TimeRemaining / 0.10f) * 255f), 85, 255);
                    AppendRibbonQuad(ref zapSegs, from, to, 1.15f, new Color32(255, 255, 255, alpha));
                }
            }

            if (zapSegs > 0 && _chainZapRenderer != null)
            {
                CommitRibbonSegments(_chainZapMesh, _chainZapRenderer, zapSegs);
            }
            else if (_chainZapRenderer != null)
            {
                _chainZapRenderer.enabled = false;
            }
        }

        private void WriteSingleRibbon(Mesh mesh, MeshRenderer mr, Vector3 from, Vector3 to, float width, Color32 col)
        {
            int segs = 0;
            AppendRibbonQuad(ref segs, from, to, width, col);
            CommitRibbonSegments(mesh, mr, segs);
        }

        private void AppendRibbonQuad(ref int segIndex, Vector3 from, Vector3 to, float width, Color32 col)
        {
            if (segIndex >= MAX_RIBBON_SEGMENTS) return;
            Vector3 delta = to - from;
            delta.y = 0f;
            float lenSq = delta.sqrMagnitude;
            if (lenSq < 0.0004f) return;

            Vector3 dir = delta / Mathf.Sqrt(lenSq);
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);

            int v = segIndex * 4;
            _ribVerts[v + 0] = from - side;
            _ribVerts[v + 1] = from + side;
            _ribVerts[v + 2] = to + side;
            _ribVerts[v + 3] = to - side;

            _ribUvs[v + 0] = new Vector2(0f, 0f);
            _ribUvs[v + 1] = new Vector2(0f, 1f);
            _ribUvs[v + 2] = new Vector2(1f, 1f);
            _ribUvs[v + 3] = new Vector2(1f, 0f);

            _ribCols[v + 0] = col;
            _ribCols[v + 1] = col;
            _ribCols[v + 2] = col;
            _ribCols[v + 3] = col;

            segIndex++;
        }

        private void CommitRibbonSegments(Mesh mesh, MeshRenderer mr, int segCount)
        {
            if (mesh == null || mr == null || segCount <= 0) return;

            int vCount = segCount * 4;
            int iCount = segCount * 6;
            mesh.Clear();
            mesh.SetVertices(_ribVerts, 0, vCount);
            mesh.SetUVs(0, _ribUvs, 0, vCount);
            mesh.SetColors(_ribCols, 0, vCount);
            mesh.SetIndices(_ribIndices, 0, iCount, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(60f, 10f, 60f));
            mr.enabled = true;
        }
    }
}
