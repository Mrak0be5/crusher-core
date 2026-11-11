using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Zero-allocation 2048-slot physical Voronoi scrap debris simulator rendered via WebGL2-safe GPU instancing (BATCH_MAX = 16).
    /// Chunks inherit enemy armor tint via _InstColor and cool down along a 3500K -> 1800K -> 900K -> dark steel thermal curve via _InstData.y.
    /// </summary>
    public sealed class DebrisSimulationSystem : MonoBehaviour
    {
        public const int CAPACITY = 2048;
        private const int BATCH_MAX = 16;
        private const int PAGES_PER_BUCKET = 6; // up to 96 chunks per Voronoi variant * 12 = 1152 concurrent chunks

        private static readonly int PropInstData = Shader.PropertyToID("_InstData");
        private static readonly int PropInstColor = Shader.PropertyToID("_InstColor");

        private struct DebrisItem
        {
            public bool Active;
            public byte MeshIndex;
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Vector3 AngularVelDeg;
            public float Scale;
            public float Age;
            public float Lifetime;
            public float InitialHeat;
            public float CutHeat;
            public Vector4 BaseColor;
        }

        private sealed class ChunkDrawBucket
        {
            public Mesh Mesh;
            public readonly Matrix4x4[][] Matrices = new Matrix4x4[PAGES_PER_BUCKET][];
            public readonly Vector4[][] InstData = new Vector4[PAGES_PER_BUCKET][];
            public readonly Vector4[][] InstColors = new Vector4[PAGES_PER_BUCKET][];
            public MaterialPropertyBlock Mpb;
            public int TotalCount;

            public ChunkDrawBucket()
            {
                for (int p = 0; p < PAGES_PER_BUCKET; p++)
                {
                    Matrices[p] = new Matrix4x4[BATCH_MAX];
                    InstData[p] = new Vector4[BATCH_MAX];
                    InstColors[p] = new Vector4[BATCH_MAX];
                }
            }
        }

        [SerializeField] private CrusherAssetRegistry _registry;

        private readonly DebrisItem[] _items = new DebrisItem[CAPACITY];
        private ChunkDrawBucket[] _buckets;
        private int _writeCursor;
        private FastRng _rng = new FastRng(0xD3B815C0FFEEUL);
        private bool _initialized;

        public void Initialize(CrusherAssetRegistry registry)
        {
            _registry = registry;
            if (_initialized || _registry == null) return;
            _initialized = true;

            int meshCount = _registry.MeshesDebrisChunks != null ? _registry.MeshesDebrisChunks.Length : 0;
            _buckets = new ChunkDrawBucket[meshCount];
            for (int i = 0; i < meshCount; i++)
            {
                _buckets[i] = new ChunkDrawBucket
                {
                    Mesh = _registry.MeshesDebrisChunks[i],
                    Mpb = new MaterialPropertyBlock(),
                    TotalCount = 0
                };
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < CAPACITY; i++)
                _items[i].Active = false;
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (!_initialized || _buckets == null || _buckets.Length == 0) return;

            // 1. Spawn Voronoi chunks from simulation KillEvents with incandescent 3500K sheared fracture faces
            if (sim != null && session != null && session.InRun)
            {
                for (int k = 0; k < sim.KillEventCount; k++)
                {
                    ref KillEvent ev = ref sim.KillEvents[k];
                    int count = GetChunkCountForEnemy(ev.Type);
                    Vector3 origin = new Vector3(
                        GameIdsAndScale.SimToWorld(ev.PosX),
                        0.48f,
                        GameIdsAndScale.SimToWorld(ev.PosY));
                    Vector4 tint = GetEnemyScrapColor(ev.Type, ev.KilledInGolden);
                    float initHeat = ev.KilledByOrbOrRay ? 2.15f : (ev.KilledInGolden ? 2.20f : 1.75f);
                    float baseScale = ev.Type == EnemyType.Boss ? 1.70f : (ev.Type == EnemyType.Tank ? 1.28f : 0.94f);

                    SpawnBurst(origin, count, tint, initHeat, baseScale);
                }
            }

            // 2. Step ballistic physics + cast-steel floor bounce + radiative T^4 thermal cooling
            float dt = Mathf.Clamp(unscaledDt, 0f, 0.05f);
            for (int b = 0; b < _buckets.Length; b++)
            {
                _buckets[b].TotalCount = 0;
            }

            const int maxPerBucket = BATCH_MAX * PAGES_PER_BUCKET;
            for (int i = 0; i < CAPACITY; i++)
            {
                if (!_items[i].Active) continue;
                ref DebrisItem d = ref _items[i];

                d.Age += dt;
                if (d.Age >= d.Lifetime)
                {
                    d.Active = false;
                    continue;
                }

                d.Velocity.y -= 18.8f * dt;
                d.Position += d.Velocity * dt;

                if (d.Position.y < 0.085f)
                {
                    d.Position.y = 0.085f;
                    d.Velocity.y = Mathf.Abs(d.Velocity.y) * 0.38f;
                    d.Velocity.x *= 0.74f;
                    d.Velocity.z *= 0.74f;
                    d.AngularVelDeg *= 0.68f;
                }

                d.Rotation *= Quaternion.Euler(d.AngularVelDeg * dt);

                // Radiative cooling: white-yellow 3500K flash decays quickly into lingering forge-orange -> dark steel
                float normAge = d.Age / d.Lifetime;
                float fastFlash = Mathf.Exp(-d.Age * 3.6f);
                float emberTail = Mathf.Max(0f, 1.0f - normAge * 1.15f);
                d.CutHeat = d.InitialHeat * (0.62f * fastFlash + 0.38f * emberTail * emberTail);

                float lifeRem = 1.0f - normAge;
                float shrink = lifeRem < 0.20f ? (lifeRem / 0.20f) : 1.0f;
                float s = d.Scale * shrink;

                int bIdx = d.MeshIndex % _buckets.Length;
                ChunkDrawBucket bucket = _buckets[bIdx];
                if (bucket.TotalCount < maxPerBucket && s > 0.02f)
                {
                    int idx = bucket.TotalCount++;
                    int page = idx / BATCH_MAX;
                    int slot = idx % BATCH_MAX;
                    bucket.Matrices[page][slot] = Matrix4x4.TRS(d.Position, d.Rotation, new Vector3(s, s, s));
                    bucket.InstData[page][slot] = new Vector4(0f, d.CutHeat, 0f, 0f);
                    bucket.InstColors[page][slot] = d.BaseColor;
                }
            }
        }

        public void SpawnBurst(Vector3 worldOrigin, int count, Vector4 baseColor, float initialHeat, float scaleMult)
        {
            if (!_initialized || _buckets == null || _buckets.Length == 0) return;

            Vector3 radial = worldOrigin;
            radial.y = 0f;
            Vector3 tangent = radial.sqrMagnitude > 0.01f
                ? new Vector3(-radial.z, 0f, radial.x).normalized
                : Vector3.right;
            Vector3 outward = radial.sqrMagnitude > 0.01f ? radial.normalized : Vector3.forward;

            for (int n = 0; n < count; n++)
            {
                int slot = _writeCursor;
                _writeCursor = (_writeCursor + 1) % CAPACITY;

                float randAng = _rng.NextFloat() * Mathf.PI * 2f;
                Vector3 spread = new Vector3(Mathf.Cos(randAng), 0f, Mathf.Sin(randAng));
                float speedOut = 2.5f + _rng.NextFloat() * 5.5f;
                float tangSign = (_rng.NextUInt() & 1u) == 0u ? 1f : -1f;
                Vector3 vel = outward * (speedOut * 0.58f)
                            + tangent * (tangSign * (2.6f + _rng.NextFloat() * 4.4f))
                            + spread * (1.8f + _rng.NextFloat() * 2.8f)
                            + Vector3.up * (3.4f + _rng.NextFloat() * 5.6f);

                float initH = initialHeat * (0.85f + _rng.NextFloat() * 0.25f);
                _items[slot] = new DebrisItem
                {
                    Active = true,
                    MeshIndex = (byte)(_rng.NextUInt() % (uint)_buckets.Length),
                    Position = worldOrigin + spread * 0.22f,
                    Velocity = vel,
                    Rotation = Quaternion.Euler(_rng.NextFloat() * 360f, _rng.NextFloat() * 360f, _rng.NextFloat() * 360f),
                    AngularVelDeg = new Vector3(
                        (_rng.NextFloat() - 0.5f) * 760f,
                        (_rng.NextFloat() - 0.5f) * 760f,
                        (_rng.NextFloat() - 0.5f) * 760f),
                    Scale = scaleMult * (0.78f + _rng.NextFloat() * 0.48f),
                    Age = 0f,
                    Lifetime = 1.40f + _rng.NextFloat() * 0.95f,
                    InitialHeat = initH,
                    CutHeat = initH,
                    BaseColor = baseColor
                };
            }
        }

        private static int GetChunkCountForEnemy(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.Boss:    return 32;
                case EnemyType.Tank:    return 14;
                case EnemyType.Vampire: return 12;
                case EnemyType.Ranged:  return 9;
                default:                return 7;
            }
        }

        private static Vector4 GetEnemyScrapColor(EnemyType t, bool golden)
        {
            if (golden)
            {
                return new Vector4(0.95f, 0.72f, 0.16f, 1f);
            }
            switch (t)
            {
                case EnemyType.Fast:    return new Vector4(0.86f, 0.58f, 0.10f, 1f);
                case EnemyType.Tank:    return new Vector4(0.24f, 0.28f, 0.35f, 1f);
                case EnemyType.Ranged:  return new Vector4(0.62f, 0.16f, 0.42f, 1f);
                case EnemyType.Vampire: return new Vector4(0.56f, 0.10f, 0.20f, 1f);
                case EnemyType.Boss:    return new Vector4(0.22f, 0.26f, 0.34f, 1f);
                default:                return new Vector4(0.16f, 0.44f, 0.58f, 1f);
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
            if (!_initialized || _buckets == null || _registry.MatDebrisInstanced == null) return;

            for (int b = 0; b < _buckets.Length; b++)
            {
                ChunkDrawBucket bucket = _buckets[b];
                if (bucket.TotalCount <= 0 || bucket.Mesh == null) continue;

                int remaining = bucket.TotalCount;
                for (int page = 0; page < PAGES_PER_BUCKET && remaining > 0; page++)
                {
                    int cnt = Mathf.Min(BATCH_MAX, remaining);
                    bucket.Mpb.SetVectorArray(PropInstData, bucket.InstData[page]);
                    bucket.Mpb.SetVectorArray(PropInstColor, bucket.InstColors[page]);
                    Graphics.DrawMeshInstanced(
                        bucket.Mesh,
                        0,
                        _registry.MatDebrisInstanced,
                        bucket.Matrices[page],
                        cnt,
                        bucket.Mpb,
                        ShadowCastingMode.On,
                        false,
                        0,
                        targetCamera);
                    remaining -= cnt;
                }
            }
        }
    }
}
