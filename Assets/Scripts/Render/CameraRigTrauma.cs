using Crusher.Config;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Crusher.Render
{
    /// <summary>
    /// Adaptive V2 Ultra Camera & Dynamic Foundry Lighting Rig:
    /// - Portrait Mobile (9:20, aspect <= 0.92): Pitch 62 deg, FOV 62 deg, centers rotor at viewport Y = 0.665 above 42% bottom deck.
    /// - Landscape WebGL/PC (16:9, aspect > 0.92): Pitch 58.5 deg, FOV 48 deg, centers rotor at viewport Y = 0.570 above 27% bottom deck,
    ///   framing both the combat perimeter and the surrounding SM_ArenaFoundryEnv scrap bunkers, cables & floodlight masts.
    /// - Interactive WebGL mouse cursor parallax (+/-2.5 deg) & scroll-wheel zoom (0.82x .. 1.22x).
    /// - Warm Foundry Pit light + 3 zero-alloc dynamic Point Lights (Muzzle Flash, Black Hole / Death Ray, Boss / Plasma Shockwave).
    /// </summary>
    public sealed class CameraRigTrauma : MonoBehaviour
    {
        public const float PITCH_DEGREES = 62.0f;
        public const float LANDSCAPE_PITCH_DEGREES = 58.5f;
        public const float VERTICAL_FOV_DEGREES = 62.0f;
        public const float LANDSCAPE_VERTICAL_FOV_DEGREES = 48.0f;
        public const float COMBAT_VIEWPORT_CENTER_Y = 0.665f;
        public const float LANDSCAPE_VIEWPORT_CENTER_Y = 0.570f;

        public const float ZOOM_MIN = 0.82f;
        public const float ZOOM_MAX = 1.22f;

        [SerializeField] private Camera _worldCamera;
        [SerializeField] private Light _keyLight;
        [SerializeField] private Light _rimLight;
        [SerializeField] private Light _foundryPitLight;
        [SerializeField] private Light _muzzleFlashLight;
        [SerializeField] private Light _fieldOrBeamLight;
        [SerializeField] private Light _explosionFlashLight;

        private float _currentRangeWorld = GameIdsAndScale.RANGE_0_WORLD;
        private float _trauma;
        private float _noiseClock;
        private float _hitStopRemaining;
        private bool _wasGoldenActive;
        private bool _wasGameOver;

        // Interactive WebGL parallax & zoom state
        private float _targetZoom = 1.0f;
        private float _currentZoom = 1.0f;
        private float _parallaxYawDeg;
        private float _parallaxPitchDeg;
        private bool _muzzleSideToggle;

        // Dynamic point-light envelopes
        private float _muzzleFlashTimer;
        private Color _muzzleFlashColor = new Color(1.0f, 0.72f, 0.28f);
        private Vector3 _muzzleFlashPos = new Vector3(0f, 1.05f, 0.8f);

        private float _explosionFlashTimer;
        private float _explosionFlashDuration = 0.35f;
        private float _explosionPeakIntensity = 8.0f;
        private Color _explosionFlashColor = new Color(1.0f, 0.65f, 0.22f);
        private Vector3 _explosionFlashPos = new Vector3(0f, 1.4f, 0f);

        public Camera WorldCamera => _worldCamera;
        public float HitStopScale => _hitStopRemaining > 0f ? 0.08f : 1.0f;
        public float CurrentZoom => _currentZoom;

        public void BindSceneObjects(Camera cam, Light keyLight, Light rimLight)
        {
            _worldCamera = cam;
            _keyLight = keyLight;
            _rimLight = rimLight;
            EnsureDynamicLightsExist();
            ApplyImmediateFraming(GameIdsAndScale.RANGE_0_WORLD);
        }

        public void BindSceneObjectsV2(
            Camera cam,
            Light keyLight,
            Light rimLight,
            Light foundryPitLight,
            Light muzzleFlashLight,
            Light fieldOrBeamLight,
            Light explosionFlashLight)
        {
            _worldCamera = cam;
            _keyLight = keyLight;
            _rimLight = rimLight;
            _foundryPitLight = foundryPitLight;
            _muzzleFlashLight = muzzleFlashLight;
            _fieldOrBeamLight = fieldOrBeamLight;
            _explosionFlashLight = explosionFlashLight;
            EnsureDynamicLightsExist();
            ApplyImmediateFraming(GameIdsAndScale.RANGE_0_WORLD);
        }

        private void EnsureDynamicLightsExist()
        {
            if (_foundryPitLight == null)
            {
                _foundryPitLight = CreatePointLight("FoundryPit_PointLight", new Vector3(0f, 1.25f, 0f), new Color(1.0f, 0.46f, 0.12f), 9.5f, 2.8f, true);
            }
            if (_muzzleFlashLight == null)
            {
                _muzzleFlashLight = CreatePointLight("Dynamic_MuzzleFlashLight", new Vector3(0f, 1.15f, 0.9f), new Color(1.0f, 0.74f, 0.30f), 7.5f, 0f, false);
            }
            if (_fieldOrBeamLight == null)
            {
                _fieldOrBeamLight = CreatePointLight("Dynamic_FieldOrBeamLight", new Vector3(0f, 1.35f, 0f), new Color(0.55f, 0.42f, 1.0f), 11.5f, 0f, false);
            }
            if (_explosionFlashLight == null)
            {
                _explosionFlashLight = CreatePointLight("Dynamic_ExplosionLight", new Vector3(0f, 1.50f, 0f), new Color(1.0f, 0.68f, 0.24f), 14.0f, 0f, false);
            }
        }

        private Light CreatePointLight(string objName, Vector3 pos, Color col, float range, float intensity, bool enabledInit)
        {
            Transform existing = transform.Find(objName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(objName);
            if (existing == null)
            {
                go.transform.SetParent(transform, false);
            }
            go.transform.position = pos;

            Light lt = go.GetComponent<Light>();
            if (lt == null) lt = go.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = col;
            lt.range = range;
            lt.intensity = intensity;
            lt.shadows = LightShadows.None;
            lt.enabled = enabledInit;

            if (go.GetComponent<UniversalAdditionalLightData>() == null)
            {
                go.AddComponent<UniversalAdditionalLightData>();
            }
            return lt;
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
            _targetZoom = 1.0f;
            _currentZoom = 1.0f;
            _parallaxYawDeg = 0f;
            _parallaxPitchDeg = 0f;
            _muzzleFlashTimer = 0f;
            _explosionFlashTimer = 0f;
            _currentRangeWorld = Mathf.Max(6.5f, rangeWorld);
            _wasGoldenActive = false;
            _wasGameOver = false;
            EnsureDynamicLightsExist();
            ApplyImmediateFraming(_currentRangeWorld);
        }

        public void ApplyImmediateFraming(float rangeWorld)
        {
            _currentRangeWorld = Mathf.Max(6.5f, rangeWorld);
            UpdateCameraTransform(0f, 0f);
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (_worldCamera == null) return;
            EnsureDynamicLightsExist();

            float dt = Mathf.Max(0.0005f, unscaledDt);
            _noiseClock += dt * 26.0f;
            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining = Mathf.Max(0f, _hitStopRemaining - dt);
            }

            if (_trauma > 0f)
            {
                _trauma = Mathf.Max(0f, _trauma - dt * 1.75f);
            }

            // Interactive mouse parallax & scroll-wheel zoom in PlayMode (kept neutral in headless OffscreenGate)
            UpdateInteractiveControls(dt);

            if (sim != null)
            {
                float targetRangeWorld = GameIdsAndScale.SimToWorld(Mathf.Max(GameIdsAndScale.RANGE_0_SIM, sim.Stats.RangeSim));
                _currentRangeWorld = Mathf.Lerp(_currentRangeWorld, targetRangeWorld, 1f - Mathf.Exp(-4.5f * dt));

                // 1. Kills -> trauma + hit-stop + Boss/Elite explosion flash light
                for (int i = 0; i < sim.KillEventCount; i++)
                {
                    ref KillEvent ev = ref sim.KillEvents[i];
                    Vector3 killPos = new Vector3(GameIdsAndScale.SimToWorld(ev.PosX), 1.35f, GameIdsAndScale.SimToWorld(ev.PosY));

                    if (ev.Type == EnemyType.Boss)
                    {
                        AddTrauma(0.75f);
                        TriggerHitStop(0.055f);
                        TriggerExplosionFlash(killPos, new Color(1.00f, 0.58f, 0.16f), 11.5f, 0.48f);
                    }
                    else if (ev.Type == EnemyType.Vampire || ev.Type == EnemyType.Tank)
                    {
                        AddTrauma(0.18f);
                        Color flashCol = ev.KilledByOrbOrRay
                            ? new Color(0.35f, 0.86f, 1.00f)
                            : new Color(1.00f, 0.52f, 0.18f);
                        TriggerExplosionFlash(killPos, flashCol, 6.8f, 0.24f);
                    }
                    else
                    {
                        AddTrauma(0.035f);
                    }
                }

                // 2. SfxQueue -> Cannon Muzzle Flash & Plasma Cannon / Chain Lightning bursts
                for (int s = 0; s < sim.SfxCount; s++)
                {
                    SfxId sfx = sim.SfxQueue[s];
                    if (sfx == SfxId.PneumoShot)
                    {
                        _muzzleSideToggle = !_muzzleSideToggle;
                        float sideX = _muzzleSideToggle ? -0.35f : 0.35f;
                        _muzzleFlashPos = new Vector3(sideX, 1.15f, 0.95f);
                        _muzzleFlashColor = new Color(1.00f, 0.72f, 0.26f);
                        _muzzleFlashTimer = 0.085f;
                    }
                    else if (sfx == SfxId.PlasmaHit)
                    {
                        _muzzleFlashPos = new Vector3(0f, 1.25f, 1.15f);
                        _muzzleFlashColor = new Color(0.28f, 0.88f, 1.00f);
                        _muzzleFlashTimer = 0.14f;
                        TriggerExplosionFlash(new Vector3(0f, 1.4f, 2.2f), new Color(0.30f, 0.90f, 1.00f), 8.5f, 0.28f);
                    }
                }

                if (sim.GoldenTower.Active && !_wasGoldenActive)
                {
                    AddTrauma(0.34f);
                    TriggerExplosionFlash(new Vector3(0f, 1.8f, 0f), new Color(1.00f, 0.78f, 0.20f), 10.0f, 0.42f);
                }
                _wasGoldenActive = sim.GoldenTower.Active;

                if (sim.IsGameOver && !_wasGameOver)
                {
                    AddTrauma(0.90f);
                    TriggerHitStop(0.085f);
                }
                _wasGameOver = sim.IsGameOver;

                // 3. Modulate Directional Studio Lights + Foundry Pit + Dynamic Action Point Lights
                UpdateDynamicLighting(sim, session, dt);
            }

            UpdateCameraTransform(_trauma * _trauma, dt);
        }

        private void TriggerExplosionFlash(Vector3 pos, Color col, float peakIntensity, float duration)
        {
            if (peakIntensity >= _explosionPeakIntensity * (_explosionFlashTimer / Mathf.Max(0.01f, _explosionFlashDuration)))
            {
                _explosionFlashPos = pos;
                _explosionFlashColor = col;
                _explosionPeakIntensity = peakIntensity;
                _explosionFlashDuration = Mathf.Max(0.05f, duration);
                _explosionFlashTimer = _explosionFlashDuration;
            }
        }

        private void UpdateDynamicLighting(CombatSim sim, GameSession session, float dt)
        {
            bool inRun = session != null && session.InRun;
            bool gtActive = inRun && sim.GoldenTower.Active;
            bool bhActive = inRun && sim.BlackHole.Active;
            bool drActive = inRun && sim.DeathRay.Equipped && sim.DeathRay.Active;

            if (_keyLight != null)
            {
                Color baseKey = new Color(1.00f, 0.98f, 0.94f);
                Color goldKey = new Color(1.00f, 0.88f, 0.64f);
                _keyLight.color = Color.Lerp(_keyLight.color, Color.Lerp(baseKey, goldKey, gtActive ? 0.50f : 0f), dt * 5f);
                float targetIntensity = gtActive ? 1.24f : 1.06f;
                _keyLight.intensity = Mathf.MoveTowards(_keyLight.intensity, targetIntensity, dt * 1.5f);
            }

            if (_rimLight != null)
            {
                Color baseRim = new Color(0.55f, 0.78f, 1.00f);
                Color bhRim = new Color(0.68f, 0.46f, 1.00f);
                _rimLight.color = Color.Lerp(_rimLight.color, bhActive ? bhRim : baseRim, dt * 5f);
                _rimLight.intensity = bhActive ? 0.82f : 0.58f;
            }

            // Warm central Foundry Smelter Pit light
            if (_foundryPitLight != null)
            {
                _foundryPitLight.enabled = true;
                float pulse = 0.88f + 0.12f * Mathf.Sin(_noiseClock * 0.22f);
                float basePit = inRun ? (gtActive ? 2.40f : 1.15f) : 0.95f;
                _foundryPitLight.intensity = basePit * pulse;
                _foundryPitLight.color = gtActive
                    ? new Color(1.00f, 0.72f, 0.18f)
                    : new Color(1.00f, 0.45f, 0.10f);
            }

            // Muzzle flash point light
            if (_muzzleFlashLight != null)
            {
                if (_muzzleFlashTimer > 0f)
                {
                    _muzzleFlashTimer = Mathf.Max(0f, _muzzleFlashTimer - dt);
                    _muzzleFlashLight.enabled = true;
                    _muzzleFlashLight.transform.position = _muzzleFlashPos;
                    _muzzleFlashLight.color = _muzzleFlashColor;
                    _muzzleFlashLight.intensity = (_muzzleFlashTimer / 0.085f) * 5.8f;
                }
                else
                {
                    _muzzleFlashLight.enabled = false;
                }
            }

            // Black Hole Vortex OR CNC Death Ray tracking point light
            if (_fieldOrBeamLight != null)
            {
                if (bhActive)
                {
                    _fieldOrBeamLight.enabled = true;
                    _fieldOrBeamLight.transform.position = new Vector3(
                        GameIdsAndScale.SimToWorld(sim.BlackHole.PosX),
                        1.45f,
                        GameIdsAndScale.SimToWorld(sim.BlackHole.PosY));
                    _fieldOrBeamLight.color = new Color(0.62f, 0.38f, 1.00f);
                    _fieldOrBeamLight.range = 12.0f;
                    _fieldOrBeamLight.intensity = 6.4f * Mathf.Clamp01(sim.BlackHole.TimeRemaining * 2.5f);
                }
                else if (drActive)
                {
                    _fieldOrBeamLight.enabled = true;
                    float rMid = _currentRangeWorld * 0.72f;
                    _fieldOrBeamLight.transform.position = new Vector3(
                        Mathf.Cos(sim.DeathRay.Angle) * rMid,
                        1.20f,
                        Mathf.Sin(sim.DeathRay.Angle) * rMid);
                    _fieldOrBeamLight.color = new Color(1.00f, 0.36f, 0.14f);
                    _fieldOrBeamLight.range = 11.0f;
                    _fieldOrBeamLight.intensity = 5.8f;
                }
                else
                {
                    _fieldOrBeamLight.enabled = false;
                }
            }

            // Boss Shatter / Plasma Cannon shockwave point light
            if (_explosionFlashLight != null)
            {
                if (_explosionFlashTimer > 0f)
                {
                    _explosionFlashTimer = Mathf.Max(0f, _explosionFlashTimer - dt);
                    float norm = _explosionFlashTimer / _explosionFlashDuration;
                    _explosionFlashLight.enabled = true;
                    _explosionFlashLight.transform.position = _explosionFlashPos;
                    _explosionFlashLight.color = _explosionFlashColor;
                    _explosionFlashLight.intensity = _explosionPeakIntensity * (norm * norm);
                }
                else
                {
                    _explosionFlashLight.enabled = false;
                }
            }
        }

        private void UpdateInteractiveControls(float dt)
        {
            if (!Application.isPlaying)
            {
                _currentZoom = 1.0f;
                _parallaxYawDeg = 0f;
                _parallaxPitchDeg = 0f;
                return;
            }

            // 1. Mouse scroll-wheel zoom (0.82x .. 1.22x distance factor; suppressed over UI ScrollRect/panels)
            float scrollY = Input.mouseScrollDelta.y;
            bool overUi = UnityEngine.EventSystems.EventSystem.current != null &&
                          UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (Mathf.Abs(scrollY) > 0.001f && !overUi)
            {
                _targetZoom = Mathf.Clamp(_targetZoom - scrollY * 0.065f, ZOOM_MIN, ZOOM_MAX);
            }
            _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, 1f - Mathf.Exp(-10.0f * dt));

            // 2. Smooth +/-2.5 deg 3D mouse cursor parallax on desktop/WebGL (disabled while touch is active)
            float targetYaw = 0f;
            float targetPitch = 0f;
            if (Input.touchCount == 0 && Screen.width > 64 && Screen.height > 64)
            {
                Vector3 mp = Input.mousePosition;
                if (mp.x >= 0f && mp.x <= Screen.width && mp.y >= 0f && mp.y <= Screen.height)
                {
                    float nx = Mathf.Clamp((mp.x / Screen.width) * 2.0f - 1.0f, -1.0f, 1.0f);
                    float ny = Mathf.Clamp((mp.y / Screen.height) * 2.0f - 1.0f, -1.0f, 1.0f);
                    targetYaw = nx * 2.5f;
                    targetPitch = -ny * 1.8f;
                }
            }

            float smooth = 1f - Mathf.Exp(-7.5f * dt);
            _parallaxYawDeg = Mathf.Lerp(_parallaxYawDeg, targetYaw, smooth);
            _parallaxPitchDeg = Mathf.Lerp(_parallaxPitchDeg, targetPitch, smooth);
        }

        private float GetEffectiveAspect()
        {
            if (_worldCamera.targetTexture != null && _worldCamera.targetTexture.height > 0)
            {
                return (float)_worldCamera.targetTexture.width / _worldCamera.targetTexture.height;
            }
            if (_worldCamera.pixelHeight > 0 && _worldCamera.pixelWidth > 0)
            {
                return (float)_worldCamera.pixelWidth / _worldCamera.pixelHeight;
            }
            return _worldCamera.aspect > 0.05f ? _worldCamera.aspect : (1080f / 2400f);
        }

        private void UpdateCameraTransform(float shakeSquared, float dt)
        {
            if (_worldCamera == null) return;

            float aspect = GetEffectiveAspect();
            bool isLandscape = aspect > 0.92f;

            float basePitch = isLandscape ? LANDSCAPE_PITCH_DEGREES : PITCH_DEGREES;
            float vertFov = isLandscape ? LANDSCAPE_VERTICAL_FOV_DEGREES : VERTICAL_FOV_DEGREES;
            float viewportCenterY = isLandscape ? LANDSCAPE_VIEWPORT_CENTER_Y : COMBAT_VIEWPORT_CENTER_Y;

            _worldCamera.fieldOfView = vertFov;
            float tanHalfV = Mathf.Tan(vertFov * 0.5f * Mathf.Deg2Rad);
            float tanHalfH = Mathf.Max(0.12f, aspect * tanHalfV);

            float depthD;
            if (isLandscape)
            {
                // On 16:9 WebGL, frame the combat ring cleanly above the 27% bottom deck while revealing
                // the surrounding SM_ArenaFoundryEnv scrap bunkers, armored cables & floodlight masts (r = 9m..15.8m)!
                float neededForRing = (_currentRangeWorld * 1.48f) / Mathf.Max(0.25f, tanHalfV);
                depthD = Mathf.Clamp(neededForRing, 26.5f, 40.0f) * _currentZoom;
            }
            else
            {
                // On 9:20 Mobile Portrait, fit perimeter ring + spawn margin inside horizontal width
                float desiredHalfWidthMeters = _currentRangeWorld * 1.15f;
                depthD = Mathf.Clamp(desiredHalfWidthMeters / tanHalfH, 24.0f, 44.0f) * _currentZoom;
            }

            Quaternion baseRot = Quaternion.Euler(basePitch + _parallaxPitchDeg, _parallaxYawDeg, 0f);
            Vector3 fwd = baseRot * Vector3.forward;
            Vector3 up = baseRot * Vector3.up;
            Vector3 right = baseRot * Vector3.right;

            float ndcY = viewportCenterY * 2.0f - 1.0f;
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
