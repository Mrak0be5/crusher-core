using System;
using System.IO;
using Crusher.Audio;
using Crusher.Render;
using Crusher.Sim;
using Crusher.UI;
using UnityEngine;

namespace Crusher.Core
{
    /// <summary>
    /// Root scene coordinator for Crusher Core V1.
    /// Owns GameSession lifecycle, atomic SaveSystem persistence, 0.0s FTUE combat entry,
    /// deterministic Update ordering (Sim -> Audio/Camera/3D/VFX/UI -> ConsumeEvents), and OffscreenGate hooks.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private const float AUTO_SAVE_INTERVAL_SEC = 15.0f;

        public static Bootstrap Instance { get; private set; }

        [SerializeField] private CrusherAssetRegistry _registry;
        [SerializeField] private CameraRigTrauma _cameraRig;
        [SerializeField] private MeshRenderer _arenaFloorRenderer;
        [SerializeField] private CoreAnimatorView _coreView;
        [SerializeField] private EnemyRenderSystem _enemySystem;
        [SerializeField] private DebrisSimulationSystem _debrisSystem;
        [SerializeField] private WeldingSparksSystem _sparksSystem;
        [SerializeField] private GoldIngotFlowSystem _ingotSystem;
        [SerializeField] private ProceduralSfxPlayer _sfxPlayer;
        [SerializeField] private CrusherUiRoot _uiRoot;

        private float _autoSaveTimer;
        private bool _wasInRun;
        private bool _bootstrapped;

        public GameSession Session { get; private set; }
        public OfflineProgressSummary LastOfflineSummary { get; private set; }
        public CrusherAssetRegistry Registry => _registry;
        public CameraRigTrauma CameraRig => _cameraRig;
        public CoreAnimatorView CoreView => _coreView;
        public WeldingSparksSystem SparksSystem => _sparksSystem;
        public ProceduralSfxPlayer SfxPlayer => _sfxPlayer;
        public CrusherUiRoot UiRoot => _uiRoot;

        public string SaveFilePath => Path.Combine(Application.persistentDataPath, "save.json");

        public void BindSceneReferences(
            CrusherAssetRegistry registry,
            CameraRigTrauma cameraRig,
            MeshRenderer arenaFloorRenderer,
            CoreAnimatorView coreView,
            EnemyRenderSystem enemySystem,
            DebrisSimulationSystem debrisSystem,
            WeldingSparksSystem sparksSystem,
            GoldIngotFlowSystem ingotSystem,
            ProceduralSfxPlayer sfxPlayer,
            CrusherUiRoot uiRoot)
        {
            _registry = registry;
            _cameraRig = cameraRig;
            _arenaFloorRenderer = arenaFloorRenderer;
            _coreView = coreView;
            _enemySystem = enemySystem;
            _debrisSystem = debrisSystem;
            _sparksSystem = sparksSystem;
            _ingotSystem = ingotSystem;
            _sfxPlayer = sfxPlayer;
            _uiRoot = uiRoot;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (Application.isPlaying)
            {
                Application.targetFrameRate = 120;
                QualitySettings.vSyncCount = 0;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
        }

        private void Start()
        {
            if (!Application.isPlaying || _bootstrapped) return;

            ulong seed = (ulong)DateTime.UtcNow.Ticks ^ 0xC8054E8C0DEUL;
            var session = new GameSession(seed);

            if (SaveSystem.TryLoadAndApplyOffline(session, SaveFilePath, out OfflineProgressSummary offline))
            {
                LastOfflineSummary = offline;
            }

            // FTUE Hook: 0.0s immediate combat entry before Hangar Workshop is unlocked
            if (session.TotalRunsStarted == 0 || !session.WorkshopUnlocked)
            {
                session.StartNewRun(1);
            }

            InitializeRuntimeSystems(session);
            if (_uiRoot != null && LastOfflineSummary.ElapsedSeconds >= 5.0)
            {
                _uiRoot.ShowOfflineProgress(LastOfflineSummary);
            }
        }

        public void InitializeRuntimeSystems(GameSession session)
        {
            Session = session;
            _bootstrapped = true;

            Camera worldCam = _cameraRig != null ? _cameraRig.WorldCamera : Camera.main;

            if (_sfxPlayer != null) _sfxPlayer.Initialize(_registry);
            if (_coreView != null)
            {
                _coreView.Initialize(_registry, _arenaFloorRenderer);
                _coreView.ClearShockwaves();
            }
            if (_enemySystem != null) _enemySystem.Initialize(_registry);
            if (_debrisSystem != null)
            {
                _debrisSystem.Initialize(_registry);
                _debrisSystem.ClearAll();
            }
            if (_sparksSystem != null)
            {
                _sparksSystem.Initialize(_registry);
                _sparksSystem.ClearAll();
            }
            if (_ingotSystem != null)
            {
                _ingotSystem.Initialize(_registry);
                _ingotSystem.ClearAll();
            }
            if (_cameraRig != null && Session != null)
            {
                float rw = Config.GameIdsAndScale.SimToWorld(Session.ComputeCurrentStats().RangeSim);
                _cameraRig.ResetState(rw);
            }
            if (_uiRoot != null)
            {
                _uiRoot.BuildOrBind(_registry, Session, worldCam);
            }

            _wasInRun = Session != null && Session.InRun;
            _autoSaveTimer = AUTO_SAVE_INTERVAL_SEC;
        }

        private void Update()
        {
            if (!Application.isPlaying || !_bootstrapped || Session == null) return;

            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0005f, 0.1f);
            StepFrame(dt, tickSimulation: true);

            // Save immediately upon transitioning from active run to Hangar, plus periodic 15s autosave
            if (_wasInRun && !Session.InRun)
            {
                SaveNowSafe();
                _autoSaveTimer = AUTO_SAVE_INTERVAL_SEC;
            }
            _wasInRun = Session.InRun;

            _autoSaveTimer -= dt;
            if (_autoSaveTimer <= 0f)
            {
                _autoSaveTimer = AUTO_SAVE_INTERVAL_SEC;
                SaveNowSafe();
            }
        }

        public void StepFrame(float unscaledDt, bool tickSimulation)
        {
            if (Session == null) return;

            float dt = Mathf.Clamp(unscaledDt, 0.0005f, 0.1f);
            if (tickSimulation)
            {
                float simScale = _cameraRig != null ? _cameraRig.HitStopScale : 1.0f;
                Session.Tick(dt * simScale);
            }

            Camera worldCam = _cameraRig != null ? _cameraRig.WorldCamera : null;

            if (_sfxPlayer != null) _sfxPlayer.TickFromSim(Session.Combat, Session, dt);
            if (_cameraRig != null) _cameraRig.TickFromSim(Session.Combat, Session, dt);
            if (_coreView != null) _coreView.TickFromSim(Session.Combat, Session, worldCam, dt);
            if (_enemySystem != null) _enemySystem.TickFromSim(Session.Combat, Session, dt);
            if (_debrisSystem != null) _debrisSystem.TickFromSim(Session.Combat, Session, dt);
            if (_sparksSystem != null) _sparksSystem.TickFromSim(Session.Combat, Session, dt);
            if (_ingotSystem != null) _ingotSystem.TickFromSim(Session.Combat, Session, worldCam, dt);
            if (_uiRoot != null) _uiRoot.TickUI(Session, dt);

            Session.Combat.ConsumeRenderAndAudioEvents();
        }

        public void SubmitAllInstancedDraws(Camera targetCamera)
        {
            if (_enemySystem != null) _enemySystem.SubmitDrawCalls(targetCamera);
            if (_debrisSystem != null) _debrisSystem.SubmitDrawCalls(targetCamera);
            if (_sparksSystem != null) _sparksSystem.SubmitDrawCalls(targetCamera);
            if (_ingotSystem != null) _ingotSystem.SubmitDrawCalls(targetCamera);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveNowSafe();
            }
        }

        private void OnApplicationQuit()
        {
            SaveNowSafe();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SaveNowSafe()
        {
            if (!Application.isPlaying || Session == null) return;
            try
            {
                SaveSystem.SaveAtomic(Session, SaveFilePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Bootstrap] Atomic save warning: " + ex.Message);
            }
        }
    }
}
