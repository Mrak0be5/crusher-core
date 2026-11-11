using System;
using System.Globalization;
using System.Text;
using Crusher.Audio;
using Crusher.Config;
using Crusher.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Crusher.UI
{
    /// <summary>
    /// Adaptive Industrial Combat HUD (Crusher Core V2 Ultra):
    /// - FullScreenArenaOverlay (outside SafeAreaRoot): pixel-perfect concentric 24-segment Rotor HP/Regen ring,
    ///   <=6 pooled DamageNumberAggregator popups, and 3D-tracked GemDrone (+5) button via ScreenPointToLocalPointInRectangle.
    /// - Adaptive Mobile Portrait (1080x2400, 2-col 41.8% deck) & PC WebGL Landscape (1920x1080, 1320px compact top bar + 1420px 27% 4/5-col deck).
    /// - Live industrial telemetry ("⚡ DPS • ОБОРОТЫ RPM • УБИТО"), Mute button ("ЗВУК ВКЛ/ВЫКЛ"), tactile hover/press tweens, and PC hotkeys.
    /// </summary>
    public sealed class HudScreen
    {
        private const int MaxDeckCards = 10; // Defense has 10 stats max
        private static readonly int[] BuyMultSteps = { 1, 5, 10, -1 }; // -1 = MAX
        private static readonly string[] BuyMultLabels = { "КРАТН ×1", "КРАТН ×5", "КРАТН ×10", "КРАТН MAX" };
        private static readonly float[] SpeedSteps = { 1.0f, 1.5f, 2.0f, 2.7f, 3.5f, 4.3f, 5.0f };

        private struct StatCardWidget
        {
            public RectTransform Root;
            public Image CardBg;
            public Button CardWholeBtn;
            public RectTransform IconRect;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI LevelBadge;
            public TextMeshProUGUI ValueTransition;
            public Button BuyBtn;
            public RectTransform BuyRect;
            public Image BuyBg;
            public TextMeshProUGUI BuyLabel;

            public StatId BoundStat;
            public bool IsPreviewLocked;
            public int CachedTotalLv;
            public int CachedBuyCount;
            public long CachedCostKey;
            public bool CachedAffordable;
        }

        private struct UwChoiceRow
        {
            public RectTransform Root;
            public RectTransform IconRect;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Desc;
            public Button PickBtn;
            public RectTransform PickRect;
            public Image PickBg;
            public TextMeshProUGUI PickLabel;
            public UwId BoundUw;
        }

        private struct CurrencyPillRefs
        {
            public RectTransform Rect;
            public RectTransform IconRect;
            public TextMeshProUGUI ValueTmp;
        }

        private RectTransform _arenaOverlay; // Full-screen outside SafeAreaRoot!
        private RectTransform _root;         // Inside SafeAreaRoot
        private RectTransform _combatLayer;
        private Canvas _canvas;
        private UiStyle _style;
        private TweenRunner _tweens;
        private Camera _worldCam;
        private Action<int> _onOpenHangar;
        private bool _isLandscape;
        private int _lastCardLayoutCols = -1;

        // Top HUD widgets & caches
        private RectTransform _topPanelRect;
        private RectTransform _tierPillRect;
        private TextMeshProUGUI _tierBadgeTmp;
        private RectTransform _waveBoxRect;
        private TextMeshProUGUI _waveTitleTmp;
        private TextMeshProUGUI _waveSubTmp;
        private RectTransform _telemetryStripRect;
        private TextMeshProUGUI _telemetryTmp;

        private Button _speedBtn;
        private RectTransform _speedBtnRect;
        private TextMeshProUGUI _speedLabelTmp;

        private Button _muteBtn;
        private RectTransform _muteBtnRect;
        private Image _muteBtnBg;
        private TextMeshProUGUI _muteLabelTmp;
        private int _cachedMuteState = -1;

        private Button _retreatBtn;
        private RectTransform _retreatBtnRect;
        private TextMeshProUGUI _retreatLabelTmp;

        private readonly CurrencyPillRefs[] _curPills = new CurrencyPillRefs[5];

        private int _cachedTier = -1;
        private int _cachedWaveKey = -1;
        private int _cachedWaveSubSec = -999;
        private bool _cachedInCooldown;
        private int _cachedSpeedTenths = -1;
        private long _cachedTelemetryKey = long.MinValue;
        private long _cachedCash = -1;
        private long _cachedCoins = -1;
        private int _cachedGems = -1;
        private int _cachedStones = -1;
        private int _cachedCells = -1;

        // Synergy & Toast banners
        private RectTransform _synergyRect;
        private TextMeshProUGUI _synergyTmp;
        private int _cachedSynergyHash = int.MinValue;

        private RectTransform _toastRect;
        private CanvasGroup _toastGroup;
        private TextMeshProUGUI _toastTmp;
        private float _toastTimer;
        private float _toastTargetY = -284f;

        // Diegetic Core HP Ring (in _arenaOverlay!)
        private RectTransform _hpRingRoot;
        private RectTransform _hpPlateRect;
        private TextMeshProUGUI _hpNumericTmp;
        private TextMeshProUGUI _hpArmorSubTmp;
        private float _displayedTrailRatio = 1.0f;
        private long _cachedHpKey = -1;
        private long _cachedArmorKey = -1;

        // Damage numbers & Gem Drone (in _arenaOverlay!)
        private readonly DamageNumberAggregator _dmgNumbers = new DamageNumberAggregator();
        private RectTransform _gemDroneBtnRect;
        private Button _gemDroneBtn;

        // Bottom Combat Deck
        private RectTransform _deckRect;
        private RectTransform _deckHeaderRect;
        private TextMeshProUGUI _deckTitleTmp;
        private TextMeshProUGUI _coreGradeTmp;
        private Button _uwQuickBtn;
        private RectTransform _uwQuickRect;
        private TextMeshProUGUI _uwQuickLabel;
        private int _cachedHeaderGrade = -1;
        private StatTab _cachedHeaderTab = (StatTab)255;

        private StatTab _activeTab = StatTab.Attack;
        private int _buyMultIdx;
        private readonly StatCardWidget[] _cards = new StatCardWidget[MaxDeckCards];
        private int _visibleCardCount;

        // Bottom / Left Rail Tabs & Multiplier buttons
        private Image _tabAttackBg;
        private RectTransform _tabAttackRect;
        private RectTransform _tabAttackIconRect;
        private TextMeshProUGUI _tabAttackLbl;

        private Image _tabDefenseBg;
        private RectTransform _tabDefenseRect;
        private RectTransform _tabDefenseIconRect;
        private TextMeshProUGUI _tabDefenseLbl;

        private Image _tabUtilityBg;
        private RectTransform _tabUtilityRect;
        private RectTransform _tabUtilityIconRect;
        private TextMeshProUGUI _tabUtilityLbl;

        private Button _multBtn;
        private RectTransform _multBtnRect;
        private TextMeshProUGUI _multLabelTmp;

        // FTUE Arrow in HUD
        private RectTransform _ftueArrowRect;
        private TextMeshProUGUI _ftueArrowTmp;
        private bool _run1DefensePurchased;

        // UW Choice Modal (shared over HUD & Hangar)
        private RectTransform _uwModalRoot;
        private RectTransform _uwModalWinRect;
        private CanvasGroup _uwModalGroup;
        private TextMeshProUGUI _uwModalSubTmp;
        private readonly UwChoiceRow[] _uwRows = new UwChoiceRow[3];

        // Run Summary Modal (Game Over)
        private RectTransform _summaryModalRoot;
        private RectTransform _summaryWinRect;
        private RectTransform _summaryStatsBoxRect;
        private CanvasGroup _summaryModalGroup;
        private TextMeshProUGUI _sumTitleTmp;
        private TextMeshProUGUI _sumStatsTmp;
        private RectTransform _sumFtueArrowRect;
        private bool _summaryOpen;

        private GameSession _boundSession;
        private readonly StringBuilder _sb = new StringBuilder(192);
        private readonly char[] _buyBtnChars = new char[48];

        public bool IsSummaryModalOpen => _summaryOpen;

        public void Build(
            Transform fullScreenArenaParent,
            Transform safeParent,
            Canvas canvas,
            UiStyle style,
            TweenRunner tweens,
            Camera worldCam,
            Action<int> onOpenHangar)
        {
            _canvas = canvas;
            _style = style;
            _tweens = tweens;
            _worldCam = worldCam;
            _onOpenHangar = onOpenHangar;

            // 1. Full-Screen Diegetic Overlay (OUTSIDE SafeAreaRoot so rotor ring is 100% concentric!)
            _arenaOverlay = UiFactory.CreateStretchRect("HudArenaOverlay", fullScreenArenaParent);

            // 2. Safe-Area Anchored Chrome
            _root = UiFactory.CreateStretchRect("HudScreen", safeParent);
            _combatLayer = UiFactory.CreateStretchRect("CombatLayer", _root);

            BuildArenaOverlay();
            BuildTopTelemetryBar();
            BuildBottomDeck();
            BuildUwChoiceModal(safeParent);
            BuildRunSummaryModal();

            ApplyLayoutMode(false);
            ApplyTabVisuals();
            RefreshMuteVisual(force: true);
        }

        public void SetWorldCamera(Camera worldCam)
        {
            _worldCam = worldCam;
            _dmgNumbers.SetWorldCamera(worldCam);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible)
            {
                _root.gameObject.SetActive(visible);
            }
            bool showArenaOverlay = visible && !_summaryOpen;
            if (_arenaOverlay != null && _arenaOverlay.gameObject.activeSelf != showArenaOverlay)
            {
                _arenaOverlay.gameObject.SetActive(showArenaOverlay);
            }
        }

        public void DismissModals()
        {
            _summaryOpen = false;
            if (_summaryModalRoot != null) _summaryModalRoot.gameObject.SetActive(false);
            if (_uwModalRoot != null) _uwModalRoot.gameObject.SetActive(false);
            if (_arenaOverlay != null && _root != null && _root.gameObject.activeSelf)
            {
                _arenaOverlay.gameObject.SetActive(true);
            }
        }

        public void BringSharedModalsToFront()
        {
            if (_uwModalRoot != null)
            {
                _uwModalRoot.SetAsLastSibling();
            }
        }

        public void ApplyLayoutMode(bool isLandscape)
        {
            _isLandscape = isLandscape;
            _lastCardLayoutCols = -1;
            _cachedHeaderGrade = -1;

            if (!isLandscape)
            {
                // --- PORTRAIT 1080x2400 LAYOUT ---
                _hpRingRoot.sizeDelta = new Vector2(264f, 264f);
                _hpPlateRect.anchoredPosition = new Vector2(0f, -152f);
                _hpPlateRect.sizeDelta = new Vector2(316f, 64f);

                _topPanelRect.anchoredPosition = new Vector2(0f, -14f);
                _topPanelRect.sizeDelta = new Vector2(1044f, 226f);

                _tierPillRect.anchoredPosition = new Vector2(16f, -14f);
                _tierPillRect.sizeDelta = new Vector2(124f, 70f);
                _tierBadgeTmp.fontSize = 23f;

                _waveBoxRect.anchoredPosition = new Vector2(150f, -14f);
                _waveBoxRect.sizeDelta = new Vector2(382f, 70f);
                _waveTitleTmp.enableAutoSizing = true;
                _waveTitleTmp.fontSizeMin = 15f;
                _waveTitleTmp.fontSizeMax = 23f;
                _waveSubTmp.fontSize = 17.5f;

                _speedBtnRect.anchoredPosition = new Vector2(542f, -14f);
                _speedBtnRect.sizeDelta = new Vector2(138f, 70f);
                _speedLabelTmp.fontSize = 24f;

                _muteBtnRect.anchoredPosition = new Vector2(690f, -14f);
                _muteBtnRect.sizeDelta = new Vector2(146f, 70f);
                _muteLabelTmp.fontSize = 18f;

                _retreatBtnRect.anchoredPosition = new Vector2(846f, -14f);
                _retreatBtnRect.sizeDelta = new Vector2(182f, 70f);
                _retreatLabelTmp.fontSize = 22f;

                // Row 2: Live Industrial Telemetry Strip
                _telemetryStripRect.anchoredPosition = new Vector2(16f, -92f);
                _telemetryStripRect.sizeDelta = new Vector2(1012f, 44f);
                _telemetryTmp.enableAutoSizing = true;
                _telemetryTmp.fontSizeMin = 13f;
                _telemetryTmp.fontSizeMax = 19f;

                // Row 3: 5 Currency Pills
                float[] px = { 16f, 248f, 468f, 660f, 846f };
                float[] pw = { 222f, 210f, 182f, 176f, 182f };
                for (int i = 0; i < 5; i++)
                {
                    _curPills[i].Rect.anchoredPosition = new Vector2(px[i], -144f);
                    _curPills[i].Rect.sizeDelta = new Vector2(pw[i], 68f);
                    _curPills[i].IconRect.sizeDelta = new Vector2(44f, 44f);
                    _curPills[i].ValueTmp.fontSize = 22f;
                    _curPills[i].ValueTmp.rectTransform.offsetMin = new Vector2(58f, 4f);
                }

                _synergyRect.anchoredPosition = new Vector2(0f, -248f);
                _synergyRect.sizeDelta = new Vector2(880f, 54f);
                _toastTargetY = -310f;
                _toastRect.anchoredPosition = new Vector2(0f, _toastTargetY);
                _toastRect.sizeDelta = new Vector2(940f, 68f);

                // Bottom 41.8% Combat Deck (1080x1004)
                _deckRect.anchoredPosition = Vector2.zero;
                _deckRect.sizeDelta = new Vector2(1080f, 1004f);

                _deckHeaderRect.anchoredPosition = new Vector2(0f, 918f);
                _deckHeaderRect.sizeDelta = new Vector2(1040f, 72f);
                _deckTitleTmp.fontSize = 22f;
                _coreGradeTmp.fontSize = 19f;
                _uwQuickRect.sizeDelta = new Vector2(280f, 56f);

                // Bottom Tabs (matching smoke_tt36p.py coordinates)
                ConfigurePortraitTab(_tabAttackRect, _tabAttackIconRect, _tabAttackLbl, -392f, "АТАКА");
                ConfigurePortraitTab(_tabDefenseRect, _tabDefenseIconRect, _tabDefenseLbl, -132f, "ЗАЩИТА");
                ConfigurePortraitTab(_tabUtilityRect, _tabUtilityIconRect, _tabUtilityLbl, 128f, "УТИЛИТИ");

                _multBtnRect.anchorMin = new Vector2(0.5f, 0f);
                _multBtnRect.anchorMax = new Vector2(0.5f, 0f);
                _multBtnRect.pivot = new Vector2(0.5f, 0f);
                _multBtnRect.anchoredPosition = new Vector2(390f, 32f);
                _multBtnRect.sizeDelta = new Vector2(246f, 140f);
                _multLabelTmp.fontSize = 23f;

                // Modals (Portrait)
                _uwModalWinRect.sizeDelta = new Vector2(960f, 1180f);
                for (int i = 0; i < 3; i++)
                {
                    _uwRows[i].Root.anchorMin = new Vector2(0.5f, 1f);
                    _uwRows[i].Root.anchorMax = new Vector2(0.5f, 1f);
                    _uwRows[i].Root.pivot = new Vector2(0.5f, 1f);
                    _uwRows[i].Root.anchoredPosition = new Vector2(0f, -156f - i * 280f);
                    _uwRows[i].Root.sizeDelta = new Vector2(880f, 252f);
                    _uwRows[i].IconRect.anchorMin = new Vector2(0f, 0.5f);
                    _uwRows[i].IconRect.anchorMax = new Vector2(0f, 0.5f);
                    _uwRows[i].IconRect.pivot = new Vector2(0f, 0.5f);
                    _uwRows[i].IconRect.anchoredPosition = new Vector2(24f, 0f);
                    _uwRows[i].IconRect.sizeDelta = new Vector2(114f, 114f);
                    _uwRows[i].Title.alignment = TextAlignmentOptions.Left;
                    _uwRows[i].Title.rectTransform.offsetMin = new Vector2(160f, -70f);
                    _uwRows[i].Title.rectTransform.offsetMax = new Vector2(-24f, -18f);
                    _uwRows[i].Desc.alignment = TextAlignmentOptions.Left;
                    _uwRows[i].Desc.rectTransform.offsetMin = new Vector2(160f, 84f);
                    _uwRows[i].Desc.rectTransform.offsetMax = new Vector2(-26f, -72f);
                    _uwRows[i].PickRect.anchorMin = new Vector2(1f, 0f);
                    _uwRows[i].PickRect.anchorMax = new Vector2(1f, 0f);
                    _uwRows[i].PickRect.pivot = new Vector2(1f, 0f);
                    _uwRows[i].PickRect.anchoredPosition = new Vector2(-22f, 18f);
                    _uwRows[i].PickRect.sizeDelta = new Vector2(280f, 64f);
                }

                _summaryWinRect.sizeDelta = new Vector2(920f, 880f);
                _summaryStatsBoxRect.sizeDelta = new Vector2(820f, 420f);
            }
            else
            {
                // --- LANDSCAPE 1920x1080 PC WEBGL LAYOUT ---
                _hpRingRoot.sizeDelta = new Vector2(216f, 216f);
                _hpPlateRect.anchoredPosition = new Vector2(0f, -128f);
                _hpPlateRect.sizeDelta = new Vector2(292f, 54f);

                // Compact Centered Top Telemetry Bar (max-width: 1320px, height: 112px)
                _topPanelRect.anchoredPosition = new Vector2(0f, -8f);
                _topPanelRect.sizeDelta = new Vector2(1320f, 112f);

                _tierPillRect.anchoredPosition = new Vector2(14f, -10f);
                _tierPillRect.sizeDelta = new Vector2(104f, 44f);
                _tierBadgeTmp.fontSize = 19f;

                _waveBoxRect.anchoredPosition = new Vector2(126f, -10f);
                _waveBoxRect.sizeDelta = new Vector2(292f, 44f);
                _waveTitleTmp.enableAutoSizing = true;
                _waveTitleTmp.fontSizeMin = 13.5f;
                _waveTitleTmp.fontSizeMax = 19.5f;
                _waveSubTmp.fontSize = 15.5f;

                _telemetryStripRect.anchoredPosition = new Vector2(426f, -10f);
                _telemetryStripRect.sizeDelta = new Vector2(454f, 44f);
                _telemetryTmp.enableAutoSizing = true;
                _telemetryTmp.fontSizeMin = 12.5f;
                _telemetryTmp.fontSizeMax = 16.5f;

                _speedBtnRect.anchoredPosition = new Vector2(888f, -10f);
                _speedBtnRect.sizeDelta = new Vector2(128f, 44f);
                _speedLabelTmp.fontSize = 19f;

                _muteBtnRect.anchoredPosition = new Vector2(1024f, -10f);
                _muteBtnRect.sizeDelta = new Vector2(138f, 44f);
                _muteLabelTmp.fontSize = 16f;

                _retreatBtnRect.anchoredPosition = new Vector2(1170f, -10f);
                _retreatBtnRect.sizeDelta = new Vector2(136f, 44f);
                _retreatLabelTmp.fontSize = 18f;

                // Row 2 of Compact Top Bar: 5 Currency Pills across 1292px
                for (int i = 0; i < 5; i++)
                {
                    float w = 250f;
                    float x = 14f + i * 260.5f;
                    _curPills[i].Rect.anchoredPosition = new Vector2(x, -60f);
                    _curPills[i].Rect.sizeDelta = new Vector2(w, 42f);
                    _curPills[i].IconRect.sizeDelta = new Vector2(32f, 32f);
                    _curPills[i].ValueTmp.fontSize = 19f;
                    _curPills[i].ValueTmp.rectTransform.offsetMin = new Vector2(48f, 2f);
                }

                _synergyRect.anchoredPosition = new Vector2(0f, -126f);
                _synergyRect.sizeDelta = new Vector2(780f, 42f);
                _toastTargetY = -174f;
                _toastRect.anchoredPosition = new Vector2(0f, _toastTargetY);
                _toastRect.sizeDelta = new Vector2(840f, 48f);

                // Compact Bottom ~27% Combat Deck (max-width: 1420px, height: 292px => 27.0% of 1080px!)
                _deckRect.anchoredPosition = new Vector2(0f, 8f);
                _deckRect.sizeDelta = new Vector2(1420f, 292f);

                _deckHeaderRect.anchoredPosition = new Vector2(0f, 250f);
                _deckHeaderRect.sizeDelta = new Vector2(1394f, 34f);
                _deckTitleTmp.fontSize = 17f;
                _coreGradeTmp.fontSize = 16f;
                _uwQuickRect.sizeDelta = new Vector2(248f, 30f);

                // Left Command Rail inside 1420x292 Deck (3 Tabs + [Q] Multiplier)
                ConfigureLandscapeRailTab(_tabAttackRect, _tabAttackIconRect, _tabAttackLbl, 186f, "[1] АТАКА");
                ConfigureLandscapeRailTab(_tabDefenseRect, _tabDefenseIconRect, _tabDefenseLbl, 126f, "[2] ЗАЩИТА");
                ConfigureLandscapeRailTab(_tabUtilityRect, _tabUtilityIconRect, _tabUtilityLbl, 66f, "[3] УТИЛИТИ");

                _multBtnRect.anchorMin = new Vector2(0f, 0f);
                _multBtnRect.anchorMax = new Vector2(0f, 0f);
                _multBtnRect.pivot = new Vector2(0f, 0f);
                _multBtnRect.anchoredPosition = new Vector2(14f, 10f);
                _multBtnRect.sizeDelta = new Vector2(208f, 48f);
                _multLabelTmp.fontSize = 18f;

                // Modals (Landscape 1920x1080: 3 horizontal UW columns so it fits cleanly inside 1080px height!)
                _uwModalWinRect.sizeDelta = new Vector2(1280f, 700f);
                for (int i = 0; i < 3; i++)
                {
                    float cx = (i - 1) * 408f;
                    _uwRows[i].Root.anchorMin = new Vector2(0.5f, 1f);
                    _uwRows[i].Root.anchorMax = new Vector2(0.5f, 1f);
                    _uwRows[i].Root.pivot = new Vector2(0.5f, 1f);
                    _uwRows[i].Root.anchoredPosition = new Vector2(cx, -138f);
                    _uwRows[i].Root.sizeDelta = new Vector2(390f, 444f);
                    _uwRows[i].IconRect.anchorMin = new Vector2(0.5f, 1f);
                    _uwRows[i].IconRect.anchorMax = new Vector2(0.5f, 1f);
                    _uwRows[i].IconRect.pivot = new Vector2(0.5f, 1f);
                    _uwRows[i].IconRect.anchoredPosition = new Vector2(0f, -20f);
                    _uwRows[i].IconRect.sizeDelta = new Vector2(104f, 104f);
                    _uwRows[i].Title.alignment = TextAlignmentOptions.Center;
                    _uwRows[i].Title.rectTransform.offsetMin = new Vector2(16f, -184f);
                    _uwRows[i].Title.rectTransform.offsetMax = new Vector2(-16f, -134f);
                    _uwRows[i].Desc.alignment = TextAlignmentOptions.Center;
                    _uwRows[i].Desc.rectTransform.offsetMin = new Vector2(20f, 98f);
                    _uwRows[i].Desc.rectTransform.offsetMax = new Vector2(-20f, -190f);
                    _uwRows[i].PickRect.anchorMin = new Vector2(0.5f, 0f);
                    _uwRows[i].PickRect.anchorMax = new Vector2(0.5f, 0f);
                    _uwRows[i].PickRect.pivot = new Vector2(0.5f, 0f);
                    _uwRows[i].PickRect.anchoredPosition = new Vector2(0f, 18f);
                    _uwRows[i].PickRect.sizeDelta = new Vector2(340f, 64f);
                }

                _summaryWinRect.sizeDelta = new Vector2(900f, 680f);
                _summaryStatsBoxRect.sizeDelta = new Vector2(800f, 320f);
            }

            LayoutStatCardsForCurrentMode(_visibleCardCount > 0 ? _visibleCardCount : 7);
        }

        private static void ConfigurePortraitTab(RectTransform tabRt, RectTransform iconRt, TextMeshProUGUI lbl, float centerX, string text)
        {
            tabRt.anchorMin = new Vector2(0.5f, 0f);
            tabRt.anchorMax = new Vector2(0.5f, 0f);
            tabRt.pivot = new Vector2(0.5f, 0f);
            tabRt.anchoredPosition = new Vector2(centerX, 32f);
            tabRt.sizeDelta = new Vector2(250f, 140f);

            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -12f);
            iconRt.sizeDelta = new Vector2(56f, 56f);

            lbl.text = text;
            lbl.fontSize = 22f;
            lbl.alignment = TextAlignmentOptions.Bottom;
            lbl.rectTransform.offsetMin = new Vector2(8f, 22f);
            lbl.rectTransform.offsetMax = new Vector2(-8f, -64f);
        }

        private static void ConfigureLandscapeRailTab(RectTransform tabRt, RectTransform iconRt, TextMeshProUGUI lbl, float bottomY, string text)
        {
            tabRt.anchorMin = new Vector2(0f, 0f);
            tabRt.anchorMax = new Vector2(0f, 0f);
            tabRt.pivot = new Vector2(0f, 0f);
            tabRt.anchoredPosition = new Vector2(14f, bottomY);
            tabRt.sizeDelta = new Vector2(208f, 54f);

            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(10f, 0f);
            iconRt.sizeDelta = new Vector2(36f, 36f);

            lbl.text = text;
            lbl.fontSize = 18f;
            lbl.alignment = TextAlignmentOptions.Left;
            lbl.rectTransform.offsetMin = new Vector2(54f, 4f);
            lbl.rectTransform.offsetMax = new Vector2(-8f, -4f);
        }

        private void LayoutStatCardsForCurrentMode(int activeCount)
        {
            int cols = !_isLandscape ? 2 : (activeCount > 8 ? 5 : 4);
            if (cols == _lastCardLayoutCols) return;
            _lastCardLayoutCols = cols;

            if (!_isLandscape)
            {
                for (int i = 0; i < MaxDeckCards; i++)
                {
                    int col = i % 2;
                    int row = i / 2;
                    float cx = col == 0 ? -264f : 264f;
                    float cy = 838f - row * 142f;

                    ref StatCardWidget w = ref _cards[i];
                    w.Root.anchorMin = new Vector2(0.5f, 0f);
                    w.Root.anchorMax = new Vector2(0.5f, 0f);
                    w.Root.pivot = new Vector2(0.5f, 0.5f);
                    w.Root.anchoredPosition = new Vector2(cx, cy);
                    w.Root.sizeDelta = new Vector2(512f, 134f);

                    w.IconRect.anchoredPosition = new Vector2(14f, -14f);
                    w.IconRect.sizeDelta = new Vector2(58f, 58f);

                    w.Title.enableAutoSizing = false;
                    w.Title.fontSize = 22f;
                    w.Title.rectTransform.offsetMin = new Vector2(84f, -52f);
                    w.Title.rectTransform.offsetMax = new Vector2(-14f, -10f);

                    w.LevelBadge.fontSize = 18f;
                    w.LevelBadge.alignment = TextAlignmentOptions.Center;
                    w.LevelBadge.rectTransform.anchorMin = new Vector2(0f, 0f);
                    w.LevelBadge.rectTransform.anchorMax = new Vector2(0f, 0f);
                    w.LevelBadge.rectTransform.pivot = new Vector2(0f, 0f);
                    w.LevelBadge.rectTransform.anchoredPosition = new Vector2(10f, 12f);
                    w.LevelBadge.rectTransform.sizeDelta = new Vector2(72f, 34f);

                    w.ValueTransition.fontSize = 20f;
                    w.ValueTransition.rectTransform.offsetMin = new Vector2(84f, 14f);
                    w.ValueTransition.rectTransform.offsetMax = new Vector2(-204f, 68f);

                    w.BuyRect.anchorMin = new Vector2(1f, 0f);
                    w.BuyRect.anchorMax = new Vector2(1f, 0f);
                    w.BuyRect.pivot = new Vector2(1f, 0f);
                    w.BuyRect.anchoredPosition = new Vector2(-12f, 12f);
                    w.BuyRect.sizeDelta = new Vector2(184f, 64f);
                    w.BuyLabel.fontSize = 22f;
                }
            }
            else
            {
                // Landscape 4x2 (286x112) or 5x2 (226x112) inside right 1172x232 matrix of 1420x292 deck
                float cardW = cols == 5 ? 226f : 286f;
                float stepX = cols == 5 ? 236f : 296f;
                const float cardH = 112f;

                for (int i = 0; i < MaxDeckCards; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    float xLeft = 234f + col * stepX;
                    float yBot = row == 0 ? 128f : 10f;

                    ref StatCardWidget w = ref _cards[i];
                    w.Root.anchorMin = new Vector2(0f, 0f);
                    w.Root.anchorMax = new Vector2(0f, 0f);
                    w.Root.pivot = new Vector2(0f, 0f);
                    w.Root.anchoredPosition = new Vector2(xLeft, yBot);
                    w.Root.sizeDelta = new Vector2(cardW, cardH);

                    w.IconRect.anchoredPosition = new Vector2(8f, -8f);
                    w.IconRect.sizeDelta = new Vector2(36f, 36f);

                    w.Title.enableAutoSizing = true;
                    w.Title.fontSizeMin = 12f;
                    w.Title.fontSizeMax = cols == 5 ? 15f : 17f;
                    w.Title.rectTransform.offsetMin = new Vector2(48f, -34f);
                    w.Title.rectTransform.offsetMax = new Vector2(-54f, -6f);

                    w.LevelBadge.fontSize = 14f;
                    w.LevelBadge.alignment = TextAlignmentOptions.Right;
                    w.LevelBadge.rectTransform.anchorMin = new Vector2(1f, 1f);
                    w.LevelBadge.rectTransform.anchorMax = new Vector2(1f, 1f);
                    w.LevelBadge.rectTransform.pivot = new Vector2(1f, 1f);
                    w.LevelBadge.rectTransform.anchoredPosition = new Vector2(-8f, -8f);
                    w.LevelBadge.rectTransform.sizeDelta = new Vector2(60f, 24f);

                    w.ValueTransition.fontSize = cols == 5 ? 14.5f : 16f;
                    w.ValueTransition.rectTransform.offsetMin = new Vector2(10f, 46f);
                    w.ValueTransition.rectTransform.offsetMax = new Vector2(-10f, 76f);

                    w.BuyRect.anchorMin = new Vector2(0.5f, 0f);
                    w.BuyRect.anchorMax = new Vector2(0.5f, 0f);
                    w.BuyRect.pivot = new Vector2(0.5f, 0f);
                    w.BuyRect.anchoredPosition = new Vector2(0f, 6f);
                    w.BuyRect.sizeDelta = new Vector2(cardW - 16f, 36f);
                    w.BuyLabel.fontSize = 16.5f;
                }
            }
        }

        public void OpenSummaryForLastRun(GameSession session)
        {
            if (session == null || _summaryModalRoot == null) return;
            _boundSession = session;
            _summaryOpen = true;
            if (_arenaOverlay != null) _arenaOverlay.gameObject.SetActive(false);
            _combatLayer.gameObject.SetActive(true);
            _summaryModalRoot.gameObject.SetActive(true);
            _tweens.FadeCanvasGroup(_summaryModalGroup, 0f, 1f, 0.20f);

            int waveEnded = Math.Max(1, session.Combat.CurrentWave);
            _sumTitleTmp.text = "РОТОР ПЕРЕГРУЖЕН!";

            _sb.Clear();
            _sb.Append("Достигнута волна: <color=#FFD54F>ВОЛНА ").Append(waveEnded).Append("</color>\n");
            _sb.Append("Рекорд Тира ").Append(session.ActiveTier).Append(": <color=#52E5FF>ВОЛНА ")
               .Append(session.ActiveTier == 1 ? session.BestWaveT1 : session.BestWaveT2).Append("</color>\n\n");
            _sb.Append("Добыто монет за забег: <color=#FFD54F>+");
            UiFormat.AppendCompactToSb(_sb, session.Combat.RunCoinsEarned);
            _sb.Append("</color>\n");
            _sb.Append("Элитных клеток: <color=#CE93D8>+").Append(session.Combat.RunCellsEarned).Append("</color>\n");
            _sb.Append("Уничтожено целей: ").Append(session.Combat.RunTotalKills);
            _sumStatsTmp.text = _sb.ToString();

            bool showWorkshopFtue = session.TotalRunsCompleted <= 1 && !session.FtueDefenseArrowCompleted;
            _sumFtueArrowRect.gameObject.SetActive(showWorkshopFtue);
        }

        public void OpenUwModal(GameSession session)
        {
            if (session == null || _uwModalRoot == null) return;
            _boundSession = session;

            Span<UwId> choices = stackalloc UwId[3];
            int count = session.GetAvailableUwChoices(choices);
            if (count <= 0) return;

            int stoneCost = session.GetNextUwUnlockStoneCost();
            bool canAfford = session.Stones >= stoneCost;

            _sb.Clear();
            _sb.Append("Стоимость реакторного ядра: <color=#69F0AE>").Append(stoneCost)
               .Append(" камней</color> (в наличии: ").Append(session.Stones).Append(')');
            _uwModalSubTmp.text = _sb.ToString();

            for (int i = 0; i < 3; i++)
            {
                if (i < count)
                {
                    UwId id = choices[i];
                    ref readonly UwDef def = ref BalanceTables.Uws[(int)id];
                    _uwRows[i].BoundUw = id;
                    _uwRows[i].Root.gameObject.SetActive(true);
                    _uwRows[i].Icon.sprite = _style.GetIcon(UiIcons.UwIcons[(int)id]);
                    _uwRows[i].Title.text = def.TitleRu;
                    _uwRows[i].Desc.text = def.DescRu;
                    _uwRows[i].PickBtn.interactable = canAfford;
                    if (_style.MatBuyAmber != null && _style.MatBuyDisabled != null)
                    {
                        _uwRows[i].PickBg.material = canAfford ? _style.MatBuyAmber : _style.MatBuyDisabled;
                    }
                    _uwRows[i].PickLabel.color = canAfford ? UiStyle.TextButtonDark : UiStyle.TextMuted;
                    _uwRows[i].PickLabel.text = canAfford ? ("ОТКРЫТЬ (" + stoneCost + ")") : ("НУЖНО " + stoneCost);
                }
                else
                {
                    _uwRows[i].Root.gameObject.SetActive(false);
                }
            }

            _uwModalRoot.gameObject.SetActive(true);
            _tweens.FadeCanvasGroup(_uwModalGroup, 0f, 1f, 0.18f);
        }

        public void Tick(GameSession session, float unscaledDt)
        {
            if (session == null) return;
            _boundSession = session;
            CombatSim sim = session.Combat;

            RefreshMuteVisual(force: false);

            if (_summaryOpen)
            {
                AnimateFtueBob(_sumFtueArrowRect, 166f);
                return;
            }

            // 1. Top Telemetry & Currencies
            if (session.ActiveTier != _cachedTier)
            {
                _cachedTier = session.ActiveTier;
                _tierBadgeTmp.text = _cachedTier == 2 ? "ТИР 2" : "ТИР 1";
                _tierBadgeTmp.color = _cachedTier == 2 ? UiStyle.TextAmber : UiStyle.TextCyan;
            }

            bool isBossWave = (sim.CurrentWave % 10 == 0) || (sim.CurrentWave >= 102 && sim.CurrentWave % 2 == 0);
            UiFormat.SetWaveHeader(_waveTitleTmp, sim.CurrentWave, isBossWave, ref _cachedWaveKey);

            float waveFill;
            int subSec;
            if (sim.InWaveCooldown)
            {
                float totalCd = Mathf.Max(0.2f, sim.Stats.WaveCooldownSeconds);
                waveFill = Mathf.Clamp01(1f - (sim.CooldownRemaining / totalCd));
                subSec = Mathf.CeilToInt(sim.CooldownRemaining);
            }
            else
            {
                waveFill = Mathf.Clamp01(sim.WaveElapsed / BalanceTables.WAVE_FIGHT_SECONDS);
                subSec = Mathf.CeilToInt(Mathf.Max(0f, BalanceTables.WAVE_FIGHT_SECONDS - sim.WaveElapsed));
            }

            if (_style.MatWaveBar != null)
            {
                _style.MatWaveBar.SetFloat(UiStyle.PropFill, waveFill);
                _style.MatWaveBar.SetColor(UiStyle.PropAccent,
                    isBossWave ? new Color(1.20f, 0.32f, 0.28f, 1f)
                    : (sim.InWaveCooldown ? new Color(0.35f, 0.72f, 1.00f, 1f) : new Color(0.24f, 0.90f, 1.12f, 1f)));
            }

            if (subSec != _cachedWaveSubSec || sim.InWaveCooldown != _cachedInCooldown)
            {
                _cachedWaveSubSec = subSec;
                _cachedInCooldown = sim.InWaveCooldown;
                _sb.Clear();
                _sb.Append(sim.InWaveCooldown ? "ПАУЗА " : "БОЙ ").Append(Mathf.Max(0, subSec)).Append('с');
                _waveSubTmp.text = _sb.ToString();
            }

            int speedTenths = Mathf.RoundToInt(session.EffectiveTimeScale * 10f);
            if (speedTenths != _cachedSpeedTenths)
            {
                _cachedSpeedTenths = speedTenths;
                _sb.Clear();
                _sb.Append('×').Append((speedTenths / 10).ToString()).Append('.').Append((speedTenths % 10).ToString());
                _speedLabelTmp.text = _sb.ToString();
            }

            // Live DPS • RPM • KILLS telemetry (zero-alloc SetCharArray)
            ref readonly ComputedTowerStats st = ref sim.Stats;
            double critMult = 1.0 + st.CritChance * Math.Max(0.0, st.CritFactor - 1.0);
            double multiMult = 1.0 + st.MultishotChance * Math.Max(0, st.MultishotTargets - 1);
            double liveDps = st.Damage * st.AttackSpeed * critMult * multiMult;
            int liveRpm = Mathf.RoundToInt((72f + st.OrbAngularSpeedRad * 38f) * session.EffectiveTimeScale * (sim.GoldenTower.Active ? 1.35f : 1.0f));
            UiFormat.SetCombatTelemetry(_telemetryTmp, liveDps, liveRpm, sim.RunTotalKills, ref _cachedTelemetryKey);

            UiFormat.SetCompactDouble(_curPills[0].ValueTmp, "$", session.RunCash, ref _cachedCash);
            UiFormat.SetCompactDouble(_curPills[1].ValueTmp, "", session.Coins, ref _cachedCoins);
            UiFormat.SetInt(_curPills[2].ValueTmp, "", session.Gems, ref _cachedGems);
            UiFormat.SetInt(_curPills[3].ValueTmp, "", session.Stones, ref _cachedStones);
            UiFormat.SetInt(_curPills[4].ValueTmp, "", session.EliteCells, ref _cachedCells);

            // 2. Synergy / UW Banner
            TickSynergyBanner(session, sim);

            // 3. Toasts & Run 1 W3 FTUE Prototype Thorns Banner
            TickToasts(session, sim, unscaledDt);

            // 4. Diegetic Core HP + Regen Ring (concentric in FullScreenArenaOverlay!)
            TickCoreHpRing(sim, unscaledDt);

            // 5. Pooled <=6 Damage Numbers
            _dmgNumbers.Tick(sim, unscaledDt);

            // 6. Floating Gem Drone (+5) Button
            TickGemDroneButton(sim);

            // 7. Bottom Combat Upgrade Deck + FTUE Arrow
            TickDeck(session, sim);
        }

        // --- Keyboard Hotkey Entry Points ---

        public void SelectCombatTabByIndex(int index)
        {
            if (index == 0) SelectTab(StatTab.Attack);
            else if (index == 1) SelectTab(StatTab.Defense);
            else if (index == 2) SelectTab(StatTab.Utility);
        }

        public void TriggerCycleMultiplier() => OnClickCycleMultiplier();
        public void TriggerCycleSpeed() => OnClickSpeedToggle();
        public void TriggerRetreatToHangar() => OnClickRetreatToHangar();

        public void RefreshMuteVisual(bool force)
        {
            if (_muteBtnBg == null || _muteLabelTmp == null) return;
            int state = UiStyle.IsMuted ? 1 : 0;
            if (!force && state == _cachedMuteState) return;
            _cachedMuteState = state;

            bool muted = state == 1;
            _muteBtnBg.material = muted ? _style.MatTabInactive : _style.MatPill;
            _muteLabelTmp.color = muted ? UiStyle.TextMuted : UiStyle.TextCyan;
            _muteLabelTmp.text = muted ? "ЗВУК ВЫКЛ" : "ЗВУК ВКЛ";
        }

        // --- UI Construction ---

        private void BuildArenaOverlay()
        {
            // Diegetic Rotor HP Ring inside _arenaOverlay (outside SafeAreaRoot!)
            _hpRingRoot = UiFactory.CreateRect(
                "CoreHpRingRoot", _arenaOverlay,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 180f), new Vector2(264f, 264f));

            UiFactory.CreateSdfImage(
                "RadialRing", _hpRingRoot, _style.MatCoreHpRing, new Color(0.1f, 0.8f, 0.5f, 0.45f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, raycast: false);

            Image hpPlate = UiFactory.CreateSdfImage(
                "HpReadoutPlate", _hpRingRoot, _style.MatPill, new Color(0.06f, 0.09f, 0.14f, 0.90f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -152f), new Vector2(316f, 64f), raycast: false);
            _hpPlateRect = hpPlate.rectTransform;

            _hpNumericTmp = UiFactory.CreateTmp(
                "HpNumeric", hpPlate.transform, _style.Font, "22 / 22  +0.2/с",
                20f, UiStyle.TextPrimary, TextAlignmentOptions.Center,
                new Vector2(0f, 0.42f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-10f, 0f));
            _hpNumericTmp.fontStyle = FontStyles.Bold;

            _hpArmorSubTmp = UiFactory.CreateTmp(
                "HpArmorSub", hpPlate.transform, _style.Font, "РОТОР ШРЕДЕРА",
                15f, UiStyle.TextCyan, TextAlignmentOptions.Center,
                new Vector2(0f, 0f), new Vector2(1f, 0.48f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-10f, 0f));

            // <= 6 Pooled Damage Numbers inside _arenaOverlay
            _dmgNumbers.Build(_arenaOverlay, _canvas, _style, _worldCam);

            // Interactive Floating Gem Drone (+5 Gems) Button inside _arenaOverlay
            _gemDroneBtn = UiFactory.CreateSdfButton(
                "GemDroneBtn", _arenaOverlay, _style, _style.MatBuyCyan, new Color(0.10f, 0.55f, 0.78f, 0.96f),
                "", 20f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(240f, 260f), new Vector2(218f, 74f),
                OnClickGemDrone, out _, out TextMeshProUGUI droneLbl);

            _gemDroneBtnRect = _gemDroneBtn.GetComponent<RectTransform>();
            UiFactory.CreateIcon(
                "DroneIcon", _gemDroneBtnRect, _style.GetIcon(UiIcons.BotGemDrone),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(50f, 50f));

            droneLbl.rectTransform.offsetMin = new Vector2(66f, 4f);
            droneLbl.rectTransform.offsetMax = new Vector2(-10f, -4f);
            droneLbl.alignment = TextAlignmentOptions.Left;
            droneLbl.fontStyle = FontStyles.Bold;
            droneLbl.text = "ДРОН\n<color=#52E5FF>+5 ГЕМОВ</color>";
            _gemDroneBtnRect.gameObject.SetActive(false);
        }

        private void BuildTopTelemetryBar()
        {
            Image topPanel = UiFactory.CreateSdfImage(
                "TopTelemetryBar", _combatLayer, _style.MatTopHud, new Color(0.09f, 0.12f, 0.17f, 0.95f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(1044f, 226f), raycast: false);
            _topPanelRect = topPanel.rectTransform;

            Transform top = topPanel.transform;

            Image tierPill = UiFactory.CreateSdfImage(
                "TierBadge", top, _style.MatPill, new Color(0.12f, 0.18f, 0.26f, 0.95f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -14f), new Vector2(124f, 70f));
            _tierPillRect = tierPill.rectTransform;

            _tierBadgeTmp = UiFactory.CreateTmp(
                "TierText", tierPill.transform, _style.Font, "ТИР 1",
                23f, UiStyle.TextCyan, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _tierBadgeTmp.fontStyle = FontStyles.Bold;

            Image waveBox = UiFactory.CreateSdfImage(
                "WaveProgressBox", top, _style.MatWaveBar, new Color(0.11f, 0.16f, 0.24f, 0.96f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(150f, -14f), new Vector2(382f, 70f));
            _waveBoxRect = waveBox.rectTransform;

            _waveTitleTmp = UiFactory.CreateTmp(
                "WaveTitle", waveBox.transform, _style.Font, "ВОЛНА 1",
                23f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 0.22f), new Vector2(0.73f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(10f, 0f), new Vector2(-12f, 0f));
            _waveTitleTmp.fontStyle = FontStyles.Bold;

            _waveSubTmp = UiFactory.CreateTmp(
                "WaveSub", waveBox.transform, _style.Font, "БОЙ 26с",
                17.5f, UiStyle.TextSecondary, TextAlignmentOptions.Right,
                new Vector2(0.68f, 0.22f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(-12f, 0f));

            // Live DPS • RPM • KILLS Industrial Telemetry Strip
            Image telStrip = UiFactory.CreateSdfImage(
                "LiveTelemetryStrip", top, _style.MatHeaderStrip, new Color(0.09f, 0.13f, 0.19f, 0.95f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -92f), new Vector2(1012f, 44f));
            _telemetryStripRect = telStrip.rectTransform;

            _telemetryTmp = UiFactory.CreateTmp(
                "TelemetryVal", _telemetryStripRect, _style.Font, "⚡ DPS 4.1  •  ОБОРОТЫ 140 RPM  •  УБИТО 0",
                19f, UiStyle.TextCyan, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-18f, -2f));
            _telemetryTmp.fontStyle = FontStyles.Bold;

            _speedBtn = UiFactory.CreateSdfButton(
                "SpeedBtn", top, _style, _style.MatBuyCyan, new Color(0.12f, 0.45f, 0.65f, 0.96f),
                "×1.0", 24f, UiStyle.TextPrimary,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(542f, -14f), new Vector2(138f, 70f),
                OnClickSpeedToggle, out _, out _speedLabelTmp);
            _speedLabelTmp.fontStyle = FontStyles.Bold;
            _speedBtnRect = _speedBtn.GetComponent<RectTransform>();

            _muteBtn = UiFactory.CreateSdfButton(
                "MuteBtn", top, _style, _style.MatPill, new Color(0.11f, 0.16f, 0.22f, 0.96f),
                "ЗВУК ВКЛ", 18f, UiStyle.TextCyan,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(690f, -14f), new Vector2(146f, 70f),
                () =>
                {
                    UiStyle.ToggleMute();
                    RefreshMuteVisual(force: true);
                    _tweens.PunchScale(_muteBtnRect, 0.08f, 0.14f);
                }, out _muteBtnBg, out _muteLabelTmp);
            _muteLabelTmp.fontStyle = FontStyles.Bold;
            _muteBtnRect = _muteBtn.GetComponent<RectTransform>();

            _retreatBtn = UiFactory.CreateSdfButton(
                "HangarRetreatBtn", top, _style, _style.MatDangerBtn, new Color(0.45f, 0.15f, 0.15f, 0.96f),
                "АНГАР", 22f, UiStyle.TextPrimary,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(846f, -14f), new Vector2(182f, 70f),
                OnClickRetreatToHangar, out _, out _retreatLabelTmp);
            _retreatLabelTmp.fontStyle = FontStyles.Bold;
            _retreatBtnRect = _retreatBtn.GetComponent<RectTransform>();

            // 5 Currency Pills ($ Cash, Coins, Gems, Stones, Cells)
            _curPills[0] = BuildCurrencyPill(top, 16f, 222f, UiIcons.Cash, "$35", UiStyle.TextEmerald);
            _curPills[1] = BuildCurrencyPill(top, 248f, 210f, UiIcons.Coins, "0", UiStyle.TextAmber);
            _curPills[2] = BuildCurrencyPill(top, 468f, 182f, UiIcons.Gems, "0", UiStyle.TextCyan);
            _curPills[3] = BuildCurrencyPill(top, 660f, 176f, UiIcons.Stones, "0", new Color(0.45f, 0.98f, 0.72f, 1f));
            _curPills[4] = BuildCurrencyPill(top, 846f, 182f, UiIcons.Cells, "0", new Color(0.84f, 0.62f, 1.00f, 1f));

            // Active UW / Synergy Overdrive Strip
            Image synImg = UiFactory.CreateSdfImage(
                "SynergyBanner", _combatLayer, _style.MatSynergyBanner, new Color(0.28f, 0.18f, 0.04f, 0.95f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -248f), new Vector2(880f, 54f), raycast: false);
            _synergyRect = synImg.rectTransform;
            _synergyTmp = UiFactory.CreateTmp(
                "SynergyText", _synergyRect, _style.Font, "",
                20f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, -4f));
            _synergyTmp.fontStyle = FontStyles.Bold;
            _synergyRect.gameObject.SetActive(false);

            // Milestone / FTUE Toast Banner
            Image toastImg = UiFactory.CreateSdfImage(
                "ToastBanner", _combatLayer, _style.MatToastBanner, new Color(0.22f, 0.16f, 0.06f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -310f), new Vector2(940f, 68f), raycast: false);
            _toastRect = toastImg.rectTransform;
            _toastGroup = toastImg.gameObject.AddComponent<CanvasGroup>();
            _toastTmp = UiFactory.CreateTmp(
                "ToastText", _toastRect, _style.Font, "",
                21f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-26f, -6f));
            _toastTmp.fontStyle = FontStyles.Bold;
            _toastRect.gameObject.SetActive(false);
        }

        private CurrencyPillRefs BuildCurrencyPill(Transform parent, float xLeft, float width, string iconId, string initText, Color textCol)
        {
            Image pill = UiFactory.CreateSdfImage(
                "Cur_" + iconId, parent, _style.MatPill, new Color(0.11f, 0.14f, 0.20f, 0.95f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(xLeft, -144f), new Vector2(width, 68f));

            Image ic = UiFactory.CreateIcon(
                "Icon", pill.transform, _style.GetIcon(iconId),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(44f, 44f));

            TextMeshProUGUI tmp = UiFactory.CreateTmp(
                "Val", pill.transform, _style.Font, initText,
                22f, textCol, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            tmp.rectTransform.offsetMin = new Vector2(58f, 4f);
            tmp.rectTransform.offsetMax = new Vector2(-10f, -4f);
            tmp.fontStyle = FontStyles.Bold;

            return new CurrencyPillRefs
            {
                Rect = pill.rectTransform,
                IconRect = ic.rectTransform,
                ValueTmp = tmp
            };
        }

        private void BuildBottomDeck()
        {
            Image deckBg = UiFactory.CreateSdfImage(
                "BottomUpgradeDeck", _combatLayer, _style.MatDeckBg, new Color(0.08f, 0.10f, 0.14f, 0.98f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(1080f, 1004f), raycast: true);
            _deckRect = deckBg.rectTransform;

            Image hdr = UiFactory.CreateSdfImage(
                "DeckHeader", _deckRect, _style.MatHeaderStrip, new Color(0.13f, 0.18f, 0.25f, 0.96f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 918f), new Vector2(1040f, 72f));
            _deckHeaderRect = hdr.rectTransform;

            _deckTitleTmp = UiFactory.CreateTmp(
                "DeckTitle", hdr.transform, _style.Font, "ПАЛУБА ПРОКАЧКИ • АТАКА",
                22f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(0.62f, 1f), new Vector2(0f, 0.5f),
                new Vector2(18f, 0f), new Vector2(-18f, 0f));
            _deckTitleTmp.fontStyle = FontStyles.Bold;

            _coreGradeTmp = UiFactory.CreateTmp(
                "CoreGradeLabel", hdr.transform, _style.Font, "G0 РЖАВЫЙ РОТОР",
                19f, UiStyle.TextAmber, TextAlignmentOptions.Right,
                new Vector2(0.58f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(-18f, 0f));
            _coreGradeTmp.fontStyle = FontStyles.Bold;

            _uwQuickBtn = UiFactory.CreateSdfButton(
                "UwQuickBtn", hdr.transform, _style, _style.MatBuyGreen, new Color(0.14f, 0.62f, 0.36f, 0.98f),
                "⚡ ВЫБРАТЬ УЛЬТУ!", 18f, UiStyle.TextPrimary,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(280f, 56f),
                OnClickUwQuick, out _, out _uwQuickLabel);
            _uwQuickLabel.fontStyle = FontStyles.Bold;
            _uwQuickRect = _uwQuickBtn.GetComponent<RectTransform>();
            _uwQuickRect.gameObject.SetActive(false);

            for (int i = 0; i < MaxDeckCards; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float cx = col == 0 ? -264f : 264f;
                float cy = 838f - row * 142f;
                _cards[i] = BuildStatCardSlot(i, cx, cy);
            }

            BuildTabButton("TabAttack", -392f, 250f, UiIcons.TabAttack, "АТАКА", () => SelectTab(StatTab.Attack),
                out _tabAttackBg, out _tabAttackRect, out _tabAttackIconRect, out _tabAttackLbl);
            BuildTabButton("TabDefense", -132f, 250f, UiIcons.TabDefense, "ЗАЩИТА", () => SelectTab(StatTab.Defense),
                out _tabDefenseBg, out _tabDefenseRect, out _tabDefenseIconRect, out _tabDefenseLbl);
            BuildTabButton("TabUtility", 128f, 250f, UiIcons.TabUtility, "УТИЛИТИ", () => SelectTab(StatTab.Utility),
                out _tabUtilityBg, out _tabUtilityRect, out _tabUtilityIconRect, out _tabUtilityLbl);

            _multBtn = UiFactory.CreateSdfButton(
                "BuyMultBtn", _deckRect, _style, _style.MatBuyCyan, new Color(0.12f, 0.45f, 0.65f, 0.96f),
                BuyMultLabels[0], 23f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(390f, 32f), new Vector2(246f, 140f),
                OnClickCycleMultiplier, out _, out _multLabelTmp);
            _multLabelTmp.fontStyle = FontStyles.Bold;
            _multBtnRect = _multBtn.GetComponent<RectTransform>();

            // FTUE Pulsing Chevron Arrow (_ShapeMode = 2)
            Image arrowImg = UiFactory.CreateSdfImage(
                "FtueDefenseArrow", _deckRect, _style.MatFtueArrowHud, UiStyle.TextAmber,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-132f, 178f), new Vector2(88f, 76f), raycast: false);
            _ftueArrowRect = arrowImg.rectTransform;

            Image callout = UiFactory.CreateSdfImage(
                "CalloutPlate", _ftueArrowRect, _style.MatToastBanner, new Color(0.28f, 0.18f, 0.04f, 0.98f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(248f, 42f), raycast: false);

            _ftueArrowTmp = UiFactory.CreateTmp(
                "CalloutText", callout.transform, _style.Font, "КАЧАЙТЕ ЗУБЬЯ!",
                18f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-10f, -2f));
            _ftueArrowTmp.fontStyle = FontStyles.Bold;
            _ftueArrowRect.gameObject.SetActive(false);
        }

        private StatCardWidget BuildStatCardSlot(int slotIndex, float centerX, float centerY)
        {
            var w = new StatCardWidget
            {
                CachedTotalLv = -1,
                CachedBuyCount = -1,
                CachedCostKey = -1,
                CachedAffordable = false
            };

            w.CardBg = UiFactory.CreateSdfImage(
                "StatCard_" + slotIndex, _deckRect, _style.MatStatCard, new Color(0.14f, 0.18f, 0.24f, 0.97f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(centerX, centerY), new Vector2(512f, 134f), raycast: true);
            w.Root = w.CardBg.rectTransform;

            int captured = slotIndex;
            w.CardWholeBtn = w.CardBg.gameObject.AddComponent<Button>();
            w.CardWholeBtn.targetGraphic = w.CardBg;
            w.CardWholeBtn.transition = Selectable.Transition.None;
            w.CardWholeBtn.onClick.AddListener(() => OnClickBuyStatSlot(captured));
            UiFactory.AttachHover(w.Root, 1.025f, 0.975f);

            w.Icon = UiFactory.CreateIcon(
                "Icon", w.Root, null,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -14f), new Vector2(58f, 58f));
            w.IconRect = w.Icon.rectTransform;

            w.LevelBadge = UiFactory.CreateTmp(
                "LvBadge", w.Root, _style.Font, "Ур. 0",
                18f, UiStyle.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(10f, 12f), new Vector2(72f, 34f));

            w.Title = UiFactory.CreateTmp(
                "Title", w.Root, _style.Font, "Параметр",
                22f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, Vector2.zero);
            w.Title.rectTransform.offsetMin = new Vector2(84f, -52f);
            w.Title.rectTransform.offsetMax = new Vector2(-14f, -10f);
            w.Title.fontStyle = FontStyles.Bold;

            w.ValueTransition = UiFactory.CreateTmp(
                "Values", w.Root, _style.Font, "0 → 1",
                20f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                Vector2.zero, Vector2.zero);
            w.ValueTransition.rectTransform.offsetMin = new Vector2(84f, 14f);
            w.ValueTransition.rectTransform.offsetMax = new Vector2(-204f, 68f);

            w.BuyBtn = UiFactory.CreateSdfButton(
                "BuyBtn", w.Root, _style, _style.MatBuyAmber, new Color(0.92f, 0.60f, 0.10f, 0.98f),
                "$10", 22f, UiStyle.TextButtonDark,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-12f, 12f), new Vector2(184f, 64f),
                () => OnClickBuyStatSlot(captured), out w.BuyBg, out w.BuyLabel);
            w.BuyRect = w.BuyBtn.GetComponent<RectTransform>();
            w.BuyLabel.fontStyle = FontStyles.Bold;

            return w;
        }

        private void BuildTabButton(
            string name, float centerX, float width, string iconId, string title,
            UnityAction onClick, out Image bgOut, out RectTransform rectOut, out RectTransform iconRectOut, out TextMeshProUGUI lblOut)
        {
            Button btn = UiFactory.CreateSdfButton(
                name, _deckRect, _style, _style.MatTabInactive, new Color(0.12f, 0.16f, 0.22f, 0.96f),
                title, 22f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(centerX, 32f), new Vector2(width, 140f),
                onClick, out bgOut, out lblOut);

            rectOut = btn.GetComponent<RectTransform>();
            Image ic = UiFactory.CreateIcon(
                "TabIcon", rectOut, _style.GetIcon(iconId),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(56f, 56f));
            iconRectOut = ic.rectTransform;

            lblOut.alignment = TextAlignmentOptions.Bottom;
            lblOut.rectTransform.offsetMin = new Vector2(8f, 16f);
            lblOut.rectTransform.offsetMax = new Vector2(-8f, -68f);
            lblOut.fontStyle = FontStyles.Bold;
        }

        private void BuildUwChoiceModal(Transform modalParent)
        {
            _uwModalRoot = UiFactory.CreateStretchRect("UwChoiceModal", modalParent);
            var backdrop = _uwModalRoot.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.04f, 0.07f, 0.84f);
            backdrop.raycastTarget = true;
            _uwModalGroup = _uwModalRoot.gameObject.AddComponent<CanvasGroup>();

            Image win = UiFactory.CreateSdfImage(
                "ModalWindow", _uwModalRoot, _style.MatModalWindow, new Color(0.11f, 0.15f, 0.22f, 0.99f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(960f, 1180f), raycast: true);
            _uwModalWinRect = win.rectTransform;

            TextMeshProUGUI hdr = UiFactory.CreateTmp(
                "Header", win.transform, _style.Font, "⚡ АБСОЛЮТНОЕ ОРУЖИЕ ЯДРА",
                30f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(-40f, 50f));
            hdr.fontStyle = FontStyles.Bold;

            _uwModalSubTmp = UiFactory.CreateTmp(
                "SubHeader", win.transform, _style.Font, "Выберите модуль абсолютного оружия:",
                21f, UiStyle.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -78f), new Vector2(-40f, 38f));

            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                float yTop = -156f - i * 280f;

                Image card = UiFactory.CreateSdfImage(
                    "UwOption_" + i, win.transform, _style.MatCardRare, new Color(0.14f, 0.22f, 0.32f, 0.98f),
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, yTop), new Vector2(880f, 252f), raycast: false);

                UwChoiceRow row = default;
                row.Root = card.rectTransform;
                row.Icon = UiFactory.CreateIcon(
                    "Icon", row.Root, null,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(24f, 0f), new Vector2(114f, 114f));
                row.IconRect = row.Icon.rectTransform;

                row.Title = UiFactory.CreateTmp(
                    "Title", row.Root, _style.Font, "Ульта",
                    26f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                row.Title.rectTransform.offsetMin = new Vector2(160f, -70f);
                row.Title.rectTransform.offsetMax = new Vector2(-24f, -18f);
                row.Title.fontStyle = FontStyles.Bold;

                row.Desc = UiFactory.CreateTmp(
                    "Desc", row.Root, _style.Font, "",
                    20f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                    Vector2.zero, Vector2.zero, wrap: true);
                row.Desc.rectTransform.offsetMin = new Vector2(160f, 84f);
                row.Desc.rectTransform.offsetMax = new Vector2(-26f, -72f);

                row.PickBtn = UiFactory.CreateSdfButton(
                    "PickBtn", row.Root, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                    "ОТКРЫТЬ", 23f, UiStyle.TextButtonDark,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-22f, 18f), new Vector2(280f, 64f),
                    () => OnClickPickUw(slot), out row.PickBg, out row.PickLabel);
                row.PickRect = row.PickBtn.GetComponent<RectTransform>();
                row.PickLabel.fontStyle = FontStyles.Bold;

                _uwRows[i] = row;
            }

            UiFactory.CreateSdfButton(
                "CloseUwModalBtn", win.transform, _style, _style.MatTabInactive, new Color(0.18f, 0.22f, 0.28f, 0.96f),
                "ЗАКРЫТЬ", 22f, UiStyle.TextSecondary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 22f), new Vector2(320f, 68f),
                () =>
                {
                    UiStyle.PlaySfx(SfxId.UiTap);
                    _uwModalRoot.gameObject.SetActive(false);
                }, out _, out _);

            _uwModalRoot.gameObject.SetActive(false);
        }

        private void BuildRunSummaryModal()
        {
            _summaryModalRoot = UiFactory.CreateStretchRect("RunSummaryModal", _root);
            var backdrop = _summaryModalRoot.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.04f, 0.07f, 0.86f);
            backdrop.raycastTarget = true;
            _summaryModalGroup = _summaryModalRoot.gameObject.AddComponent<CanvasGroup>();

            Image win = UiFactory.CreateSdfImage(
                "SummaryWindow", _summaryModalRoot, _style.MatModalWindow, new Color(0.11f, 0.15f, 0.22f, 0.99f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(920f, 880f), raycast: true);
            _summaryWinRect = win.rectTransform;

            _sumTitleTmp = UiFactory.CreateTmp(
                "SummaryTitle", win.transform, _style.Font, "ИТОГИ ЗАБЕГА",
                36f, UiStyle.TextCrimson, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(-40f, 60f));
            _sumTitleTmp.fontStyle = FontStyles.Bold;

            UiFactory.CreateTmp(
                "SummarySub", win.transform, _style.Font, "ОТЧЁТ БОЕВОЙ ТЕЛЕМЕТРИИ",
                20f, UiStyle.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -88f), new Vector2(-40f, 34f));

            Image statsBox = UiFactory.CreateSdfImage(
                "StatsBox", win.transform, _style.MatStatCard, new Color(0.08f, 0.11f, 0.16f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -142f), new Vector2(820f, 420f));
            _summaryStatsBoxRect = statsBox.rectTransform;

            _sumStatsTmp = UiFactory.CreateTmp(
                "SummaryStats", statsBox.transform, _style.Font, "",
                26f, UiStyle.TextPrimary, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-44f, -28f), wrap: true);

            UiFactory.CreateSdfButton(
                "ToWorkshopBtn", win.transform, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                "В МАСТЕРСКУЮ", 25f, UiStyle.TextButtonDark,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-210f, 38f), new Vector2(390f, 102f),
                () =>
                {
                    UiStyle.PlaySfx(SfxId.UiTap);
                    _summaryOpen = false;
                    _summaryModalRoot.gameObject.SetActive(false);
                    _onOpenHangar?.Invoke(0);
                }, out _, out TextMeshProUGUI wsLbl);
            wsLbl.fontStyle = FontStyles.Bold;

            Image arrow = UiFactory.CreateSdfImage(
                "SummaryFtueArrow", win.transform, _style.MatFtueArrowModal, UiStyle.TextAmber,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-210f, 150f), new Vector2(90f, 78f), raycast: false);
            _sumFtueArrowRect = arrow.rectTransform;

            TextMeshProUGUI arrowTip = UiFactory.CreateTmp(
                "Tip", _sumFtueArrowRect, _style.Font, "ОТКРОЙТЕ ЗУБЬЯ!",
                19f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 2f), new Vector2(280f, 34f));
            arrowTip.fontStyle = FontStyles.Bold;

            UiFactory.CreateSdfButton(
                "NewRunBtn", win.transform, _style, _style.MatBuyGreen, UiStyle.TextEmerald,
                "▶ НОВЫЙ ЗАБЕГ", 25f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(210f, 38f), new Vector2(390f, 102f),
                () =>
                {
                    if (_boundSession == null) return;
                    UiStyle.PlaySfx(SfxId.UiUnlock);
                    _summaryOpen = false;
                    _summaryModalRoot.gameObject.SetActive(false);
                    if (_arenaOverlay != null) _arenaOverlay.gameObject.SetActive(true);
                    _dmgNumbers.ClearAll();
                    _boundSession.StartNewRun(_boundSession.ActiveTier);
                }, out _, out TextMeshProUGUI retryLbl);
            retryLbl.fontStyle = FontStyles.Bold;

            _summaryModalRoot.gameObject.SetActive(false);
        }

        private void TickSynergyBanner(GameSession session, CombatSim sim)
        {
            bool gtAct = sim.GoldenTower.Unlocked && sim.GoldenTower.Active;
            bool bhAct = sim.BlackHole.Unlocked && sim.BlackHole.Active;
            bool drAct = sim.DeathRay.Equipped && sim.DeathRay.Active;
            bool anyUw = session.UwUnlockedCount > 0 || session.MoltenCoreSkinEquipped;

            if (!gtAct && !bhAct && !drAct && !anyUw)
            {
                if (_synergyRect.gameObject.activeSelf) _synergyRect.gameObject.SetActive(false);
                return;
            }

            if (!_synergyRect.gameObject.activeSelf) _synergyRect.gameObject.SetActive(true);

            int remGt = Mathf.CeilToInt(sim.GoldenTower.TimeRemaining);
            int remBh = Mathf.CeilToInt(sim.BlackHole.TimeRemaining);
            int cdGt = Mathf.CeilToInt(sim.GoldenTower.CycleTimer);
            int cdBh = Mathf.CeilToInt(sim.BlackHole.CycleTimer);

            int hash = (gtAct ? 100000 : 0) + (bhAct ? 200000 : 0) + (drAct ? 400000 : 0)
                + (session.MoltenCoreSkinEquipped ? 800000 : 0)
                + remGt * 97 + remBh * 31 + (gtAct || bhAct ? 0 : (cdGt * 7 + cdBh));

            if (hash != _cachedSynergyHash)
            {
                _cachedSynergyHash = hash;
                _sb.Clear();

                if (gtAct && bhAct)
                {
                    _sb.Append("⚡ СИНХРОН ОВЕРДРАЙВ: GT ×")
                       .Append(sim.GoldenTower.Multiplier.ToString("0.0", CultureInfo.InvariantCulture))
                       .Append(" + ЧЁРНАЯ ДЫРА (").Append(Mathf.Max(1, remGt)).Append("с)");
                }
                else if (gtAct)
                {
                    _sb.Append("⚡ ЗОЛОТАЯ ПЛАВИЛЬНЯ АКТИВНА ×")
                       .Append(sim.GoldenTower.Multiplier.ToString("0.0", CultureInfo.InvariantCulture))
                       .Append(" (").Append(Mathf.Max(1, remGt)).Append("с)");
                }
                else if (bhAct)
                {
                    _sb.Append("⚡ ЭЛЕКТРОМАГНИТ BH СТЯГИВАЕТ РОЙ (").Append(Mathf.Max(1, remBh)).Append("с)");
                }
                else if (drAct)
                {
                    _sb.Append("⚡ ПЛАЗМЕННЫЙ ЛУЧ СМЕРТИ ВРАЩАЕТСЯ!");
                }
                else
                {
                    if (sim.GoldenTower.Unlocked)
                        _sb.Append("GT КД: ").Append(Mathf.Max(0, cdGt)).Append("с  ");
                    if (sim.BlackHole.Unlocked)
                        _sb.Append("BH КД: ").Append(Mathf.Max(0, cdBh)).Append("с  ");
                    if (session.UwUnlocked[(int)UwId.ChainLightning])
                        _sb.Append("• ЦЕПНАЯ ДУГА АКТИВНА  ");
                    if (session.MoltenCoreSkinEquipped)
                        _sb.Append("• MOLTEN CORE +2%");
                }
                _synergyTmp.text = _sb.ToString();
            }

            if (_style.MatSynergyBanner != null)
            {
                float fill = (gtAct || bhAct) ? Mathf.Clamp01(Mathf.Max(sim.GoldenTower.TimeRemaining, sim.BlackHole.TimeRemaining) / 20f) : 0f;
                _style.MatSynergyBanner.SetFloat(UiStyle.PropFill, fill);
                _style.MatSynergyBanner.SetFloat(UiStyle.PropGlow, (gtAct || bhAct) ? 1.15f : 0.25f);
            }
        }

        private void TickToasts(GameSession session, CombatSim sim, float unscaledDt)
        {
            if (sim.FtueThornsToastPending)
            {
                sim.FtueThornsToastPending = false;
                ShowToast("▲ ПРОТОТИП ЗУБЬЕВ (8%) СРАБОТАЛ! ОТКРОЙТЕ ВКЛАДКУ [ЗАЩИТА]!", 5.2f);
                UiStyle.PlaySfx(SfxId.UiUnlock);
            }
            else if (_toastTimer <= 0.15f && session.ToastCount > 0)
            {
                MilestoneToast ms = session.ToastQueue[0];
                for (int i = 1; i < session.ToastCount; i++)
                {
                    session.ToastQueue[i - 1] = session.ToastQueue[i];
                }
                session.ToastCount--;

                _sb.Clear();
                _sb.Append("★ ВЕХА W").Append(ms.Wave).Append(": ");
                if (!string.IsNullOrEmpty(ms.TextRu))
                {
                    for (int k = 0; k < ms.TextRu.Length; k++)
                    {
                        char ch = ms.TextRu[k];
                        _sb.Append((ch == '\u00AB' || ch == '\u00BB') ? '"' : ch);
                    }
                }
                if (ms.Coins > 0) _sb.Append("  +").Append(ms.Coins).Append(" монет");
                if (ms.Gems > 0) _sb.Append("  +").Append(ms.Gems).Append(" гемов");
                if (ms.Stones > 0) _sb.Append("  +").Append(ms.Stones).Append(" камней");

                ShowToast(_sb.ToString(), 3.6f);
                UiStyle.PlaySfx(SfxId.UiUnlock);
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= unscaledDt;
                if (_toastTimer <= 0f)
                {
                    _toastRect.gameObject.SetActive(false);
                }
                else if (_toastTimer < 0.35f)
                {
                    _toastGroup.alpha = Mathf.Clamp01(_toastTimer / 0.35f);
                }
            }
        }

        public void ShowToast(string message, float duration = 3.0f)
        {
            if (_toastRect == null) return;
            _toastTmp.text = message;
            _toastTimer = duration;
            _toastGroup.alpha = 1f;
            _toastRect.gameObject.SetActive(true);
            _tweens.SlideAnchoredY(_toastRect, _toastTargetY + 34f, _toastTargetY, 0.20f);
        }

        private void TickCoreHpRing(CombatSim sim, float unscaledDt)
        {
            // Project 3D Rotor Core hub (0, 0.42, 0) onto FullScreenArenaOverlay (outside SafeAreaRoot!)
            Vector2 coreUiPos = UiScreenSpace.ProjectWorldToOverlay(
                _worldCam, _canvas, _arenaOverlay, new Vector3(0f, 0.42f, 0f));
            _hpRingRoot.anchoredPosition = coreUiPos;

            float maxHp = (float)Math.Max(1.0, sim.CoreMaxHp);
            float hpRatio = Mathf.Clamp01((float)(sim.CoreHp / maxHp));

            if (hpRatio > _displayedTrailRatio)
            {
                _displayedTrailRatio = hpRatio;
            }
            else
            {
                _displayedTrailRatio = Mathf.MoveTowards(_displayedTrailRatio, hpRatio, 0.45f * unscaledDt);
            }

            if (_style.MatCoreHpRing != null)
            {
                _style.MatCoreHpRing.SetFloat(UiStyle.PropFill, hpRatio);
                _style.MatCoreHpRing.SetFloat(UiStyle.PropTrailAmt, _displayedTrailRatio);
            }

            UiFormat.SetHpReadout(_hpNumericTmp, sim.CoreHp, sim.CoreMaxHp, sim.Stats.HealthRegen, ref _cachedHpKey);

            long armorKey = (sim.AnyVampireBeaming ? (1L << 40) : 0L)
                ^ ((long)Math.Round(sim.Stats.DefAbs) * 1009L)
                ^ (long)Math.Round(sim.Stats.DefPercent * 1000.0);

            if (armorKey != _cachedArmorKey)
            {
                _cachedArmorKey = armorKey;
                if (sim.AnyVampireBeaming)
                {
                    _hpArmorSubTmp.color = UiStyle.TextCrimson;
                    _hpArmorSubTmp.text = "⚠ ЛУЧ КЛЕЩА -2% HP/с!";
                }
                else if (sim.Stats.DefAbs > 0.05 || sim.Stats.DefPercent > 0.001)
                {
                    _hpArmorSubTmp.color = UiStyle.TextCyan;
                    _sb.Clear();
                    _sb.Append("БРОНЯ ");
                    UiFormat.AppendCompactToSb(_sb, sim.Stats.DefAbs);
                    _sb.Append(" • ДЕМПФЕР ")
                       .Append((sim.Stats.DefPercent * 100.0).ToString("0.#", CultureInfo.InvariantCulture))
                       .Append('%');
                    _hpArmorSubTmp.text = _sb.ToString();
                }
                else
                {
                    _hpArmorSubTmp.color = UiStyle.TextSecondary;
                    _hpArmorSubTmp.text = "РОТОР ШРЕДЕРА";
                }
            }
        }

        private void TickGemDroneButton(CombatSim sim)
        {
            if (!sim.GemDrone.Active)
            {
                if (_gemDroneBtnRect.gameObject.activeSelf)
                {
                    _gemDroneBtnRect.gameObject.SetActive(false);
                }
                return;
            }

            if (!_gemDroneBtnRect.gameObject.activeSelf)
            {
                _gemDroneBtnRect.gameObject.SetActive(true);
            }

            Vector2 dronePos = _dmgNumbers.ProjectSimToFullScreen(sim.GemDrone.PosX, sim.GemDrone.PosY, 1.5f);
            float bob = Mathf.Sin(Time.unscaledTime * 4.2f) * 6f;

            if (_isLandscape)
            {
                dronePos.x = Mathf.Clamp(dronePos.x, -740f, 740f);
                dronePos.y = Mathf.Clamp(dronePos.y + 38f + bob, -160f, 360f);
            }
            else
            {
                dronePos.x = Mathf.Clamp(dronePos.x, -400f, 400f);
                dronePos.y = Mathf.Clamp(dronePos.y + 44f + bob, -120f, 840f);
            }
            _gemDroneBtnRect.anchoredPosition = dronePos;
        }

        private void TickDeck(GameSession session, CombatSim sim)
        {
            int gradeInt = (int)session.GetVisualCoreGrade();
            bool uwReady = session.UwSystemUnlocked
                && session.UwUnlockedCount < GameIdsAndScale.UW_COUNT
                && session.Stones >= session.GetNextUwUnlockStoneCost();

            if (_uwQuickRect.gameObject.activeSelf != uwReady)
            {
                _uwQuickRect.gameObject.SetActive(uwReady);
                _coreGradeTmp.gameObject.SetActive(!uwReady);
            }

            if (gradeInt != _cachedHeaderGrade || _activeTab != _cachedHeaderTab)
            {
                _cachedHeaderGrade = gradeInt;
                _cachedHeaderTab = _activeTab;

                if (_isLandscape)
                {
                    _deckTitleTmp.text = _activeTab == StatTab.Attack
                        ? "ПАЛУБА • АТАКА   [1-3] Вкладки  [Q] Кратность  [Space] Скорость  [Tab] Ангар  [M] Звук"
                        : (_activeTab == StatTab.Defense
                            ? "ПАЛУБА • ЗАЩИТА   [1-3] Вкладки  [Q] Кратность  [Space] Скорость  [Tab] Ангар  [M] Звук"
                            : "ПАЛУБА • УТИЛИТИ   [1-3] Вкладки  [Q] Кратность  [Space] Скорость  [Tab] Ангар  [M] Звук");
                }
                else
                {
                    _deckTitleTmp.text = _activeTab == StatTab.Attack ? "ПАЛУБА ПРОКАЧКИ • АТАКА"
                        : (_activeTab == StatTab.Defense ? "ПАЛУБА ПРОКАЧКИ • ЗАЩИТА" : "ПАЛУБА ПРОКАЧКИ • УТИЛИТИ");
                }

                _coreGradeTmp.text = gradeInt switch
                {
                    0 => "G0 РЖАВЫЙ РОТОР",
                    1 => "G1 КАРБИДНЫЕ ЗУБЬЯ",
                    2 => "G2 ТИТАНОВЫЕ ПИЛЫ",
                    _ => "G3 АЛМАЗНЫЙ РЕАКТОР"
                };
            }

            // Count & populate unlocked stats for _activeTab
            int slot = 0;
            int thornsSlotIdx = -1;
            int healthSlotIdx = -1;

            for (int i = 0; i < GameIdsAndScale.STAT_COUNT && slot < MaxDeckCards; i++)
            {
                ref readonly StatDef def = ref BalanceTables.Stats[i];
                if (def.Tab != _activeTab) continue;

                StatId sid = def.Id;
                bool unlocked = session.IsStatUnlocked(sid);

                bool showRun1ThornsPreview = !unlocked
                    && sid == StatId.Thorns
                    && _activeTab == StatTab.Defense
                    && (session.TotalRunsStarted <= 1 || !session.WorkshopUnlocked);

                if (!unlocked && !showRun1ThornsPreview) continue;

                if (sid == StatId.Thorns && unlocked) thornsSlotIdx = slot;
                if (sid == StatId.Health) healthSlotIdx = slot;

                BindAndRefreshStatCard(ref _cards[slot], session, sid, showRun1ThornsPreview);
                slot++;
            }

            _visibleCardCount = slot;
            LayoutStatCardsForCurrentMode(Math.Max(1, _visibleCardCount));

            for (int i = slot; i < MaxDeckCards; i++)
            {
                if (_cards[i].Root.gameObject.activeSelf)
                {
                    _cards[i].Root.gameObject.SetActive(false);
                }
            }

            // FTUE Pulsing Arrow Logic
            bool thornsNeedsFirstBuy = session.IsStatUnlocked(StatId.Thorns) && session.GetTotalStatLevel(StatId.Thorns) == 0;
            bool run1W3Guide = session.TotalRunsStarted == 1 && (sim.CurrentWave >= 3 || sim.PrototypeThornsTriggered) && !_run1DefensePurchased;
            bool showFtueArrow = thornsNeedsFirstBuy || run1W3Guide;

            if (!showFtueArrow)
            {
                if (_ftueArrowRect.gameObject.activeSelf) _ftueArrowRect.gameObject.SetActive(false);
                return;
            }

            if (!_ftueArrowRect.gameObject.activeSelf) _ftueArrowRect.gameObject.SetActive(true);
            float bobY = Mathf.Sin(Time.unscaledTime * 7.5f) * 6f;

            if (_activeTab != StatTab.Defense)
            {
                _ftueArrowTmp.text = "ЗАЩИТА → ЗУБЬЯ!";
                if (_isLandscape)
                {
                    _ftueArrowRect.anchoredPosition = new Vector2(-590f, 186f + bobY);
                }
                else
                {
                    _ftueArrowRect.anchoredPosition = new Vector2(-132f, 174f + bobY);
                }
            }
            else
            {
                int targetSlot = thornsSlotIdx >= 0 ? thornsSlotIdx : healthSlotIdx;
                if (targetSlot >= 0 && targetSlot < _visibleCardCount)
                {
                    Vector2 cardPos = _cards[targetSlot].Root.anchoredPosition;
                    _ftueArrowTmp.text = thornsSlotIdx >= 0 ? "КАЧАЙТЕ ЗУБЬЯ!" : "УКРЕПИТЕ ЯДРО!";
                    if (_isLandscape)
                    {
                        float deckCenterX = -710f + cardPos.x + _cards[targetSlot].Root.sizeDelta.x * 0.5f;
                        _ftueArrowRect.anchoredPosition = new Vector2(deckCenterX, cardPos.y + 108f + bobY);
                    }
                    else
                    {
                        _ftueArrowRect.anchoredPosition = new Vector2(cardPos.x + 120f, cardPos.y + 42f + bobY);
                    }
                }
                else
                {
                    _ftueArrowRect.gameObject.SetActive(false);
                }
            }
        }

        private void BindAndRefreshStatCard(ref StatCardWidget w, GameSession session, StatId id, bool previewLocked)
        {
            if (!w.Root.gameObject.activeSelf)
            {
                w.Root.gameObject.SetActive(true);
            }

            ref readonly StatDef def = ref BalanceTables.Stats[(int)id];

            if (w.BoundStat != id || w.IsPreviewLocked != previewLocked || w.CachedTotalLv < 0)
            {
                w.BoundStat = id;
                w.IsPreviewLocked = previewLocked;
                w.Icon.sprite = _style.GetIcon(UiIcons.StatIcons[(int)id]);
                w.Title.text = def.TitleRu;
                w.CachedTotalLv = -999;
                w.CachedCostKey = -999;
            }

            if (previewLocked)
            {
                if (w.CachedTotalLv != -2)
                {
                    w.CachedTotalLv = -2;
                    w.CardBg.material = _style.MatCardLocked;
                    w.LevelBadge.text = "ПРОТОТИП";
                    w.ValueTransition.text = "8% шипы • Ангар W10";
                    w.BuyBg.material = _style.MatBuyDisabled;
                    w.BuyLabel.color = UiStyle.TextMuted;
                    w.BuyLabel.text = "АНГАР W10";
                    w.BuyBtn.interactable = false;
                    w.CardWholeBtn.interactable = false;
                }
                return;
            }

            w.CardBg.material = _style.MatStatCard;
            int totalLv = session.GetTotalStatLevel(id);
            int maxLv = def.MaxLevel;
            bool isMaxed = totalLv >= maxLv;

            ComputeRunPurchasePlan(session, id, totalLv, maxLv, out int buyCount, out double totalCost, out bool canAfford);

            if (totalLv != w.CachedTotalLv || buyCount != w.CachedBuyCount)
            {
                w.CachedTotalLv = totalLv;
                w.CachedBuyCount = buyCount;

                _sb.Clear();
                _sb.Append("Ур. ").Append(totalLv);
                w.LevelBadge.text = _sb.ToString();

                w.ValueTransition.text = UiFormat.BuildStatTransitionText(id, totalLv, buyCount, maxLv);
            }

            long costKey = isMaxed ? -500L : ((long)Math.Floor(totalCost) * 31L + buyCount);
            if (costKey != w.CachedCostKey || canAfford != w.CachedAffordable)
            {
                w.CachedCostKey = costKey;
                w.CachedAffordable = canAfford;

                w.BuyBtn.interactable = !isMaxed && canAfford;
                w.CardWholeBtn.interactable = !isMaxed && canAfford;

                if (isMaxed)
                {
                    w.BuyBg.material = _style.MatBuyDisabled;
                    w.BuyLabel.color = UiStyle.TextCyan;
                    w.BuyLabel.text = "МАКС";
                }
                else
                {
                    w.BuyBg.material = canAfford ? _style.MatBuyAmber : _style.MatBuyDisabled;
                    w.BuyLabel.color = canAfford ? UiStyle.TextButtonDark : UiStyle.TextMuted;

                    int p = 0;
                    if (buyCount > 1)
                    {
                        _buyBtnChars[p++] = '×';
                        p = UiFormat.AppendNonNegInt(_buyBtnChars, p, buyCount);
                        _buyBtnChars[p++] = ' ';
                    }
                    _buyBtnChars[p++] = '$';
                    p = UiFormat.AppendCompactDouble(_buyBtnChars, p, totalCost);
                    w.BuyLabel.SetCharArray(_buyBtnChars, 0, p);
                }
            }
        }

        private void ComputeRunPurchasePlan(
            GameSession session, StatId id, int curLv, int maxLv,
            out int countOut, out double costOut, out bool affordableOut)
        {
            int rem = maxLv - curLv;
            if (rem <= 0)
            {
                countOut = 0;
                costOut = 0.0;
                affordableOut = false;
                return;
            }

            int step = BuyMultSteps[_buyMultIdx];
            if (step > 0)
            {
                int n = Math.Min(step, rem);
                double sum = 0.0;
                for (int k = 0; k < n; k++)
                {
                    sum += BalanceTables.GetCashCost(id, curLv + k);
                }
                countOut = n;
                costOut = sum;
                affordableOut = session.RunCash >= sum - 1e-6;
                return;
            }

            // MAX mode
            double cash = session.RunCash;
            double first = BalanceTables.GetCashCost(id, curLv);
            if (cash < first - 1e-6)
            {
                countOut = 1;
                costOut = first;
                affordableOut = false;
                return;
            }

            int bought = 0;
            double total = 0.0;
            while (bought < rem && bought < 250)
            {
                double next = BalanceTables.GetCashCost(id, curLv + bought);
                if (total + next > cash + 1e-6) break;
                total += next;
                bought++;
            }

            countOut = Math.Max(1, bought);
            costOut = total;
            affordableOut = bought > 0;
        }

        // --- User Interaction Handlers ---

        private void SelectTab(StatTab tab)
        {
            if (_activeTab == tab) return;
            _activeTab = tab;
            ApplyTabVisuals();
            UiStyle.PlaySfx(SfxId.UiTap);

            for (int i = 0; i < MaxDeckCards; i++)
            {
                _cards[i].CachedTotalLv = -1;
                _cards[i].CachedCostKey = -999;
            }
        }

        private void ApplyTabVisuals()
        {
            if (_style.MatTabActive == null || _style.MatTabInactive == null) return;
            _tabAttackBg.material = _activeTab == StatTab.Attack ? _style.MatTabActive : _style.MatTabInactive;
            _tabDefenseBg.material = _activeTab == StatTab.Defense ? _style.MatTabActive : _style.MatTabInactive;
            _tabUtilityBg.material = _activeTab == StatTab.Utility ? _style.MatTabActive : _style.MatTabInactive;
            _tabAttackLbl.color = _activeTab == StatTab.Attack ? UiStyle.TextAmber : UiStyle.TextPrimary;
            _tabDefenseLbl.color = _activeTab == StatTab.Defense ? UiStyle.TextAmber : UiStyle.TextPrimary;
            _tabUtilityLbl.color = _activeTab == StatTab.Utility ? UiStyle.TextAmber : UiStyle.TextPrimary;
        }

        private void OnClickCycleMultiplier()
        {
            _buyMultIdx = (_buyMultIdx + 1) % BuyMultSteps.Length;
            _multLabelTmp.text = BuyMultLabels[_buyMultIdx];
            _tweens.PunchScale(_multBtnRect, 0.09f, 0.15f);
            UiStyle.PlaySfx(SfxId.UiTap);

            for (int i = 0; i < MaxDeckCards; i++)
            {
                _cards[i].CachedBuyCount = -1;
                _cards[i].CachedCostKey = -999;
            }
        }

        private void OnClickBuyStatSlot(int slotIndex)
        {
            if (_boundSession == null || slotIndex < 0 || slotIndex >= _visibleCardCount) return;
            ref StatCardWidget w = ref _cards[slotIndex];
            if (w.IsPreviewLocked) return;

            int curLv = _boundSession.GetTotalStatLevel(w.BoundStat);
            int maxLv = BalanceTables.Stats[(int)w.BoundStat].MaxLevel;
            ComputeRunPurchasePlan(_boundSession, w.BoundStat, curLv, maxLv, out int count, out _, out bool canAfford);

            if (!canAfford || count <= 0) return;

            if (_boundSession.TryBuyRunUpgrade(w.BoundStat, count))
            {
                if (_activeTab == StatTab.Defense)
                {
                    _run1DefensePurchased = true;
                }
                UiStyle.PlaySfx(SfxId.UiUpgrade);
                _tweens.PunchScale(w.Root, 0.08f, 0.16f);
                w.CachedTotalLv = -1;
                w.CachedCostKey = -999;
            }
        }

        private void OnClickSpeedToggle()
        {
            if (_boundSession == null) return;
            float maxSpd = _boundSession.MaxUnlockedGameSpeed;
            if (maxSpd <= 1.01f)
            {
                ShowToast("Форсаж ×1.5 откроется на Волне 5!", 2.2f);
                UiStyle.PlaySfx(SfxId.UiTap);
                return;
            }

            float cur = _boundSession.SelectedGameSpeed;
            float next = 1.0f;
            bool foundHigher = false;
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                if (SpeedSteps[i] > cur + 0.05f && SpeedSteps[i] <= maxSpd + 0.02f)
                {
                    next = SpeedSteps[i];
                    foundHigher = true;
                    break;
                }
            }
            if (!foundHigher && cur < maxSpd - 0.05f)
            {
                next = maxSpd;
            }

            _boundSession.SetSelectedGameSpeed(next);
            _tweens.PunchScale(_speedBtnRect, 0.11f, 0.16f);
            UiStyle.PlaySfx(SfxId.UiTap);
        }

        private void OnClickRetreatToHangar()
        {
            if (_boundSession == null) return;
            UiStyle.PlaySfx(SfxId.UiTap);
            _dmgNumbers.ClearAll();
            _boundSession.EndCurrentRunToHangar();
            _onOpenHangar?.Invoke(0);
        }

        private void OnClickGemDrone()
        {
            if (_boundSession == null) return;
            if (_boundSession.ClaimActiveGemDrone())
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _gemDroneBtnRect.gameObject.SetActive(false);
                ShowToast("◆ ДРОН ПЕРЕХВАЧЕН: +" + BalanceTables.GEM_DRONE_REWARD + " ГЕМОВ!", 2.4f);
            }
        }

        private void OnClickUwQuick()
        {
            if (_boundSession == null) return;
            UiStyle.PlaySfx(SfxId.UiTap);
            OpenUwModal(_boundSession);
        }

        private void OnClickPickUw(int rowSlot)
        {
            if (_boundSession == null || rowSlot < 0 || rowSlot >= 3) return;
            UwId chosen = _uwRows[rowSlot].BoundUw;
            if (_boundSession.TryUnlockUw(chosen))
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _uwModalRoot.gameObject.SetActive(false);
                ShowToast("⚡ ОТКРЫТО АБСОЛЮТНОЕ ОРУЖИЕ: " + BalanceTables.Uws[(int)chosen].TitleRu + "!", 3.8f);
            }
        }

        private static void AnimateFtueBob(RectTransform rt, float baseY)
        {
            if (rt == null || !rt.gameObject.activeSelf) return;
            Vector2 p = rt.anchoredPosition;
            p.y = baseY + Mathf.Sin(Time.unscaledTime * 7.0f) * 8f;
            rt.anchoredPosition = p;
        }
    }
}
