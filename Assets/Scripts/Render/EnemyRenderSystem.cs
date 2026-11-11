using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Zero-allocation GPU-instanced renderer for Basic/Fast/Tank/Ranged/Vampire enemies and 3 projectile kinds,
    /// plus a 10-slot skeletal pool for SM_Enemy_Boss_ATM.
    /// </summary>
    public sealed class EnemyRenderSystem : MonoBehaviour
    {
        private const int BATCH_MAX = 16;
        private const int ENEMY_PAGES = 4;
        private const int PROJ_PAGES = 6;
        private const int BOSS_POOL_SIZE = BalanceTables.MAX_ACTIVE_BOSS;

        private static readonly int PropInstData = Shader.PropertyToID("_InstData");
        private static readonly int PropInstColor = Shader.PropertyToID("_InstColor");
        private static readonly int PropHitFlash = Shader.PropertyToID("_HitFlash");
        private static readonly int PropSquash = Shader.PropertyToID("_Squash");
        private static readonly int PropHeatGlow = Shader.PropertyToID("_HeatGlow");

        private static readonly int HashBossRoll = Animator.StringToHash("Roll");
        private static readonly int HashBossRam = Animator.StringToHash("Ram");

        [SerializeField] private CrusherAssetRegistry _registry;

        private sealed class InstancedEnemyBatch
        {
            public Mesh Mesh;
            public Material Material;
            public readonly Matrix4x4[][] Matrices = new Matrix4x4[ENEMY_PAGES][];
            public readonly Vector4[][] InstData = new Vector4[ENEMY_PAGES][];
            public MaterialPropertyBlock Mpb;
            public int TotalCount;

            public InstancedEnemyBatch()
            {
                for (int p = 0; p < ENEMY_PAGES; p++)
                {
                    Matrices[p] = new Matrix4x4[BATCH_MAX];
                    InstData[p] = new Vector4[BATCH_MAX];
                }
            }
        }

        private readonly InstancedEnemyBatch[] _batches = new InstancedEnemyBatch[GameIdsAndScale.ENEMY_TYPE_COUNT];

        // Pooled skeletal Boss ATM instances
        private readonly GameObject[] _bossObjects = new GameObject[BOSS_POOL_SIZE];
        private readonly Transform[] _bossTransforms = new Transform[BOSS_POOL_SIZE];
        private readonly Animator[] _bossAnimators = new Animator[BOSS_POOL_SIZE];
        private readonly Renderer[] _bossRenderers = new Renderer[BOSS_POOL_SIZE];
        private MaterialPropertyBlock _bossMpb;

        // Instanced projectiles (paged in slices of BATCH_MAX=16)
        private readonly Matrix4x4[][] _projMatrices = new Matrix4x4[PROJ_PAGES][];
        private readonly Vector4[][] _projInstData = new Vector4[PROJ_PAGES][];
        private readonly Vector4[][] _projInstColors = new Vector4[PROJ_PAGES][];
        private MaterialPropertyBlock _projMpb;
        private int _projCount;

        private float _animClock;
        private bool _initialized;

        public void Initialize(CrusherAssetRegistry registry)
        {
            _registry = registry;
            if (_initialized || _registry == null) return;
            _initialized = true;

            Mesh[] meshesByType =
            {
                _registry.MeshEnemyBasic,
                _registry.MeshEnemyFast,
                _registry.MeshEnemyTank,
                _registry.MeshEnemyRanged,
                null, // Boss uses skeletal pool
                _registry.MeshEnemyVampire
            };

            for (int t = 0; t < GameIdsAndScale.ENEMY_TYPE_COUNT; t++)
            {
                if (t == (int)EnemyType.Boss) continue;
                Material mat = (_registry.MatEnemiesInstanced != null && t < _registry.MatEnemiesInstanced.Length)
                    ? _registry.MatEnemiesInstanced[t]
                    : null;

                _batches[t] = new InstancedEnemyBatch
                {
                    Mesh = meshesByType[t],
                    Material = mat,
                    Mpb = new MaterialPropertyBlock(),
                    TotalCount = 0
                };
            }

            _bossMpb = new MaterialPropertyBlock();
            if (_registry.PrefabBossAtm != null)
            {
                for (int i = 0; i < BOSS_POOL_SIZE; i++)
                {
                    GameObject go = Instantiate(_registry.PrefabBossAtm, transform);
                    go.name = "Boss_ATM_Pool_" + i;
                    var anim = go.GetComponent<Animator>();
                    if (anim == null) anim = go.AddComponent<Animator>();
                    anim.runtimeAnimatorController = _registry.AnimBossAtm;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                    Renderer rend = go.GetComponentInChildren<Renderer>(true);
                    if (rend != null && _registry.MatBossAtm != null)
                    {
                        rend.sharedMaterial = _registry.MatBossAtm;
                        rend.shadowCastingMode = ShadowCastingMode.On;
                    }

                    go.SetActive(false);
                    _bossObjects[i] = go;
                    _bossTransforms[i] = go.transform;
                    _bossAnimators[i] = anim;
                    _bossRenderers[i] = rend;
                }
            }

            _projMpb = new MaterialPropertyBlock();
            for (int p = 0; p < PROJ_PAGES; p++)
            {
                _projMatrices[p] = new Matrix4x4[BATCH_MAX];
                _projInstData[p] = new Vector4[BATCH_MAX];
                _projInstColors[p] = new Vector4[BATCH_MAX];
            }
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (!_initialized) return;

            _animClock += Mathf.Max(0f, unscaledDt);
            for (int t = 0; t < GameIdsAndScale.ENEMY_TYPE_COUNT; t++)
            {
                if (_batches[t] != null) _batches[t].TotalCount = 0;
            }
            _projCount = 0;

            if (sim == null || session == null || !session.InRun)
            {
                for (int b = 0; b < BOSS_POOL_SIZE; b++)
                {
                    if (_bossObjects[b] != null && _bossObjects[b].activeSelf)
                        _bossObjects[b].SetActive(false);
                }
                return;
            }

            // 1. Populate enemy batches & skeletal Boss pool
            int bossCursor = 0;
            int enemyCount = sim.ActiveEnemyCount;
            for (int i = 0; i < enemyCount; i++)
            {
                ref EnemyState en = ref sim.Enemies[i];
                float wx = GameIdsAndScale.SimToWorld(en.PosX);
                float wz = GameIdsAndScale.SimToWorld(en.PosY);
                float faceCenterYaw = Mathf.Atan2(-wx, -wz) * Mathf.Rad2Deg;

                float hitFlash = Mathf.Clamp01(en.HitFlashTimer / 0.12f);
                float squash = en.SquashTimer > 0f
                    ? Mathf.Sin(Mathf.Clamp01(en.SquashTimer / 0.18f) * Mathf.PI) * 0.28f
                    : 0f;

                if (en.Type == EnemyType.Boss)
                {
                    if (bossCursor < BOSS_POOL_SIZE && _bossObjects[bossCursor] != null)
                    {
                        GameObject bGo = _bossObjects[bossCursor];
                        if (!bGo.activeSelf) bGo.SetActive(true);

                        _bossTransforms[bossCursor].SetPositionAndRotation(
                            new Vector3(wx, 0f, wz),
                            Quaternion.Euler(0f, faceCenterYaw, 0f));
                        _bossTransforms[bossCursor].localScale = Vector3.one;

                        Animator anim = _bossAnimators[bossCursor];
                        if (anim != null && anim.runtimeAnimatorController != null)
                        {
                            bool inMelee = en.RadiusDist <= (BalanceTables.CORE_RADIUS_SIM + en.HitboxRadius + 0.08f);
                            int targetHash = inMelee ? HashBossRam : HashBossRoll;
                            if (anim.GetCurrentAnimatorStateInfo(0).shortNameHash != targetHash)
                            {
                                anim.CrossFade(targetHash, 0.12f, 0);
                            }
                            if (!Application.isPlaying)
                            {
                                anim.Update(Mathf.Max(0.016f, unscaledDt));
                            }
                        }

                        Renderer rend = _bossRenderers[bossCursor];
                        if (rend != null)
                        {
                            rend.GetPropertyBlock(_bossMpb);
                            _bossMpb.SetFloat(PropHitFlash, hitFlash);
                            _bossMpb.SetFloat(PropSquash, squash * 0.65f);
                            _bossMpb.SetFloat(PropHeatGlow, en.ConsecutiveHits >= 3 ? 0.65f : 0.15f);
                            rend.SetPropertyBlock(_bossMpb);
                        }

                        bossCursor++;
                    }
                    continue;
                }

                int typeIdx = (int)en.Type;
                InstancedEnemyBatch batch = _batches[typeIdx];
                if (batch == null || batch.TotalCount >= BATCH_MAX * ENEMY_PAGES) continue;

                float y = 0f;
                float scale = 1.0f;
                float heat = en.IsInBlackHole ? 0.55f : 0f;
                float rollPhase = 0f;

                switch (en.Type)
                {
                    case EnemyType.Basic:
                        scale = 1.0f;
                        break;
                    case EnemyType.Fast:
                        scale = 0.96f;
                        rollPhase = -en.RadiusDist * 5.8f;
                        break;
                    case EnemyType.Tank:
                        scale = 1.12f;
                        if (en.ConsecutiveHits >= 2) heat = Mathf.Max(heat, 0.45f);
                        break;
                    case EnemyType.Ranged:
                        scale = 1.02f;
                        heat = Mathf.Max(heat, Mathf.Clamp01(1.0f - en.RangedFireTimer / 0.75f) * 1.35f);
                        break;
                    case EnemyType.Vampire:
                        y = 0.48f + 0.10f * Mathf.Sin(_animClock * 5.5f + en.Id * 1.3f);
                        scale = 1.10f;
                        if (en.IsVampireBeaming) heat = 1.40f;
                        break;
                }

                int totalIdx = batch.TotalCount++;
                int page = totalIdx / BATCH_MAX;
                int slot = totalIdx % BATCH_MAX;
                Quaternion rot = Quaternion.Euler(0f, faceCenterYaw, 0f);
                if (en.IsInBlackHole)
                {
                    rot *= Quaternion.Euler(14f * Mathf.Sin(_animClock * 9f + en.Id), _animClock * 180f, 0f);
                }

                batch.Matrices[page][slot] = Matrix4x4.TRS(
                    new Vector3(wx, y, wz),
                    rot,
                    new Vector3(scale, scale, scale));
                batch.InstData[page][slot] = new Vector4(hitFlash, heat, squash, rollPhase);
            }

            // Deactivate unused Boss pool entries
            for (int b = bossCursor; b < BOSS_POOL_SIZE; b++)
            {
                if (_bossObjects[b] != null && _bossObjects[b].activeSelf)
                {
                    _bossObjects[b].SetActive(false);
                }
            }

            // 2. Populate Projectile batch
            for (int p = 0; p < CombatSim.PROJECTILE_CAPACITY && _projCount < BATCH_MAX * PROJ_PAGES; p++)
            {
                if (!sim.Projectiles[p].Active) continue;
                ref ProjectileState pr = ref sim.Projectiles[p];

                float wx = GameIdsAndScale.SimToWorld(pr.PosX);
                float wz = GameIdsAndScale.SimToWorld(pr.PosY);
                float tx = GameIdsAndScale.SimToWorld(pr.TargetX);
                float tz = GameIdsAndScale.SimToWorld(pr.TargetY);

                float dx = tx - wx;
                float dz = tz - wz;
                float yaw = (dx * dx + dz * dz > 0.0001f) ? Mathf.Atan2(dx, dz) * Mathf.Rad2Deg : 0f;

                Vector3 scaleVec;
                Vector4 col;
                float glow;
                switch (pr.Kind)
                {
                    case ProjectileKind.PlasmaCannonSlug:
                        scaleVec = new Vector3(2.1f, 2.1f, 2.8f);
                        col = new Vector4(0.55f, 2.40f, 3.80f, 1.0f);
                        glow = 1.8f;
                        break;
                    case ProjectileKind.RangedEnemyBolt:
                        scaleVec = new Vector3(1.15f, 1.15f, 1.55f);
                        col = new Vector4(2.50f, 0.25f, 0.45f, 1.0f);
                        glow = 1.5f;
                        break;
                    default: // TowerNeedle
                        scaleVec = pr.IsCrit ? new Vector3(1.30f, 1.30f, 1.95f) : new Vector3(1.0f, 1.0f, 1.55f);
                        col = pr.IsCrit
                            ? new Vector4(2.80f, 1.85f, 0.35f, 1.0f)
                            : new Vector4(1.75f, 1.35f, 0.55f, 1.0f);
                        glow = pr.IsCrit ? 1.6f : 1.1f;
                        break;
                }

                int totalProj = _projCount++;
                int projPage = totalProj / BATCH_MAX;
                int projSlot = totalProj % BATCH_MAX;
                _projMatrices[projPage][projSlot] = Matrix4x4.TRS(
                    new Vector3(wx, 0.66f, wz),
                    Quaternion.Euler(0f, yaw, 0f),
                    scaleVec);
                _projInstData[projPage][projSlot] = new Vector4(0f, glow, 0f, 0f);
                _projInstColors[projPage][projSlot] = col;
            }
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                SubmitDrawCalls(null);
            }
        }

        public void SubmitDrawCalls(Camera targetCamera = null)
        {
            if (!_initialized) return;

            for (int t = 0; t < GameIdsAndScale.ENEMY_TYPE_COUNT; t++)
            {
                InstancedEnemyBatch batch = _batches[t];
                if (batch == null || batch.TotalCount <= 0 || batch.Mesh == null || batch.Material == null)
                    continue;

                int remaining = batch.TotalCount;
                for (int page = 0; page < ENEMY_PAGES && remaining > 0; page++)
                {
                    int cnt = Mathf.Min(BATCH_MAX, remaining);
                    batch.Mpb.SetVectorArray(PropInstData, batch.InstData[page]);
                    Graphics.DrawMeshInstanced(
                        batch.Mesh,
                        0,
                        batch.Material,
                        batch.Matrices[page],
                        cnt,
                        batch.Mpb,
                        ShadowCastingMode.On,
                        true,
                        0,
                        targetCamera);
                    remaining -= cnt;
                }
            }

            if (_projCount > 0 && _registry.MeshProjectile != null && _registry.MatProjectilesInstanced != null)
            {
                int remProj = _projCount;
                for (int page = 0; page < PROJ_PAGES && remProj > 0; page++)
                {
                    int cnt = Mathf.Min(BATCH_MAX, remProj);
                    _projMpb.SetVectorArray(PropInstData, _projInstData[page]);
                    _projMpb.SetVectorArray(PropInstColor, _projInstColors[page]);
                    Graphics.DrawMeshInstanced(
                        _registry.MeshProjectile,
                        0,
                        _registry.MatProjectilesInstanced,
                        _projMatrices[page],
                        cnt,
                        _projMpb,
                        ShadowCastingMode.Off,
                        false,
                        0,
                        targetCamera);
                    remProj -= cnt;
                }
            }
        }
    }
}
