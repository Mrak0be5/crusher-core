using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Zero-allocation 512-slot GPU-instanced SM_GoldIngot stream.
    /// Bullion ingots pop out of crushed enemies and arc smoothly toward the top Coin HUD counter.
    /// </summary>
    public sealed class GoldIngotFlowSystem : MonoBehaviour
    {
        public const int CAPACITY = 512;
        private const int BATCH_MAX = 16;
        private const int MAX_BATCHES = CAPACITY / BATCH_MAX;

        private static readonly int PropInstData = Shader.PropertyToID("_InstData");

        private struct IngotItem
        {
            public bool Active;
            public Vector3 StartPos;
            public Vector3 ApexOffset;
            public Quaternion Rotation;
            public Vector3 SpinDeg;
            public float Age;
            public float Duration;
            public float Scale;
            public float Glow;
        }

        private sealed class IngotBatch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BATCH_MAX];
            public readonly Vector4[] InstData = new Vector4[BATCH_MAX];
            public MaterialPropertyBlock Mpb;
            public int Count;
        }

        [SerializeField] private CrusherAssetRegistry _registry;

        private readonly IngotItem[] _items = new IngotItem[CAPACITY];
        private readonly IngotBatch[] _batches = new IngotBatch[MAX_BATCHES];
        private int _writeCursor;
        private FastRng _rng = new FastRng(0x601D1460777UL);
        private bool _initialized;

        public void Initialize(CrusherAssetRegistry registry)
        {
            _registry = registry;
            if (_initialized || _registry == null) return;
            _initialized = true;

            for (int b = 0; b < MAX_BATCHES; b++)
            {
                _batches[b] = new IngotBatch
                {
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

        public void TickFromSim(CombatSim sim, GameSession session, Camera worldCam, float unscaledDt)
        {
            if (!_initialized) return;

            float dt = Mathf.Clamp(unscaledDt, 0f, 0.05f);

            if (sim != null && session != null && session.InRun)
            {
                for (int k = 0; k < sim.KillEventCount; k++)
                {
                    ref KillEvent ev = ref sim.KillEvents[k];
                    if (ev.CoinsEarned <= 0.01) continue;

                    Vector3 origin = new Vector3(
                        GameIdsAndScale.SimToWorld(ev.PosX),
                        0.55f,
                        GameIdsAndScale.SimToWorld(ev.PosY));

                    int count = 1;
                    if (ev.Type == EnemyType.Boss) count = 12;
                    else if (ev.KilledInGolden) count = 4;
                    else if (ev.CoinsEarned >= 10.0) count = 2;

                    SpawnIngots(origin, count, ev.KilledInGolden ? 1.55f : 0.75f);
                }
            }

            Vector3 hudTarget = worldCam != null
                ? worldCam.ViewportToWorldPoint(new Vector3(0.36f, 0.93f, 16.0f))
                : new Vector3(-2.2f, 7.5f, 8.5f);

            for (int b = 0; b < MAX_BATCHES; b++)
            {
                _batches[b].Count = 0;
            }

            int activeBatch = 0;
            for (int i = 0; i < CAPACITY; i++)
            {
                if (!_items[i].Active) continue;
                ref IngotItem it = ref _items[i];

                it.Age += dt;
                if (it.Age >= it.Duration)
                {
                    it.Active = false;
                    continue;
                }

                float t = it.Age / it.Duration;
                // Cubic ease-in pull toward HUD after initial upward pop
                float pull = t * t * (3f - 2f * t);
                float arc = Mathf.Sin(t * Mathf.PI);
                Vector3 pos = Vector3.Lerp(it.StartPos, hudTarget, pull * pull) + it.ApexOffset * arc;

                it.Rotation *= Quaternion.Euler(it.SpinDeg * dt);
                float scale = it.Scale * (t > 0.85f ? (1f - t) / 0.15f : Mathf.Min(1f, t * 7f));

                if (activeBatch < MAX_BATCHES && scale > 0.02f)
                {
                    IngotBatch batch = _batches[activeBatch];
                    int slot = batch.Count++;
                    batch.Matrices[slot] = Matrix4x4.TRS(pos, it.Rotation, new Vector3(scale, scale, scale));
                    batch.InstData[slot] = new Vector4(0f, it.Glow * (1f - 0.5f * t), 0f, 0f);
                    if (batch.Count >= BATCH_MAX)
                    {
                        activeBatch++;
                    }
                }
            }
        }

        public void SpawnIngots(Vector3 worldOrigin, int count, float glow)
        {
            if (!_initialized) return;

            for (int n = 0; n < count; n++)
            {
                int slot = _writeCursor;
                _writeCursor = (_writeCursor + 1) % CAPACITY;

                float ang = _rng.NextFloat() * Mathf.PI * 2f;
                float spread = 0.65f + _rng.NextFloat() * 1.45f;
                Vector3 apex = new Vector3(
                    Mathf.Cos(ang) * spread,
                    1.6f + _rng.NextFloat() * 1.8f,
                    Mathf.Sin(ang) * spread);

                _items[slot] = new IngotItem
                {
                    Active = true,
                    StartPos = worldOrigin,
                    ApexOffset = apex,
                    Rotation = Quaternion.Euler(_rng.NextFloat() * 360f, _rng.NextFloat() * 360f, _rng.NextFloat() * 360f),
                    SpinDeg = new Vector3(240f + _rng.NextFloat() * 240f, 360f + _rng.NextFloat() * 320f, 180f),
                    Age = 0f,
                    Duration = 0.72f + _rng.NextFloat() * 0.28f,
                    Scale = 1.05f + _rng.NextFloat() * 0.30f,
                    Glow = glow
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
            if (!_initialized || _registry.MeshGoldIngot == null || _registry.MatGoldIngotInstanced == null) return;

            for (int b = 0; b < MAX_BATCHES; b++)
            {
                IngotBatch batch = _batches[b];
                if (batch.Count <= 0) continue;

                batch.Mpb.SetVectorArray(PropInstData, batch.InstData);
                Graphics.DrawMeshInstanced(
                    _registry.MeshGoldIngot,
                    0,
                    _registry.MatGoldIngotInstanced,
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
