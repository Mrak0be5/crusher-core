using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Zero-allocation 2048-slot physical Voronoi scrap debris simulator rendered via GPU instancing.
    /// Chunks inherit enemy armor tint via _InstColor and cool down glowing Voronoi fracture faces via _InstData.y.
    /// </summary>
    public sealed class DebrisSimulationSystem : MonoBehaviour
    {
        public const int CAPACITY = 2048;
        private const int BATCH_MAX = 16;

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
            public float CutHeat;
            public Vector4 BaseColor;
        }

        private sealed class ChunkDrawBucket
        {
            public Mesh Mesh;
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BATCH_MAX];
            public readonly Vector4[] InstData = new Vector4[BATCH_MAX];
            public readonly Vector4[] InstColors = new Vector4[BATCH_MAX];
            public MaterialPropertyBlock Mpb;
            public int Count;
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
                    Count = 0
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

            // 1. Spawn Voronoi chunks from simulation KillEvents
            if (sim != null && session != null && session.InRun)
            {
                for (int k = 0; k < sim.KillEventCount; k++)
                {
                    ref KillEvent ev = ref sim.KillEvents[k];
                    int count = GetChunkCountForEnemy(ev.Type);
                    Vector3 origin = new Vector3(
                        GameIdsAndScale.SimToWorld(ev.PosX),
                        0.45f,
                        GameIdsAndScale.SimToWorld(ev.PosY));
                    Vector4 tint = GetEnemyScrapColor(ev.Type, ev.KilledInGolden);
                    float initHeat = ev.KilledByOrbOrRay ? 1.85f : (ev.KilledInGolden ? 1.95f : 1.45f);
                    float baseScale = ev.Type == EnemyType.Boss ? 1.65f : (ev.Type == EnemyType.Tank ? 1.25f : 0.92f);

                    SpawnBurst(origin, count, tint, initHeat, baseScale);
                }
            }

            // 2. Step ballistic physics + floor bounce + cooling
            float dt = Mathf.Clamp(unscaledDt, 0f, 0.05f);
            for (int b = 0; b < _buckets.Length; b++)
            {
                _buckets[b].Count = 0;
            }

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

                d.Velocity.y -= 18.5f * dt;
                d.Position += d.Velocity * dt;

                if (d.Position.y < 0.09f)
                {
                    d.Position.y = 0.09f;
                    d.Velocity.y = Mathf.Abs(d.Velocity.y) * 0.36f;
                    d.Velocity.x *= 0.72f;
                    d.Velocity.z *= 0.72f;
                    d.AngularVelDeg *= 0.68f;
                }

                d.Rotation *= Quaternion.Euler(d.AngularVelDeg * dt);
                d.CutHeat = Mathf.Max(0f, d.CutHeat - dt * 1.15f);

                float lifeRem = 1.0f - (d.Age / d.Lifetime);
                float shrink = lifeRem < 0.22f ? (lifeRem / 0.22f) : 1.0f;
                float s = d.Scale * shrink;

                int bIdx = d.MeshIndex % _buckets.Length;
                ChunkDrawBucket bucket = _buckets[bIdx];
                if (bucket.Count < BATCH_MAX && s > 0.02f)
                {
                    int slot = bucket.Count++;
                    bucket.Matrices[slot] = Matrix4x4.TRS(d.Position, d.Rotation, new Vector3(s, s, s));
                    bucket.InstData[slot] = new Vector4(0f, d.CutHeat, 0f, 0f);
                    bucket.InstColors[slot] = d.BaseColor;
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
                float speedOut = 2.4f + _rng.NextFloat() * 5.2f;
                float tangSign = (_rng.NextUInt() & 1u) == 0u ? 1f : -1f;
                Vector3 vel = outward * (speedOut * 0.55f)
                            + tangent * (tangSign * (2.5f + _rng.NextFloat() * 4.2f))
                            + spread * (1.8f + _rng.NextFloat() * 2.6f)
                            + Vector3.up * (3.2f + _rng.NextFloat() * 5.4f);

                _items[slot] = new DebrisItem
                {
                    Active = true,
                    MeshIndex = (byte)(_rng.NextUInt() % (uint)_buckets.Length),
                    Position = worldOrigin + spread * 0.22f,
                    Velocity = vel,
                    Rotation = Quaternion.Euler(_rng.NextFloat() * 360f, _rng.NextFloat() * 360f, _rng.NextFloat() * 360f),
                    AngularVelDeg = new Vector3(
                        (_rng.NextFloat() - 0.5f) * 720f,
                        (_rng.NextFloat() - 0.5f) * 720f,
                        (_rng.NextFloat() - 0.5f) * 720f),
                    Scale = scaleMult * (0.78f + _rng.NextFloat() * 0.48f),
                    Age = 0f,
                    Lifetime = 1.25f + _rng.NextFloat() * 0.85f,
                    CutHeat = initialHeat,
                    BaseColor = baseColor
                };
            }
        }

        private static int GetChunkCountForEnemy(EnemyType t)
        {
            switch (t)
            {
                case EnemyType.Boss:    return 28;
                case EnemyType.Tank:    return 12;
                case EnemyType.Vampire: return 10;
                case EnemyType.Ranged:  return 8;
                default:                return 6;
            }
        }

        private static Vector4 GetEnemyScrapColor(EnemyType t, bool golden)
        {
            if (golden)
            {
                return new Vector4(0.95f, 0.72f, 0.18f, 1f);
            }
            switch (t)
            {
                case EnemyType.Fast:    return new Vector4(0.86f, 0.62f, 0.12f, 1f);
                case EnemyType.Tank:    return new Vector4(0.28f, 0.32f, 0.38f, 1f);
                case EnemyType.Ranged:  return new Vector4(0.68f, 0.20f, 0.42f, 1f);
                case EnemyType.Vampire: return new Vector4(0.56f, 0.14f, 0.26f, 1f);
                case EnemyType.Boss:    return new Vector4(0.72f, 0.76f, 0.84f, 1f);
                default:                return new Vector4(0.22f, 0.46f, 0.56f, 1f);
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
                if (bucket.Count <= 0 || bucket.Mesh == null) continue;

                bucket.Mpb.SetVectorArray(PropInstData, bucket.InstData);
                bucket.Mpb.SetVectorArray(PropInstColor, bucket.InstColors);
                Graphics.DrawMeshInstanced(
                    bucket.Mesh,
                    0,
                    _registry.MatDebrisInstanced,
                    bucket.Matrices,
                    bucket.Count,
                    bucket.Mpb,
                    ShadowCastingMode.On,
                    false,
                    0,
                    targetCamera);
            }
        }
    }
}
