using Crusher.Core;
using Crusher.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Crusher.UI
{
    /// <summary>
    /// Root portrait UI orchestrator (1080x2400 CanvasScaler, matchWidthOrHeight = 0.5)
    /// implementing the exact Wave 2 inter-module contract:
    /// - MainCanvas
    /// - BuildOrBind(CrusherAssetRegistry registry, GameSession session, Camera worldCam)
    /// - TickUI(GameSession session, float unscaledDt)
    /// - SetOffscreenTargetCamera(Camera uiCam)
    /// </summary>
    public sealed class CrusherUiRoot : MonoBehaviour
    {
        public const int ReferenceWidth = 1080;
        public const int ReferenceHeight = 2400;
        public const int LandscapeReferenceWidth = 1920;
        public const int LandscapeReferenceHeight = 1080;

        public Canvas MainCanvas { get; private set; }
        public HudScreen Hud => _hud;
        public HangarScreen Hangar => _hangar;
        public bool IsLandscape => _isLandscape;

        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private RectTransform _fullScreenArenaRect;
        private RectTransform _safeAreaRect;

        private UiStyle _style;
        private TweenRunner _tweens;
        private HudScreen _hud;
        private HangarScreen _hangar;

        private CrusherAssetRegistry _registry;
        private GameSession _session;
        private Camera _worldCam;

        private bool _isBuilt;
        private bool _offscreenMode;
        private bool _isLandscape;
        private bool _orientationInitialized;
        private bool _wasInRun;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        public void BuildOrBind(CrusherAssetRegistry registry, GameSession session, Camera worldCam)
        {
            _session = session;
            if (worldCam != null)
            {
                _worldCam = worldCam;
            }

            // Ensure fallback symbols font is linked to MainFont if not already present
            if (registry != null && registry.MainFont != null && registry.SymbolsFallbackFont != null)
            {
                if (registry.MainFont.fallbackFontAssetTable == null)
                {
                    registry.MainFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMPro.TMP_FontAsset>(1);
                }
                if (!registry.MainFont.fallbackFontAssetTable.Contains(registry.SymbolsFallbackFont))
                {
                    registry.MainFont.fallbackFontAssetTable.Add(registry.SymbolsFallbackFont);
                }
            }

            // Fast idempotent rebind when switching sessions/frames (e.g. OffscreenGate 9-frame capture)
            if (_isBuilt && MainCanvas != null && _hud != null && _hangar != null && ReferenceEquals(_registry, registry))
            {
                if (_worldCam != null)
                {
                    _hud.SetWorldCamera(_worldCam);
                }
                EvaluateAndApplyOrientation(force: false);
                SyncActiveScreenImmediate(session);
                if (session != null)
                {
                    TickUI(session, 0.016f);
                }
                return;
            }

            _registry = registry;
            _orientationInitialized = false;
            CleanupStaleChildren();
            EnsureEventSystemExists();

            // 1. Adaptive Root Canvas (1080x2400 Portrait / 1920x1080 Landscape, matchWidthOrHeight = 0.5)
            var canvasGo = new GameObject("CrusherMainCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            MainCanvas = canvasGo.GetComponent<Canvas>();
            MainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            MainCanvas.sortingOrder = 50;
            MainCanvas.pixelPerfect = false;

            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;
            _scaler.referencePixelsPerUnit = 100f;

            _raycaster = canvasGo.GetComponent<GraphicRaycaster>();
            _raycaster.ignoreReversedGraphics = true;

            // 2. FullScreenArenaOverlay (outside SafeAreaRoot!) + SafeAreaRoot
            _fullScreenArenaRect = UiFactory.CreateStretchRect("FullScreenArenaOverlay", canvasGo.transform);
            _safeAreaRect = UiFactory.CreateStretchRect("SafeAreaRoot", canvasGo.transform);
            _lastSafeArea = Rect.zero;
            _lastScreenSize = Vector2Int.zero;

            // 3. Shared ProceduralSDFUI Material Style & Zero-Alloc Tween Pool
            _style = new UiStyle(registry);
            _tweens = new TweenRunner();
            UiFactory.SetGlobalTweenRunner(_tweens);

            // 4. Build Combat HUD + Industrial Hangar + Shared UW Choice Modal
            _hud = new HudScreen();
            _hud.Build(
                _fullScreenArenaRect,
                _safeAreaRect,
                MainCanvas,
                _style,
                _tweens,
                _worldCam,
                tabIdx => OpenHangar(tabIdx));

            _hangar = new HangarScreen();
            _hangar.Build(
                _safeAreaRect,
                _style,
                _tweens,
                OnStartRunFromHangar,
                () =>
                {
                    if (_session != null)
                    {
                        _hud.BringSharedModalsToFront();
                        _hud.OpenUwModal(_session);
                    }
                });

            // Keep shared 3-choice UW modal rendered above both HUD and Hangar
            _hud.BringSharedModalsToFront();

            _isBuilt = true;
            EvaluateAndApplyOrientation(force: true);
            SyncActiveScreenImmediate(session);

            if (session != null)
            {
                TickUI(session, 0.016f);
            }
        }

        public void TickUI(GameSession session, float unscaledDt)
        {
            if (!_isBuilt || session == null) return;
            _session = session;

            float dt = Mathf.Clamp(unscaledDt, 0f, 0.25f);

            EvaluateAndApplyOrientation(force: false);

            if (!_offscreenMode)
            {
                UpdateSafeAreaIfNeeded();
                if (Application.isPlaying)
                {
                    ProcessHotkeys(session);
                }
            }

            // Detect natural live-run GameOver transition -> open Run Summary modal
            if (_wasInRun && !session.InRun && session.Combat.IsGameOver)
            {
                _wasInRun = false;
                _hangar.SetVisible(false);
                _hud.SetVisible(true);
                _hud.OpenSummaryForLastRun(session);
            }
            else if (session.InRun)
            {
                _wasInRun = true;
                if (_hangar.IsVisible)
                {
                    _hangar.SetVisible(false);
                }
                if (_hud.IsSummaryModalOpen)
                {
                    _hud.DismissModals();
                }
                _hud.SetVisible(true);
            }
            else if (!_hud.IsSummaryModalOpen)
            {
                _wasInRun = false;
                if (!_hangar.IsVisible)
                {
                    _hud.SetVisible(false);
                    if (session.CardsUnlocked && session.FtueDefenseArrowCompleted && _hangar.ActiveTab == 0)
                    {
                        // Showcase Cards + Meta Strip (Cards/Labs/UWs) when entering mid/late-game Hangar
                        _hangar.SetActiveTab(1);
                    }
                    _hangar.SetVisible(true, session);
                }
            }

            _tweens.Tick(dt);

            if (session.InRun || _hud.IsSummaryModalOpen)
            {
                _hud.Tick(session, dt);
            }
            else
            {
                _hangar.Tick(session, dt);
            }

            if (_offscreenMode)
            {
                Canvas.ForceUpdateCanvases();
            }
        }

        /// <summary>
        /// Seam method for OffscreenGate RenderTexture capture:
        /// Switches MainCanvas to ScreenSpaceCamera at planeDistance = 1f when uiCam != null,
        /// bypassing headless Screen.safeArea distortion; restores ScreenSpaceOverlay when uiCam == null.
        /// </summary>
        public void SetOffscreenTargetCamera(Camera uiCam)
        {
            if (MainCanvas == null) return;

            if (uiCam != null)
            {
                _offscreenMode = true;
                MainCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                MainCanvas.worldCamera = uiCam;
                MainCanvas.planeDistance = 1f;

                if (_safeAreaRect != null)
                {
                    _safeAreaRect.anchorMin = Vector2.zero;
                    _safeAreaRect.anchorMax = Vector2.one;
                    _safeAreaRect.offsetMin = Vector2.zero;
                    _safeAreaRect.offsetMax = Vector2.zero;
                }

                // Keep _worldCam bound to the 3D Arena Camera so world->overlay projections stay 1:1 concentric!
                EvaluateAndApplyOrientation(force: false);
                Canvas.ForceUpdateCanvases();
            }
            else
            {
                _offscreenMode = false;
                MainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                MainCanvas.worldCamera = null;
                _lastSafeArea = Rect.zero;
                EvaluateAndApplyOrientation(force: false);
                UpdateSafeAreaIfNeeded();
            }
        }

        public void OpenHangar(int tabIndex = 0)
        {
            if (!_isBuilt) return;
            _wasInRun = false;
            _hud.DismissModals();
            _hud.SetVisible(false);
            _hangar.SetActiveTab(tabIndex);
            _hangar.SetVisible(true, _session);
        }

        public void SetHangarTab(int tabIndex)
        {
            if (_hangar != null)
            {
                _hangar.SetActiveTab(tabIndex);
            }
        }

        public void ShowOfflineProgress(OfflineProgressSummary summary)
        {
            if (!_isBuilt || summary.ElapsedSeconds < 5.0) return;

            if (_session != null && _session.InRun)
            {
                if (summary.LabsCompleted > 0)
                {
                    _hud.ShowToast("★ ОФФЛАЙН-ЦЕХ: ЗАВЕРШЕНО ИССЛЕДОВАНИЙ: " + summary.LabsCompleted, 4.0f);
                }
            }
            else
            {
                _hangar.ShowOfflineSummary(summary);
            }
        }

        private void EvaluateAndApplyOrientation(bool force)
        {
            if (_scaler == null || _hud == null || _hangar == null) return;

            int sw;
            int sh;
            if (MainCanvas != null && MainCanvas.renderMode == RenderMode.ScreenSpaceCamera && MainCanvas.worldCamera != null)
            {
                Camera uiCam = MainCanvas.worldCamera;
                sw = uiCam.targetTexture != null ? uiCam.targetTexture.width : uiCam.pixelWidth;
                sh = uiCam.targetTexture != null ? uiCam.targetTexture.height : uiCam.pixelHeight;
            }
            else if (_worldCam != null && _worldCam.targetTexture != null)
            {
                sw = _worldCam.targetTexture.width;
                sh = _worldCam.targetTexture.height;
            }
            else
            {
                sw = Screen.width;
                sh = Screen.height;
            }

            if (sw <= 0 || sh <= 0)
            {
                sw = ReferenceWidth;
                sh = ReferenceHeight;
            }

            bool isLandscape = sw > sh * 1.1f;
            if (!force && _orientationInitialized && isLandscape == _isLandscape)
            {
                return;
            }

            _orientationInitialized = true;
            _isLandscape = isLandscape;
            _scaler.referenceResolution = isLandscape
                ? new Vector2(LandscapeReferenceWidth, LandscapeReferenceHeight)
                : new Vector2(ReferenceWidth, ReferenceHeight);

            _lastSafeArea = Rect.zero;
            if (_offscreenMode || isLandscape)
            {
                if (_safeAreaRect != null)
                {
                    _safeAreaRect.anchorMin = Vector2.zero;
                    _safeAreaRect.anchorMax = Vector2.one;
                    _safeAreaRect.offsetMin = Vector2.zero;
                    _safeAreaRect.offsetMax = Vector2.zero;
                }
            }
            else
            {
                UpdateSafeAreaIfNeeded();
            }

            Canvas.ForceUpdateCanvases();
            _hud.ApplyLayoutMode(isLandscape);
            _hangar.ApplyLayoutMode(isLandscape);
            Canvas.ForceUpdateCanvases();
        }

        private void ProcessHotkeys(GameSession session)
        {
            if (Input.GetKeyDown(KeyCode.M))
            {
                UiStyle.ToggleMute();
                _hud.RefreshMuteVisual(force: true);
                _hangar.RefreshMuteVisual(force: true);
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (_hud.IsSummaryModalOpen)
                {
                    _hud.DismissModals();
                    OpenHangar(0);
                }
                else if (session.InRun)
                {
                    if (session.WorkshopUnlocked || session.BestWaveT1 >= 10)
                    {
                        _hud.TriggerRetreatToHangar();
                    }
                    else
                    {
                        _hud.ShowToast("Ангар модификаций откроется на Волне 10!", 2.4f);
                    }
                }
                else
                {
                    _hangar.TriggerStartRun();
                }
                return;
            }

            if (session.InRun && !_hud.IsSummaryModalOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) _hud.SelectCombatTabByIndex(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) _hud.SelectCombatTabByIndex(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) _hud.SelectCombatTabByIndex(2);

                if (Input.GetKeyDown(KeyCode.Q)) _hud.TriggerCycleMultiplier();
                if (Input.GetKeyDown(KeyCode.Space)) _hud.TriggerCycleSpeed();
            }
            else if (!session.InRun && !_hud.IsSummaryModalOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) _hangar.SelectTabFromHotkey(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) _hangar.SelectTabFromHotkey(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) _hangar.SelectTabFromHotkey(2);
                else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) _hangar.SelectTabFromHotkey(3);
                else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) _hangar.SelectTabFromHotkey(4);

                if (Input.GetKeyDown(KeyCode.Q)) _hangar.TriggerCycleWorkshopMultiplier();
                if (Input.GetKeyDown(KeyCode.Space)) _hangar.TriggerStartRun();
            }
        }

        private void OnStartRunFromHangar()
        {
            if (!_isBuilt || _session == null) return;
            _wasInRun = _session.InRun;
            _hangar.SetVisible(false);
            _hud.DismissModals();
            _hud.SetVisible(true);
        }

        private void SyncActiveScreenImmediate(GameSession session)
        {
            if (!_isBuilt) return;
            _hud.DismissModals();

            if (session == null || session.InRun)
            {
                _wasInRun = session != null && session.InRun;
                _hangar.SetVisible(false);
                _hud.SetVisible(true);
            }
            else
            {
                _wasInRun = false;
                _hud.SetVisible(false);
                if (session.CardsUnlocked && session.FtueDefenseArrowCompleted)
                {
                    _hangar.SetActiveTab(1);
                }
                else
                {
                    _hangar.SetActiveTab(0);
                }
                _hangar.SetVisible(true, session);
            }
        }

        private void UpdateSafeAreaIfNeeded()
        {
            if (_safeAreaRect == null || _offscreenMode) return;

            if (_isLandscape)
            {
                if (_lastSafeArea.width != -1f)
                {
                    _lastSafeArea = new Rect(0f, 0f, -1f, -1f);
                    _safeAreaRect.anchorMin = Vector2.zero;
                    _safeAreaRect.anchorMax = Vector2.one;
                    _safeAreaRect.offsetMin = Vector2.zero;
                    _safeAreaRect.offsetMax = Vector2.zero;
                }
                return;
            }

            int sw = Screen.width;
            int sh = Screen.height;
            if (sw <= 0 || sh <= 0) return;

            Rect sa = Screen.safeArea;
            if (sa == _lastSafeArea && _lastScreenSize.x == sw && _lastScreenSize.y == sh)
            {
                return;
            }

            _lastSafeArea = sa;
            _lastScreenSize = new Vector2Int(sw, sh);

            if (sa.width <= 0f || sa.height <= 0f)
            {
                _safeAreaRect.anchorMin = Vector2.zero;
                _safeAreaRect.anchorMax = Vector2.one;
                _safeAreaRect.offsetMin = Vector2.zero;
                _safeAreaRect.offsetMax = Vector2.zero;
                return;
            }

            Vector2 minAnchor = new Vector2(Mathf.Clamp01(sa.xMin / sw), Mathf.Clamp01(sa.yMin / sh));
            Vector2 maxAnchor = new Vector2(Mathf.Clamp01(sa.xMax / sw), Mathf.Clamp01(sa.yMax / sh));

            _safeAreaRect.anchorMin = minAnchor;
            _safeAreaRect.anchorMax = maxAnchor;
            _safeAreaRect.offsetMin = Vector2.zero;
            _safeAreaRect.offsetMax = Vector2.zero;
        }

        private void CleanupStaleChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private static void EnsureEventSystemExists()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(esGo);
            }
        }
    }
}
