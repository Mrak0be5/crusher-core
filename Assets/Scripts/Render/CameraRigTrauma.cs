using Crusher.Config;
using Crusher.Sim;
using UnityEngine;

namespace Crusher.Render
{
    /// <summary>
    /// Portrait 1080x2400 industrial arena camera rig (Pitch 62 deg, vertical FOV 62 deg -> ~30.3 deg horiz FOV).
    /// Analytically centers (0,0,0) at viewport Y = 0.665 so the full perimeter ring sits cleanly between
    /// the bottom 42% upgrade deck (Y=0.42) and top HUD bar (Y=0.90), with quadratic Perlin trauma shake & hit-stop.
    /// </summary>
    public sealed class CameraRigTrauma : MonoBehaviour
    {
        public const float PITCH_DEGREES = 62.0f;
        public const float VERTICAL_FOV_DEGREES = 62.0f;
        public const float COMBAT_VIEWPORT_CENTER_Y = 0.665f;

        [SerializeField] private Camera _worldCamera;
        [SerializeField] private Light _keyLight;
        [SerializeField] private Light _rimLight;

        private float _currentRangeWorld = GameIdsAndScale.RANGE_0_WORLD;
        private float _trauma;
        private float _noiseClock;
        private float _hitStopRemaining;
        private int _prevBossKills;
        private bool _wasGoldenActive;
        private bool _wasGameOver;

        public Camera WorldCamera => _worldCamera;
        public float HitStopScale => _hitStopRemaining > 0f ? 0.08f : 1.0f;

        public void BindSceneObjects(Camera cam, Light keyLight, Light rimLight)
        {
            _worldCamera = cam;
            _keyLight = keyLight;
            _rimLight = rimLight;
            ApplyImmediateFraming(GameIdsAndScale.RANGE_0_WORLD);
        }

        public void AddTrauma(float delta)
        {
            _trauma = Mathf.Clamp01(_trauma + delta);
        }

        public void TriggerHitStop(float seconds)
        {
            if (seconds > _hitStopRemaining)
            {
                _hitStopRemaining = seconds;
            }
        }

        public void ResetState(float rangeWorld)
        {
            _trauma = 0f;
            _hitStopRemaining = 0f;
            _currentRangeWorld = Mathf.Max(6.5f, rangeWorld);
            _wasGoldenActive = false;
            _wasGameOver = false;
            ApplyImmediateFraming(_currentRangeWorld);
        }

        public void ApplyImmediateFraming(float rangeWorld)
        {
            _currentRangeWorld = Mathf.Max(6.5f, rangeWorld);
            UpdateCameraTransform(0f);
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (_worldCamera == null) return;

            _noiseClock += unscaledDt * 26.0f;
            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining = Mathf.Max(0f, _hitStopRemaining - unscaledDt);
            }

            if (_trauma > 0f)
            {
                _trauma = Mathf.Max(0f, _trauma - unscaledDt * 1.75f);
            }

            if (sim != null)
            {
                float targetRangeWorld = GameIdsAndScale.SimToWorld(Mathf.Max(GameIdsAndScale.RANGE_0_SIM, sim.Stats.RangeSim));
                _currentRangeWorld = Mathf.Lerp(_currentRangeWorld, targetRangeWorld, 1f - Mathf.Exp(-4.5f * Mathf.Max(0.001f, unscaledDt)));

                // React to kills & ultimate activations with calibrated trauma impulses
                for (int i = 0; i < sim.KillEventCount; i++)
                {
                    EnemyType t = sim.KillEvents[i].Type;
                    if (t == EnemyType.Boss)
                    {
                        AddTrauma(0.72f);
                        TriggerHitStop(0.055f);
                    }
                    else if (t == EnemyType.Vampire || t == EnemyType.Tank)
                    {
                        AddTrauma(0.18f);
                    }
                    else
                    {
                        AddTrauma(0.035f);
                    }
                }

                if (sim.GoldenTower.Active && !_wasGoldenActive)
                {
                    AddTrauma(0.32f);
                }
                _wasGoldenActive = sim.GoldenTower.Active;

                if (sim.IsGameOver && !_wasGameOver)
                {
                    AddTrauma(0.90f);
                    TriggerHitStop(0.085f);
                }
                _wasGameOver = sim.IsGameOver;

                // Dynamic industrial studio lighting modulation
                if (_keyLight != null)
                {
                    Color baseKey = new Color(1.00f, 0.98f, 0.94f);
                    Color goldKey = new Color(1.00f, 0.88f, 0.66f);
                    float gtLerp = sim.GoldenTower.Active ? 1f : 0f;
                    _keyLight.color = Color.Lerp(_keyLight.color, Color.Lerp(baseKey, goldKey, gtLerp * 0.55f), unscaledDt * 5f);
                    float targetIntensity = sim.GoldenTower.Active ? 1.55f : 1.32f;
                    _keyLight.intensity = Mathf.MoveTowards(_keyLight.intensity, targetIntensity, unscaledDt * 1.5f);
                }

                if (_rimLight != null)
                {
                    Color baseRim = new Color(0.55f, 0.78f, 1.00f);
                    Color bhRim = new Color(0.68f, 0.48f, 1.00f);
                    _rimLight.color = Color.Lerp(_rimLight.color, sim.BlackHole.Active ? bhRim : baseRim, unscaledDt * 5f);
                    _rimLight.intensity = sim.BlackHole.Active ? 1.05f : 0.78f;
                }
            }

            UpdateCameraTransform(_trauma * _trauma);
        }

        private void UpdateCameraTransform(float shakeSquared)
        {
            if (_worldCamera == null) return;

            _worldCamera.fieldOfView = VERTICAL_FOV_DEGREES;
            float aspect = _worldCamera.aspect > 0.05f ? _worldCamera.aspect : (1080f / 2400f);
            float tanHalfV = Mathf.Tan(VERTICAL_FOV_DEGREES * 0.5f * Mathf.Deg2Rad);
            float tanHalfH = Mathf.Max(0.12f, aspect * tanHalfV);

            // Fit perimeter ring + spawn margin inside horizontal width
            float desiredHalfWidthMeters = _currentRangeWorld * 1.15f;
            float depthD = Mathf.Clamp(desiredHalfWidthMeters / tanHalfH, 24.0f, 44.0f);

            Quaternion baseRot = Quaternion.Euler(PITCH_DEGREES, 0f, 0f);
            Vector3 fwd = baseRot * Vector3.forward;
            Vector3 up = baseRot * Vector3.up;
            Vector3 right = Vector3.right;

            float ndcY = COMBAT_VIEWPORT_CENTER_Y * 2.0f - 1.0f; // +0.33 for viewport Y = 0.665
            float camSpaceOriginY = ndcY * depthD * tanHalfV;

            Vector3 pos = -fwd * depthD - up * camSpaceOriginY;
            Quaternion rot = baseRot;

            if (shakeSquared > 0.0001f)
            {
                float nx = Mathf.PerlinNoise(_noiseClock, 1.37f) * 2f - 1f;
                float ny = Mathf.PerlinNoise(4.91f, _noiseClock) * 2f - 1f;
                float nr = Mathf.PerlinNoise(_noiseClock * 0.85f, 9.13f) * 2f - 1f;

                pos += (right * nx + up * ny) * (0.24f * shakeSquared);
                rot = baseRot * Quaternion.Euler(ny * 1.15f * shakeSquared, nx * 1.15f * shakeSquared, nr * 1.65f * shakeSquared);
            }

            _worldCamera.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
