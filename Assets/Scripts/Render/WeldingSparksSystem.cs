using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Crusher.Render
{
    /// <summary>
    /// Zero-allocation GPU-instanced VFX emitter (WebGL2-safe BATCH_MAX = 16):
    /// 1) 1536-slot velocity-stretched HDR welding sparks on crunches, plasma saw slices & kills.
    /// 2) 96-slot spinning hot brass cannon shells ejected sideways from the twin turret breeches on cannon fire!
    /// </summary>
    public sealed class WeldingSparksSystem : MonoBehaviour
    {
        public const int CAPACITY = 1536;
        public const int SHELL_CAPACITY = 96;
        private const int BATCH_MAX = 16;
        private const int MAX_BATCHES = CAPACITY / BATCH_MAX;
        private const int SHELL_BATCHES = SHELL_CAPACITY / BATCH_MAX;

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

        private struct BrassShellItem
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Vector3 SpinVelDeg;
            public float Age;
            public float Lifetime;
            public float Heat;
        }

        private sealed class SparkBatch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BATCH_MAX];
            public readonly Vector4[] InstData = new Vector4[BATCH_MAX];
            public readonly Vector4[] InstColors = new Vector4[BATCH_MAX];
            public MaterialPropertyBlock Mpb;
            public int Count;
        }

        private sealed class BrassShellBatch
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[BATCH_MAX];
            public readonly Vector4[] InstData = new Vector4[BATCH_MAX];
            public MaterialPropertyBlock Mpb;
            public int Count;
        }

        [SerializeField] private CrusherAssetRegistry _registry;

        private readonly SparkItem[] _items = new SparkItem[CAPACITY];
        private readonly SparkBatch[] _batches = new SparkBatch[MAX_BATCHES];

        private readonly BrassShellItem[] _shells = new BrassShellItem[SHELL_CAPACITY];
        private readonly BrassShellBatch[] _shellBatches = new BrassShellBatch[SHELL_BATCHES];

        private Mesh _sparkDiamondMesh;
        private Mesh _brassShellMesh;
        private int _writeCursor;
        private int _shellCursor;
        private bool _ejectRightNext;
        private float _grindEmitTimer;
        private FastRng _rng = new FastRng(0x5FA8C5771234UL);
        private bool _initialized;

        public void Initialize(CrusherAssetRegistry registry)
        {
            _registry = registry;
            if (_initialized || _registry == null) return;
            _initialized = true;

            _sparkDiamondMesh = BuildDiamondStreakMesh();
            _brassShellMesh = BuildBrassShellMesh();

            for (int b = 0; b < MAX_BATCHES; b++)
            {
                _batches[b] = new SparkBatch
                {
                    Mpb = new MaterialPropertyBlock(),
                    Count = 0
                };
            }

            for (int s = 0; s < SHELL_BATCHES; s++)
            {
                _shellBatches[s] = new BrassShellBatch
                {
                    Mpb = new MaterialPropertyBlock(),
                    Count = 0
                };
            }
        }

        private void OnDestroy()
        {
            if (_sparkDiamondMesh != null) DestroyImmediate(_sparkDiamondMesh);
            if (_brassShellMesh != null) DestroyImmediate(_brassShellMesh);
        }

        public void ClearAll()
        {
            for (int i = 0; i < CAPACITY; i++)
                _items[i].Active = false;
            for (int i = 0; i < SHELL_CAPACITY; i++)
                _shells[i].Active = false;
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

        /// <summary>
        /// Builds a crisp 8-sided flanged industrial cannon shell casing (aligned along local Z, radius ~0.07m, length ~0.34m)
        /// with COLOR_0 vertex zones (R=0 brass body, R=0.70 rim flange, R=1.0 hot neck, G=bevel wear, B=AO).
        /// </summary>
        private static Mesh BuildBrassShellMesh()
        {
            const int sides = 8;
            const int rings = 3; // base flange (-0.17), main waist (+0.08), hot neck (+0.17)
            float[] ringZ = { -0.17f, 0.08f, 0.17f };
            float[] ringR = {  0.082f, 0.068f, 0.054f };
            byte[]  zoneR = { 180, 0, 255 }; // rim flange=0.70, brass body=0.0, hot mouth=1.0

            int vertCount = sides * rings + 2; // +2 end caps
            var verts = new Vector3[vertCount];
            var norms = new Vector3[vertCount];
            var cols  = new Color32[vertCount];

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    float a = (s / (float)sides) * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a);
                    float sy = Mathf.Sin(a);
                    int idx = r * sides + s;
                    verts[idx] = new Vector3(cx * ringR[r], sy * ringR[r], ringZ[r]);
                    norms[idx] = new Vector3(cx, sy, r == 2 ? 0.2f : 0f).normalized;
                    cols[idx]  = new Color32(zoneR[r], (byte)(r != 1 ? 230 : 90), 235, 128);
                }
            }

            int backCap = sides * rings;
            int frontCap = backCap + 1;
            verts[backCap]  = new Vector3(0f, 0f, ringZ[0]);
            norms[backCap]  = Vector3.back;
            cols[backCap]   = new Color32(90, 180, 180, 128);

            verts[frontCap] = new Vector3(0f, 0f, ringZ[2] - 0.02f);
            norms[frontCap] = Vector3.forward;
            cols[frontCap]  = new Color32(255, 120, 80, 128); // dark hollow mouth + hot rim

            var tris = new int[(rings - 1) * sides * 6 + sides * 6];
            int t = 0;
            for (int r = 0; r < rings - 1; r++)
            {
                int row0 = r * sides;
                int row1 = (r + 1) * sides;
                for (int s = 0; s < sides; s++)
                {
                    int ns = (s + 1) % sides;
                    tris[t++] = row0 + s;
                    tris[t++] = row1 + s;
                    tris[t++] = row1 + ns;

                    tris[t++] = row0 + s;
                    tris[t++] = row1 + ns;
                    tris[t++] = row0 + ns;
                }
            }

            for (int s = 0; s < sides; s++)
            {
                int ns = (s + 1) % sides;
                // Back cap
                tris[t++] = backCap;
                tris[t++] = ns;
                tris[t++] = s;
                // Front cap
                int topRow = (rings - 1) * sides;
                tris[t++] = frontCap;
                tris[t++] = topRow + s;
                tris[t++] = topRow + ns;
            }

            var mesh = new Mesh { name = "ProcBrassCannonShell" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.colors32 = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f));
            return mesh;
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (!_initialized) return;

            float dt = Mathf.Clamp(unscaledDt, 0f, 0.05f);

            // 1. Emit spark bursts from kills, rotor grinding, AND eject spinning brass shells on cannon shots!
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
                        ? new Vector4(0.55f, 1.85f, 2.85f, 1f) // cyan plasma saw sparks
                        : (ev.KilledInGolden
                            ? new Vector4(2.65f, 1.80f, 0.35f, 1f) // molten gold sparks
                            : new Vector4(2.45f, 1.18f, 0.25f, 1f)); // hot carbide welding sparks

                    int burstCount = ev.Type == EnemyType.Boss ? 56 : (ev.KilledByOrbOrRay ? 20 : 15);
                    SpawnSparkBurst(origin, burstCount, col, ev.Type == EnemyType.Boss ? 1.50f : 1.0f);
                }

                // Eject hot spinning brass shells from twin cannon breeches on PneumoShot / PlasmaHit
                float aimYawRad = CoreAnimatorView.LatestAimYawRad;
                for (int s = 0; s < sim.SfxCount; s++)
                {
                    SfxId id = sim.SfxQueue[s];
                    if (id == SfxId.PneumoShot || id == SfxId.PlasmaHit)
                    {
                        SpawnEjectedBrassShell(aimYawRad, isHeavyPlasma: id == SfxId.PlasmaHit);
                    }
                }

                // Continuous grinding sparks on enemies pressed against rotor teeth
                _grindEmitTimer -= dt;
                if (_grindEmitTimer <= 0f)
                {
                    _grindEmitTimer = 0.042f;
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
                            SpawnSparkBurst(p, 5, new Vector4(2.40f, 1.28f, 0.30f, 1f), 0.88f);
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
                float len = Mathf.Clamp(0.16f + speed * 0.050f, 0.16f, 1.02f) * (0.4f + 0.6f * life01);
                float thick = sp.Thickness * life01;

                if (activeBatch < MAX_BATCHES)
                {
                    SparkBatch batch = _batches[activeBatch];
                    int slot = batch.Count++;
                    batch.Matrices[slot] = Matrix4x4.TRS(sp.Position, rot, new Vector3(thick, thick, len));
                    batch.InstData[slot] = new Vector4(0f, 0.40f + 1.95f * life01, 0f, 0f);
                    batch.InstColors[slot] = sp.Color;
                    if (batch.Count >= BATCH_MAX)
                    {
                        activeBatch++;
                    }
                }
            }

            // 3. Step spinning ejected brass cannon shells & pack GPU batches
            for (int sb = 0; sb < SHELL_BATCHES; sb++)
            {
                _shellBatches[sb].Count = 0;
            }

            int activeShellBatch = 0;
            for (int i = 0; i < SHELL_CAPACITY; i++)
            {
                if (!_shells[i].Active) continue;
                ref BrassShellItem sh = ref _shells[i];

                sh.Age += dt;
                if (sh.Age >= sh.Lifetime)
                {
                    sh.Active = false;
                    continue;
                }

                sh.Velocity.y -= 19.5f * dt;
                sh.Position += sh.Velocity * dt;

                if (sh.Position.y < 0.06f)
                {
                    sh.Position.y = 0.06f;
                    sh.Velocity.y = Mathf.Abs(sh.Velocity.y) * 0.44f;
                    sh.Velocity.x *= 0.72f;
                    sh.Velocity.z *= 0.72f;
                    sh.SpinVelDeg *= 0.65f;
                }

                sh.Rotation *= Quaternion.Euler(sh.SpinVelDeg * dt);
                sh.Heat = Mathf.Max(0f, sh.Heat - dt * 1.4f);

                float lifeRem = 1.0f - (sh.Age / sh.Lifetime);
                float scale = (lifeRem < 0.18f ? (lifeRem / 0.18f) : 1.0f) * 1.15f;

                if (activeShellBatch < SHELL_BATCHES && scale > 0.02f)
                {
                    BrassShellBatch batch = _shellBatches[activeShellBatch];
                    int slot = batch.Count++;
                    batch.Matrices[slot] = Matrix4x4.TRS(sh.Position, sh.Rotation, new Vector3(scale, scale, scale));
                    batch.InstData[slot] = new Vector4(0f, sh.Heat, 0f, 0f);
                    if (batch.Count >= BATCH_MAX)
                    {
                        activeShellBatch++;
                    }
                }
            }
        }

        public void SpawnEjectedBrassShell(float aimYawRad, bool isHeavyPlasma)
        {
            if (!_initialized) return;

            _ejectRightNext = !_ejectRightNext;
            float sideSign = _ejectRightNext ? 1f : -1f;

            Vector3 fwd = new Vector3(Mathf.Sin(aimYawRad), 0f, Mathf.Cos(aimYawRad));
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

            Vector3 breechPos = Vector3.up * 1.16f + right * (sideSign * 0.42f) - fwd * 0.12f;
            Vector3 ejectVel = right * (sideSign * (3.2f + _rng.NextFloat() * 1.8f))
                             - fwd * (0.9f + _rng.NextFloat() * 1.1f)
                             + Vector3.up * (4.2f + _rng.NextFloat() * 2.1f);

            int slot = _shellCursor;
            _shellCursor = (_shellCursor + 1) % SHELL_CAPACITY;

            _shells[slot] = new BrassShellItem
            {
                Active = true,
                Position = breechPos,
                Velocity = ejectVel,
                Rotation = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(_rng.NextFloat() * 45f, _rng.NextFloat() * 45f, 0f),
                SpinVelDeg = new Vector3(
                    520f + _rng.NextFloat() * 480f,
                    sideSign * (360f + _rng.NextFloat() * 360f),
                    280f + _rng.NextFloat() * 240f),
                Age = 0f,
                Lifetime = 1.35f + _rng.NextFloat() * 0.55f,
                Heat = isHeavyPlasma ? 1.85f : 1.15f
            };

            // Tiny breech spark puff alongside the ejected casing
            SpawnSparkBurst(breechPos, 3, new Vector4(2.50f, 1.55f, 0.35f, 1f), 0.52f);
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
                float spd = (5.5f + _rng.NextFloat() * 9.8f) * speedMult;
                float tangSign = (_rng.NextUInt() & 1u) == 0u ? 1f : -1f;

                _items[slot] = new SparkItem
                {
                    Active = true,
                    Position = worldOrigin,
                    Velocity = dir * spd + tangent * (tangSign * 3.5f),
                    Color = hdrColor,
                    Age = 0f,
                    Lifetime = 0.25f + _rng.NextFloat() * 0.35f,
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
            if (!_initialized) return;

            if (_sparkDiamondMesh != null && _registry.MatWeldingSparksInstanced != null)
            {
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

            Material shellMat = _registry.MatBrassShellInstanced != null
                ? _registry.MatBrassShellInstanced
                : _registry.MatGoldIngotInstanced;

            if (_brassShellMesh != null && shellMat != null)
            {
                for (int sb = 0; sb < SHELL_BATCHES; sb++)
                {
                    BrassShellBatch batch = _shellBatches[sb];
                    if (batch.Count <= 0) continue;

                    batch.Mpb.SetVectorArray(PropInstData, batch.InstData);
                    Graphics.DrawMeshInstanced(
                        _brassShellMesh,
                        0,
                        shellMat,
                        batch.Matrices,
                        batch.Count,
                        batch.Mpb,
                        ShadowCastingMode.On,
                        false,
                        0,
                        targetCamera);
                }
            }
        }
    }
}
