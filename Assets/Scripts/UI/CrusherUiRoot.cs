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

        public Canvas MainCanvas { get; private set; }
        public HudScreen Hud => _hud;
        public HangarScreen Hangar => _hangar;

        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
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

            // Fast idempotent rebind when switching sessions/frames (e.g. OffscreenGate 5-frame capture)
            if (_isBuilt && MainCanvas != null && _hud != null && _hangar != null && ReferenceEquals(_registry, registry))
            {
                if (_worldCam != null)
                {
                    _hud.SetWorldCamera(_worldCam);
                }
                SyncActiveScreenImmediate(session);
                if (session != null)
                {
                    TickUI(session, 0.016f);
                }
                return;
            }

            _registry = registry;
            CleanupStaleChildren();
            EnsureEventSystemExists();

            // 1. Root 1080x2400 Portrait Canvas
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

            // 2. Safe-Area Root Container
            _safeAreaRect = UiFactory.CreateStretchRect("SafeAreaRoot", canvasGo.transform);
            _lastSafeArea = Rect.zero;
            _lastScreenSize = Vector2Int.zero;

            // 3. Shared ProceduralSDFUI Material Style & Zero-Alloc Tween Pool
            _style = new UiStyle(registry);
            _tweens = new TweenRunner();

            // 4. Build Combat HUD + Industrial Hangar + Shared UW Choice Modal
            _hud = new HudScreen();
            _hud.Build(
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
            UpdateSafeAreaIfNeeded();
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

            if (!_offscreenMode)
            {
                UpdateSafeAreaIfNeeded();
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

                if (_worldCam == null && _hud != null)
                {
                    _worldCam = uiCam;
                    _hud.SetWorldCamera(uiCam);
                }

                Canvas.ForceUpdateCanvases();
            }
            else
            {
                _offscreenMode = false;
                MainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                MainCanvas.worldCamera = null;
                _lastSafeArea = Rect.zero;
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
