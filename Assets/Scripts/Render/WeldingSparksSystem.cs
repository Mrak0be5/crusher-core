using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Zero-allocation 1536-slot GPU-instanced welding spark emitter (_UnlitSparkMode = 1).
    /// Emits velocity-stretched incandescent sparks on enemy crunches, plasma saw slices, and kills.
    /// </summary>
    public sealed class WeldingSparksSystem : MonoBehaviour
    {
        public const int CAPACITY = 1536;
        private const int BATCH_MAX = 16;
        private const int MAX_BATCHES = CAPACITY / BATCH_MAX;

        private static readonly int PropInstData = Shader.PropertyToID("_InstData");
        private static readonly int PropInstColor = Shader.PropertyToID("_InstColor");

        private struct SparkItem
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector4 Color;
            public float Age;
            public float Lifetime;
            public float Thickness;
        }

        private sealed class SparkBatch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BATCH_MAX];
            public readonly Vector4[] InstData = new Vector4[BATCH_MAX];
            public readonly Vector4[] InstColors = new Vector4[BATCH_MAX];
            public MaterialPropertyBlock Mpb;
            public int Count;
        }

        [SerializeField] private CrusherAssetRegistry _registry;

        private readonly SparkItem[] _items = new SparkItem[CAPACITY];
        private readonly SparkBatch[] _batches = new SparkBatch[MAX_BATCHES];
        private Mesh _sparkDiamondMesh;
        private int _writeCursor;
        private float _grindEmitTimer;
        private FastRng _rng = new FastRng(0x5FA8C5771234UL);
        private bool _initialized;

        public void Initialize(CrusherAssetRegistry registry)
        {
            _registry = registry;
            if (_initialized || _registry == null) return;
            _initialized = true;

            _sparkDiamondMesh = BuildDiamondStreakMesh();
            for (int b = 0; b < MAX_BATCHES; b++)
            {
                _batches[b] = new SparkBatch
                {
                    Mpb = new MaterialPropertyBlock(),
                    Count = 0
                };
            }
        }

        private void OnDestroy()
        {
            if (_sparkDiamondMesh != null)
            {
                DestroyImmediate(_sparkDiamondMesh);
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < CAPACITY; i++)
                _items[i].Active = false;
        }

        private static Mesh BuildDiamondStreakMesh()
        {
            var m = new Mesh { name = "ProcSparkDiamond" };
            m.vertices = new[]
            {
                new Vector3( 0.0f,  0.0f,  0.55f), // front tip (+Z)
                new Vector3( 0.0f,  0.0f, -0.55f), // tail (-Z)
                new Vector3( 0.14f, 0.0f,  0.05f),
                new Vector3(-0.14f, 0.0f,  0.05f),
                new Vector3( 0.0f,  0.14f, 0.05f),
                new Vector3( 0.0f, -0.14f, 0.05f)
            };
            m.normals = new[]
            {
                Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down
            };
            m.colors32 = new[]
            {
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255)
            };
            m.triangles = new[]
            {
                0, 4, 2,  0, 2, 5,  0, 5, 3,  0, 3, 4,
                1, 2, 4,  1, 5, 2,  1, 3, 5,  1, 4, 3
            };
            m.bounds = new Bounds(Vector3.zero, new Vector3(1.5f, 1.5f, 1.5f));
            return m;
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (!_initialized) return;

            float dt = Mathf.Clamp(unscaledDt, 0f, 0.05f);

            // 1. Emit bursts from kills & continuous rotor contact grinding
            if (sim != null && session != null && session.InRun)
            {
                for (int k = 0; k < sim.KillEventCount; k++)
                {
                    ref KillEvent ev = ref sim.KillEvents[k];
                    Vector3 origin = new Vector3(
                        GameIdsAndScale.SimToWorld(ev.PosX),
                        0.52f,
                        GameIdsAndScale.SimToWorld(ev.PosY));

                    Vector4 col = ev.KilledByOrbOrRay
                        ? new Vector4(0.55f, 1.85f, 2.80f, 1f) // cyan plasma saw sparks
                        : (ev.KilledInGolden
                            ? new Vector4(2.60f, 1.75f, 0.35f, 1f) // molten gold sparks
                            : new Vector4(2.40f, 1.15f, 0.25f, 1f)); // hot carbide welding sparks

                    int burstCount = ev.Type == EnemyType.Boss ? 48 : (ev.KilledByOrbOrRay ? 18 : 14);
                    SpawnSparkBurst(origin, burstCount, col, ev.Type == EnemyType.Boss ? 1.45f : 1.0f);
                }

                // Continuous grinding sparks on enemies pressed against rotor teeth
                _grindEmitTimer -= dt;
                if (_grindEmitTimer <= 0f)
                {
                    _grindEmitTimer = 0.045f;
                    float contactThresh = BalanceTables.CORE_RADIUS_SIM + 0.35f;
                    int emittedContacts = 0;
                    for (int i = 0; i < sim.ActiveEnemyCount && emittedContacts < 4; i++)
                    {
                        if (sim.Enemies[i].RadiusDist <= contactThresh)
                        {
                            Vector3 p = new Vector3(
                                GameIdsAndScale.SimToWorld(sim.Enemies[i].PosX) * 0.78f,
                                0.55f,
                                GameIdsAndScale.SimToWorld(sim.Enemies[i].PosY) * 0.78f);
                            SpawnSparkBurst(p, 5, new Vector4(2.35f, 1.25f, 0.30f, 1f), 0.85f);
                            emittedContacts++;
                        }
                    }
                }
            }

            // 2. Step spark trajectories & pack GPU batches
            for (int b = 0; b < MAX_BATCHES; b++)
            {
                _batches[b].Count = 0;
            }

            int activeBatch = 0;
            for (int i = 0; i < CAPACITY; i++)
            {
                if (!_items[i].Active) continue;
                ref SparkItem sp = ref _items[i];

                sp.Age += dt;
                if (sp.Age >= sp.Lifetime)
                {
                    sp.Active = false;
                    continue;
                }

                sp.Velocity.y -= 16.0f * dt;
                sp.Velocity *= (1.0f - 1.8f * dt);
                sp.Position += sp.Velocity * dt;

                if (sp.Position.y < 0.04f)
                {
                    sp.Position.y = 0.04f;
                    sp.Velocity.y = Mathf.Abs(sp.Velocity.y) * 0.48f;
                }

                float speedSq = sp.Velocity.sqrMagnitude;
                Quaternion rot = speedSq > 0.001f
                    ? Quaternion.LookRotation(sp.Velocity.normalized, Vector3.up)
                    : Quaternion.identity;

                float life01 = 1.0f - (sp.Age / sp.Lifetime);
                float speed = Mathf.Sqrt(Mathf.Max(0.01f, speedSq));
                float len = Mathf.Clamp(0.16f + speed * 0.048f, 0.16f, 0.95f) * (0.4f + 0.6f * life01);
                float thick = sp.Thickness * life01;

                if (activeBatch < MAX_BATCHES)
                {
                    SparkBatch batch = _batches[activeBatch];
                    int slot = batch.Count++;
                    batch.Matrices[slot] = Matrix4x4.TRS(sp.Position, rot, new Vector3(thick, thick, len));
                    batch.InstData[slot] = new Vector4(0f, 0.35f + 1.85f * life01, 0f, 0f);
                    batch.InstColors[slot] = sp.Color;
                    if (batch.Count >= BATCH_MAX)
                    {
                        activeBatch++;
                    }
                }
            }
        }

        public void SpawnSparkBurst(Vector3 worldOrigin, int count, Vector4 hdrColor, float speedMult)
        {
            if (!_initialized) return;

            Vector3 radial = worldOrigin;
            radial.y = 0f;
            Vector3 tangent = radial.sqrMagnitude > 0.01f
                ? new Vector3(-radial.z, 0f, radial.x).normalized
                : Vector3.right;

            for (int n = 0; n < count; n++)
            {
                int slot = _writeCursor;
                _writeCursor = (_writeCursor + 1) % CAPACITY;

                float ang = _rng.NextFloat() * Mathf.PI * 2f;
                float elev = 0.25f + _rng.NextFloat() * 0.85f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), elev, Mathf.Sin(ang)).normalized;
                float spd = (5.5f + _rng.NextFloat() * 9.5f) * speedMult;
                float tangSign = (_rng.NextUInt() & 1u) == 0u ? 1f : -1f;

                _items[slot] = new SparkItem
                {
                    Active = true,
                    Position = worldOrigin,
                    Velocity = dir * spd + tangent * (tangSign * 3.5f),
                    Color = hdrColor,
                    Age = 0f,
                    Lifetime = 0.24f + _rng.NextFloat() * 0.34f,
                    Thickness = 0.28f + _rng.NextFloat() * 0.22f
                };
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
            if (!_initialized || _sparkDiamondMesh == null || _registry.MatWeldingSparksInstanced == null) return;

            for (int b = 0; b < MAX_BATCHES; b++)
            {
                SparkBatch batch = _batches[b];
                if (batch.Count <= 0) continue;

                batch.Mpb.SetVectorArray(PropInstData, batch.InstData);
                batch.Mpb.SetVectorArray(PropInstColor, batch.InstColors);
                Graphics.DrawMeshInstanced(
                    _sparkDiamondMesh,
                    0,
                    _registry.MatWeldingSparksInstanced,
                    batch.Matrices,
                    batch.Count,
                    batch.Mpb,
                    ShadowCastingMode.Off,
                    false,
                    0,
                    targetCamera);
            }
        }
    }
}
