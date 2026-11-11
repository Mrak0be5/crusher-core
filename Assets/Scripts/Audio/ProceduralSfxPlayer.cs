using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using UnityEngine;

namespace Crusher.Audio
{
    /// <summary>
    /// Zero-allocation 12-voice procedural industrial ASMR mixer + 2 seamless loops + trauma ducking.
    /// </summary>
    public sealed class ProceduralSfxPlayer : MonoBehaviour
    {
        public const int VOICE_POOL_SIZE = 12;

        public static ProceduralSfxPlayer Instance { get; private set; }

        [SerializeField] private CrusherAssetRegistry _registry;

        private readonly AudioSource[] _voices = new AudioSource[VOICE_POOL_SIZE];
        private AudioSource _rotorLoopSource;
        private AudioSource _blackHoleLoopSource;

        private readonly float[] _lastPlayedTime = new float[GameIdsAndScale.SFX_COUNT];
        private int _voiceCursor;
        private int _crunchVariantCursor;
        private int _orbVariantCursor;
        private int _ingotComboIndex;
        private float _ingotComboResetTimer;
        private float _duckEnvelope; // 0 = normal, 1 = max ducking
        private float _clock;
        private bool _initialized;

        public bool IsMuted { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (_registry != null)
            {
                Initialize(_registry);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Initialize(CrusherAssetRegistry registry)
        {
            _registry = registry;
            if (_initialized || _registry == null) return;
            _initialized = true;

            for (int i = 0; i < VOICE_POOL_SIZE; i++)
            {
                var go = new GameObject("SfxVoice_" + i.ToString("D2"));
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                _voices[i] = src;
            }

            _rotorLoopSource = CreateLoopSource("Loop_RotorHum", _registry.ClipRotorHumLoop, 0.22f);
            _blackHoleLoopSource = CreateLoopSource("Loop_BlackHole", _registry.ClipBlackHoleLoop, 0f);
            ApplyMuteStateToSources();
        }

        public void ToggleMute()
        {
            SetMuted(!IsMuted);
        }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
            AudioListener.volume = muted ? 0f : 1f;
            ApplyMuteStateToSources();
        }

        private void ApplyMuteStateToSources()
        {
            for (int i = 0; i < VOICE_POOL_SIZE; i++)
            {
                if (_voices[i] != null)
                {
                    _voices[i].mute = IsMuted;
                }
            }
            if (_rotorLoopSource != null) _rotorLoopSource.mute = IsMuted;
            if (_blackHoleLoopSource != null) _blackHoleLoopSource.mute = IsMuted;
        }

        private AudioSource CreateLoopSource(string objName, AudioClip clip, float initialVol)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.clip = clip;
            src.volume = initialVol;
            if (clip != null)
            {
                src.Play();
            }
            return src;
        }

        public void PlayUI(SfxId id)
        {
            if (!_initialized) return;

            switch (id)
            {
                case SfxId.UiTap:
                    PlayVoice(_registry.ClipUiTap, 0.52f, 0.98f + NextJitter(0.05f), ignoreDuck: true);
                    break;
                case SfxId.UiUpgrade:
                    PlayVoice(_registry.ClipUiUpgrade, 0.65f, 0.97f + NextJitter(0.08f), ignoreDuck: true);
                    break;
                case SfxId.UiUnlock:
                    _duckEnvelope = Mathf.Max(_duckEnvelope, 0.55f);
                    PlayVoice(_registry.ClipUiUnlock, 0.80f, 1.0f, ignoreDuck: true);
                    break;
                default:
                    TriggerSimSfx(id);
                    break;
            }
        }

        public void TickFromSim(CombatSim sim, GameSession session, float unscaledDt)
        {
            if (!_initialized || sim == null || session == null) return;

            _clock += Mathf.Max(0f, unscaledDt);
            if (_ingotComboResetTimer > 0f)
            {
                _ingotComboResetTimer -= unscaledDt;
                if (_ingotComboResetTimer <= 0f)
                {
                    _ingotComboIndex = 0;
                }
            }

            if (_duckEnvelope > 0f)
            {
                _duckEnvelope = Mathf.Max(0f, _duckEnvelope - unscaledDt * 2.2f);
            }

            // 1. Dispatch queued simulation SFX with anti-clipping throttling
            int count = sim.SfxCount;
            for (int i = 0; i < count; i++)
            {
                TriggerSimSfx(sim.SfxQueue[i]);
            }

            // Emit subtle metal crunch on projectile kills if Crunch wasn't already triggered this frame
            if (sim.KillEventCount > 0 && (_clock - _lastPlayedTime[(int)SfxId.Crunch]) >= 0.048f)
            {
                TriggerSimSfx(SfxId.Crunch);
            }

            // 2. Modulate continuous industrial loops
            float duckMult = Mathf.Lerp(1.0f, 0.40f, _duckEnvelope);
            if (_rotorLoopSource != null && _rotorLoopSource.clip != null)
            {
                float targetVol = session.InRun
                    ? (sim.GoldenTower.Active ? 0.54f : 0.38f)
                    : 0.16f;
                float speedScale = session.InRun ? session.EffectiveTimeScale : 1.0f;
                float targetPitch = 0.88f + 0.11f * Mathf.Clamp(speedScale - 1.0f, 0f, 4.0f) + (sim.GoldenTower.Active ? 0.10f : 0f);

                _rotorLoopSource.volume = Mathf.MoveTowards(_rotorLoopSource.volume, targetVol * duckMult, unscaledDt * 1.8f);
                _rotorLoopSource.pitch = Mathf.MoveTowards(_rotorLoopSource.pitch, targetPitch, unscaledDt * 2.0f);
                if (!_rotorLoopSource.isPlaying) _rotorLoopSource.Play();
            }

            if (_blackHoleLoopSource != null && _blackHoleLoopSource.clip != null)
            {
                bool bhActive = session.InRun && sim.BlackHole.Active;
                float bhTargetVol = bhActive ? 0.58f * duckMult : 0f;
                _blackHoleLoopSource.volume = Mathf.MoveTowards(_blackHoleLoopSource.volume, bhTargetVol, unscaledDt * 2.8f);
                _blackHoleLoopSource.pitch = bhActive ? 0.96f + 0.08f * Mathf.Sin(_clock * 4.0f) : 1.0f;
                if (bhActive && !_blackHoleLoopSource.isPlaying) _blackHoleLoopSource.Play();
            }
        }

        private void TriggerSimSfx(SfxId id)
        {
            int idx = (int)id;
            if ((uint)idx >= (uint)_lastPlayedTime.Length) return;

            float minGap = GetMinThrottleGap(id);
            if (_clock - _lastPlayedTime[idx] < minGap)
            {
                return;
            }
            _lastPlayedTime[idx] = _clock;

            switch (id)
            {
                case SfxId.Crunch:
                    if (_registry.ClipsCrunch != null && _registry.ClipsCrunch.Length > 0)
                    {
                        AudioClip clip = _registry.ClipsCrunch[_crunchVariantCursor % _registry.ClipsCrunch.Length];
                        _crunchVariantCursor++;
                        PlayVoice(clip, 0.52f, 0.88f + NextJitter(0.24f), ignoreDuck: false);
                    }
                    break;

                case SfxId.BossShatter:
                    _duckEnvelope = 1.0f;
                    PlayVoice(_registry.ClipBossShatter, 0.95f, 0.96f + NextJitter(0.08f), ignoreDuck: true);
                    break;

                case SfxId.OrbSaw:
                    if (_registry.ClipsOrbSaw != null && _registry.ClipsOrbSaw.Length > 0)
                    {
                        AudioClip clip = _registry.ClipsOrbSaw[_orbVariantCursor % _registry.ClipsOrbSaw.Length];
                        _orbVariantCursor++;
                        PlayVoice(clip, 0.44f, 0.94f + NextJitter(0.16f), ignoreDuck: false);
                    }
                    break;

                case SfxId.PneumoShot:
                    PlayVoice(_registry.ClipPneumoShot, 0.30f, 0.93f + NextJitter(0.14f), ignoreDuck: false);
                    break;

                case SfxId.GoldenStart:
                    _duckEnvelope = Mathf.Max(_duckEnvelope, 0.85f);
                    PlayVoice(_registry.ClipGoldenStart, 0.88f, 1.0f, ignoreDuck: true);
                    break;

                case SfxId.IngotDing:
                    if (_registry.ClipsIngotDing != null && _registry.ClipsIngotDing.Length > 0)
                    {
                        AudioClip clip = _registry.ClipsIngotDing[_ingotComboIndex % _registry.ClipsIngotDing.Length];
                        _ingotComboIndex = (_ingotComboIndex + 1) % _registry.ClipsIngotDing.Length;
                        _ingotComboResetTimer = 0.85f;
                        PlayVoice(clip, 0.50f, 0.98f + NextJitter(0.04f), ignoreDuck: false);
                    }
                    break;

                case SfxId.ChainZap:
                    PlayVoice(_registry.ClipChainZap, 0.48f, 0.92f + NextJitter(0.18f), ignoreDuck: false);
                    break;

                case SfxId.PlasmaHit:
                    _duckEnvelope = Mathf.Max(_duckEnvelope, 0.45f);
                    PlayVoice(_registry.ClipPlasmaHit, 0.78f, 0.96f + NextJitter(0.08f), ignoreDuck: true);
                    break;

                case SfxId.OverloadDeath:
                    _duckEnvelope = 1.0f;
                    PlayVoice(_registry.ClipOverloadDeath, 0.95f, 1.0f, ignoreDuck: true);
                    break;
            }
        }

        private static float GetMinThrottleGap(SfxId id)
        {
            switch (id)
            {
                case SfxId.PneumoShot: return 0.055f;
                case SfxId.Crunch:     return 0.034f;
                case SfxId.OrbSaw:     return 0.042f;
                case SfxId.IngotDing:  return 0.045f;
                case SfxId.ChainZap:   return 0.048f;
                default:               return 0.020f;
            }
        }

        private void PlayVoice(AudioClip clip, float volume, float pitch, bool ignoreDuck)
        {
            if (IsMuted || clip == null) return;
            AudioSource src = _voices[_voiceCursor];
            _voiceCursor = (_voiceCursor + 1) % VOICE_POOL_SIZE;
            if (src == null) return;

            float gain = ignoreDuck ? volume : volume * Mathf.Lerp(1.0f, 0.45f, _duckEnvelope);
            src.pitch = Mathf.Clamp(pitch, 0.5f, 2.0f);
            src.volume = Mathf.Clamp01(gain);
            src.clip = clip;
            src.Play();
        }

        private float NextJitter(float span)
        {
            // Fast deterministic hash jitter without UnityEngine.Random state side-effects
            float h = Mathf.Sin((_voiceCursor * 17.13f + _clock * 91.7f)) * 43758.5453f;
            float frac = h - Mathf.Floor(h);
            return (frac - 0.5f) * span;
        }
    }
}
