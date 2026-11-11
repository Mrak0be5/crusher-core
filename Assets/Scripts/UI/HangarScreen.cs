using System;
using System.Globalization;
using System.Text;
using Crusher.Audio;
using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Crusher.UI
{
    /// <summary>
    /// Full 1080x2400 portrait Industrial Hangar screen (Crusher.UI):
    /// - Top Header: Shredder Core Grade (G0..G3), Best Wave T1/T2, 4 persistent currencies (Coins, Gems, Stones, Cells).
    /// - Command Bar: Molten Core (+2% Coins) skin toggle, Tier 1/2 selector, primary "▶ В БОЙ!" launch button.
    /// - Persistent Meta Telemetry Strip: simultaneous live overview of Equipped Cards, Lab Slot timers, and Unlocked UWs/Bots.
    /// - 5 Interactive Tabs (bottom thumb bar): МАСТЕРСКАЯ | КАРТЫ | ЛАБЫ | УЛЬТЫ | ВЕХИ.
    /// - Offline Lab Progress Modal (ShowOfflineSummary).
    /// Zero GC allocations on steady frames via char[] SetCharArray + state hash dirty checks.
    /// </summary>
    public sealed class HangarScreen
    {
        private const int TabWorkshop = 0;
        private const int TabCards = 1;
        private const int TabLabs = 2;
        private const int TabUws = 3;
        private const int TabMilestones = 4;
        private const int MainTabCount = 5;

        private const int MaxWorkshopPool = 10;
        private static readonly int[] BuyMultipliers = { 1, 5, 10, -1 }; // -1 = MAX
        private static readonly string[] BuyMultLabels = { "МНОЖ: ×1", "МНОЖ: ×5", "МНОЖ: ×10", "МНОЖ: MAX" };

        private static readonly StatId[][] TabStats = new StatId[3][]
        {
            new[]
            {
                StatId.Damage, StatId.AttackSpeed, StatId.CritChance, StatId.CritFactor,
                StatId.Range, StatId.MultishotChance, StatId.MultishotTargets
            },
            new[]
            {
                StatId.Health, StatId.HealthRegen, StatId.DefenseAbsolute, StatId.DefensePercent,
                StatId.Thorns, StatId.KnockbackChance, StatId.KnockbackForce, StatId.OrbsCount,
                StatId.OrbSpeed, StatId.Lifesteal
            },
            new[]
            {
                StatId.CashBonus, StatId.CashPerWave, StatId.CoinsPerKill, StatId.CoinsPerWave,
                StatId.FreeUpgrades
            }
        };

        private sealed class WorkshopCardWidget
        {
            public RectTransform Root;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI LevelBadge;
            public TextMeshProUGUI ValueText;
            public Button ActionBtn;
            public Image ActionBg;
            public TextMeshProUGUI ActionLabel;
            public StatId BoundStat;
            public long CachedHash = long.MinValue;
        }

        private sealed class EquippedSlotWidget
        {
            public RectTransform Root;
            public Button Btn;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Sub;
            public int CachedHash = int.MinValue;
        }

        private sealed class CardModuleWidget
        {
            public RectTransform Root;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI RarityTag;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI StarsAndCopies;
            public TextMeshProUGUI Desc;
            public Button EquipBtn;
            public Image EquipBg;
            public TextMeshProUGUI EquipLabel;
            public int CachedHash = int.MinValue;
        }

        private sealed class LabSlotWidget
        {
            public RectTransform Root;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI TimerTmp;
            public TextMeshProUGUI BoostTag;
            public Button UnlockBtn;
            public Image UnlockBg;
            public TextMeshProUGUI UnlockLbl;
            public Button Boost2Btn;
            public Image Boost2Bg;
            public Button Boost3Btn;
            public Image Boost3Bg;
            public Button RushBtn;
            public Image RushBg;
            public TextMeshProUGUI RushLbl;
            public int CachedSec = -1;
            public int CachedHash = int.MinValue;
        }

        private sealed class LabResearchWidget
        {
            public RectTransform Root;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI LevelBadge;
            public TextMeshProUGUI Desc;
            public TextMeshProUGUI DurationTmp;
            public Button StartBtn;
            public Image StartBg;
            public TextMeshProUGUI StartLbl;
            public int CachedHash = int.MinValue;
        }

        private sealed class UwReactorWidget
        {
            public RectTransform Root;
            public Image Bg;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI StatusBadge;
            public TextMeshProUGUI Desc;
            public TextMeshProUGUI LiveStats;
            public Button UpgradeBtn;
            public Image UpgradeBg;
            public TextMeshProUGUI UpgradeLbl;
            public int CachedHash = int.MinValue;
        }

        private sealed class MilestoneCardWidget
        {
            public RectTransform Root;
            public Image Bg;
            public TextMeshProUGUI WaveBadge;
            public TextMeshProUGUI StatusTag;
            public TextMeshProUGUI BannerTxt;
            public TextMeshProUGUI RewardTxt;
            public int CachedState = -1;
        }

        private UiStyle _style;
        private TweenRunner _tweens;
        private Action _onStartRun;
        private Action _onRequestUwModal;
        private GameSession _boundSession;

        private RectTransform _root;
        private readonly StringBuilder _sb = new StringBuilder(256);

        // Header & Currencies
        private TextMeshProUGUI _gradeBadgeTmp;
        private TextMeshProUGUI _recordBadgeTmp;
        private int _cachedHeaderHash = int.MinValue;

        private TextMeshProUGUI _coinsTmp;
        private TextMeshProUGUI _gemsTmp;
        private TextMeshProUGUI _stonesTmp;
        private TextMeshProUGUI _cellsTmp;
        private long _cachedCoinsKey = long.MinValue;
        private int _cachedGemsVal = int.MinValue;
        private int _cachedStonesVal = int.MinValue;
        private int _cachedCellsVal = int.MinValue;

        // Command Bar (Skin | Tier | Start Run)
        private Button _skinBtn;
        private Image _skinBtnBg;
        private TextMeshProUGUI _skinBtnTmp;
        private Button _tierBtn;
        private Image _tierBtnBg;
        private TextMeshProUGUI _tierBtnTmp;
        private Button _startRunBtn;
        private TextMeshProUGUI _startRunTmp;
        private int _cachedCmdHash = int.MinValue;

        // Persistent Meta Telemetry Strip (Cards | Labs | UWs)
        private TextMeshProUGUI _metaCardsHeaderTmp;
        private readonly Image[] _metaCardIcons = new Image[BalanceTables.MAX_CARD_SLOTS];
        private readonly TextMeshProUGUI[] _metaCardStars = new TextMeshProUGUI[BalanceTables.MAX_CARD_SLOTS];
        private int _cachedMetaCardsHash = int.MinValue;

        private TextMeshProUGUI _metaLabsHeaderTmp;
        private readonly TextMeshProUGUI[] _metaLabNames = new TextMeshProUGUI[BalanceTables.MAX_LAB_SLOTS];
        private readonly TextMeshProUGUI[] _metaLabTimers = new TextMeshProUGUI[BalanceTables.MAX_LAB_SLOTS];
        private readonly int[] _metaLabCachedSecs = { -1, -1, -1 };
        private int _cachedMetaLabsHash = int.MinValue;

        private TextMeshProUGUI _metaUwsHeaderTmp;
        private readonly Image[] _metaUwIcons = new Image[4]; // GT, BH, CL, GoldenBot
        private readonly TextMeshProUGUI[] _metaUwBadges = new TextMeshProUGUI[4];
        private int _cachedMetaUwsHash = int.MinValue;

        // Bottom 5-Tab Bar & Content Panels
        private int _activeTab = TabWorkshop;
        private readonly RectTransform[] _tabPanels = new RectTransform[MainTabCount];
        private readonly Image[] _mainTabBgs = new Image[MainTabCount];
        private readonly TextMeshProUGUI[] _mainTabLabels = new TextMeshProUGUI[MainTabCount];

        // Tab 0: Workshop
        private int _wsSubTab = 0; // 0=Attack, 1=Defense, 2=Utility
        private int _wsBuyMultIdx = 0;
        private readonly Image[] _wsSubTabBgs = new Image[3];
        private readonly TextMeshProUGUI[] _wsSubTabLabels = new TextMeshProUGUI[3];
        private TextMeshProUGUI _wsMultBtnTmp;
        private RectTransform _wsFtueBannerRect;
        private RectTransform _wsFtueArrowRect;
        private float _ftuePulseClock;
        private readonly WorkshopCardWidget[] _wsCards = new WorkshopCardWidget[MaxWorkshopPool];

        // Tab 1: Cards
        private readonly EquippedSlotWidget[] _equipSlots = new EquippedSlotWidget[BalanceTables.MAX_CARD_SLOTS];
        private TextMeshProUGUI _gachaStatusTmp;
        private Button _gachaPullBtn;
        private Image _gachaPullBg;
        private TextMeshProUGUI _gachaPullLbl;
        private int _cachedGachaBtnHash = int.MinValue;
        private readonly CardModuleWidget[] _cardWidgets = new CardModuleWidget[GameIdsAndScale.CARD_COUNT];

        // Tab 2: Labs
        private readonly LabSlotWidget[] _labSlotWidgets = new LabSlotWidget[BalanceTables.MAX_LAB_SLOTS];
        private readonly LabResearchWidget[] _labWidgets = new LabResearchWidget[GameIdsAndScale.LAB_COUNT];

        // Tab 3: Ultimate Weapons & Bots
        private TextMeshProUGUI _uwHeaderStatusTmp;
        private Button _uwUnlockModalBtn;
        private Image _uwUnlockModalBg;
        private TextMeshProUGUI _uwUnlockModalLbl;
        private int _cachedUwTopHash = int.MinValue;
        private readonly UwReactorWidget[] _uwWidgets = new UwReactorWidget[GameIdsAndScale.UW_COUNT];
        private Image _botCardBg;
        private TextMeshProUGUI _botStatusTmp;
        private int _cachedBotHash = int.MinValue;

        // Tab 4: Milestones & Tiers
        private Image _msTier1BtnBg;
        private Image _msTier2BtnBg;
        private TextMeshProUGUI _msTier1Tmp;
        private TextMeshProUGUI _msTier2Tmp;
        private int _cachedMsTierHash = int.MinValue;
        private readonly MilestoneCardWidget[] _msWidgets = new MilestoneCardWidget[12];

        // Offline Lab Progress Modal
        private RectTransform _offlineModalRoot;
        private RectTransform _offlineWinRect;
        private CanvasGroup _offlineModalGroup;
        private TextMeshProUGUI _offlineBodyTmp;

        // Adaptive Portrait (1080x2400) / Landscape (1920x1080) Window & Scroll Refs
        private Image _bgOverlay;
        private RectTransform _windowRect;
        private Image _windowBgImg;
        private bool _isLandscape;

        private RectTransform _topBarRect;
        private TextMeshProUGUI _topTitleTmp;
        private readonly RectTransform[] _curPillRects = new RectTransform[4];
        private Button _muteBtn;
        private RectTransform _muteBtnRect;
        private Image _muteBtnBg;
        private TextMeshProUGUI _muteLabelTmp;
        private int _cachedMuteState = -1;

        private RectTransform _commandBarRect;
        private RectTransform _skinBtnRect;
        private RectTransform _tierBtnRect;
        private RectTransform _startRunBtnRect;

        private RectTransform _metaStripRect;
        private readonly RectTransform[] _metaBoxRects = new RectTransform[3];
        private readonly RectTransform[] _metaCardSlotRects = new RectTransform[BalanceTables.MAX_CARD_SLOTS];
        private readonly RectTransform[] _metaUwSlotRects = new RectTransform[4];

        private RectTransform _viewportRect;
        private ScrollRect _scrollRect;
        private RectTransform _scrollbarRect;
        private RectTransform _bottomTabBarRect;
        private readonly RectTransform[] _mainTabIconRects = new RectTransform[MainTabCount];

        private TextMeshProUGUI _eqHdrTmp;
        private RectTransform _gachaBarRect;

        private static readonly float[] TabContentHeightsPortrait = { 1590f, 1646f, 1664f, 1664f, 1654f };
        private static readonly float[] TabContentHeightsLandscape = { 1590f, 1520f, 1664f, 1664f, 1654f };
        private static readonly string[] MainTabTitlesPortrait = { "МАСТЕРСКАЯ", "КАРТЫ", "ЛАБЫ", "УЛЬТЫ", "ВЕХИ" };
        private static readonly string[] MainTabTitlesLandscape = { "[1] МАСТЕР.", "[2] КАРТЫ", "[3] ЛАБЫ", "[4] УЛЬТЫ", "[5] ВЕХИ" };

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public int ActiveTab => _activeTab;

        public void Build(
            Transform safeParent,
            UiStyle style,
            TweenRunner tweens,
            Action onStartRun,
            Action onRequestUwModal)
        {
            _style = style;
            _tweens = tweens;
            _onStartRun = onStartRun;
            _onRequestUwModal = onRequestUwModal;

            _root = UiFactory.CreateStretchRect("HangarScreen", safeParent);
            _bgOverlay = _root.gameObject.AddComponent<Image>();
            _bgOverlay.color = new Color(0.04f, 0.06f, 0.09f, 0.78f);
            _bgOverlay.raycastTarget = true;

            _windowBgImg = UiFactory.CreateSdfImage(
                "HangarWindow", _root, _style.MatModalWindow, new Color(0.08f, 0.11f, 0.16f, 0.97f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, raycast: true);
            _windowBgImg.enabled = false;
            _windowRect = _windowBgImg.rectTransform;

            BuildTopHeader();
            BuildCommandBar();
            BuildMetaOverviewStrip();
            BuildMainTabsAndViewport();
            BuildOfflineModal();

            ApplyLayoutMode(false);
            ApplyMainTabVisuals();
            ApplyWsSubTabVisuals();
            RefreshMuteVisual(force: true);
        }

        public void SetVisible(bool visible, GameSession session = null)
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf != visible)
            {
                _root.gameObject.SetActive(visible);
            }

            if (visible)
            {
                RefreshMuteVisual(force: true);
            }

            if (visible && session != null)
            {
                _boundSession = session;
                // Guide Run 1 -> Run 2 player directly to Defense -> Carbide Teeth if FTUE arrow is active
                if (_activeTab == TabWorkshop && !session.FtueDefenseArrowCompleted && session.WorkshopUnlocked)
                {
                    _wsSubTab = 1;
                    ApplyWsSubTabVisuals();
                    InvalidateWorkshopCache();
                }
            }
        }

        public void SetActiveTab(int tabIndex)
        {
            int clamped = Mathf.Clamp(tabIndex, 0, MainTabCount - 1);
            _activeTab = clamped;
            ApplyMainTabVisuals();
            BindActiveTabToScroll();
        }

        public void ShowOfflineSummary(OfflineProgressSummary summary)
        {
            if (_offlineModalRoot == null || summary.ElapsedSeconds < 5.0) return;

            int totalSec = Mathf.Max(1, Mathf.RoundToInt((float)summary.ElapsedSeconds));
            int hrs = totalSec / 3600;
            int mins = (totalSec % 3600) / 60;
            int secs = totalSec % 60;

            _sb.Clear();
            _sb.Append("АВТОНОМНЫЙ ЦЕХ РАБОТАЛ В ОФФЛАЙНЕ:\n<color=#52E5FF>");
            if (hrs > 0) _sb.Append(hrs).Append(" ч ");
            _sb.Append(mins).Append(" мин ").Append(secs).Append(" с</color>\n\n");

            if (summary.LabsCompleted > 0)
            {
                _sb.Append("<color=#5CF59E>★ ЗАВЕРШЕНО ИССЛЕДОВАНИЙ: ")
                   .Append(summary.LabsCompleted)
                   .Append("</color>");
            }
            else
            {
                _sb.Append("Таймеры активных лабораторий продвинуты.");
            }

            _offlineBodyTmp.text = _sb.ToString();
            _offlineModalRoot.gameObject.SetActive(true);
            _tweens.FadeCanvasGroup(_offlineModalGroup, 0f, 1f, 0.18f);
        }

        private void BuildTopHeader()
        {
            Image topBg = UiFactory.CreateSdfImage(
                "HangarTopBar", _windowRect, _style.MatTopHud, new Color(0.10f, 0.14f, 0.20f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -16f), new Vector2(1040f, 198f));
            _topBarRect = topBg.rectTransform;

            _topTitleTmp = UiFactory.CreateTmp(
                "HangarTitle", topBg.transform, _style.Font, "АНГАР ГИПЕР-ШРЕДЕРА",
                30f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -14f), new Vector2(540f, 44f));
            _topTitleTmp.fontStyle = FontStyles.Bold;

            _gradeBadgeTmp = UiFactory.CreateTmp(
                "CoreGradeBadge", topBg.transform, _style.Font, "ШАССИ G0: БАЗОВЫЙ РОТОР",
                20f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -58f), new Vector2(560f, 32f));
            _gradeBadgeTmp.fontStyle = FontStyles.Bold;

            _recordBadgeTmp = UiFactory.CreateTmp(
                "RecordBadge", topBg.transform, _style.Font, "РЕКОРД Т1: W0",
                22f, UiStyle.TextAmber, TextAlignmentOptions.Right,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-22f, -14f), new Vector2(440f, 36f));
            _recordBadgeTmp.fontStyle = FontStyles.Bold;

            _muteBtn = UiFactory.CreateSdfButton(
                "HangarMuteBtn", topBg.transform, _style, _style.MatPill, new Color(0.10f, 0.14f, 0.20f, 0.96f),
                "ЗВУК ВКЛ", 17f, UiStyle.TextCyan,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-22f, -54f), new Vector2(168f, 40f),
                () =>
                {
                    UiStyle.ToggleMute();
                    RefreshMuteVisual(force: true);
                    _tweens.PunchScale(_muteBtnRect, 0.08f, 0.14f);
                }, out _muteBtnBg, out _muteLabelTmp);
            _muteBtnRect = _muteBtnBg.rectTransform;
            _muteLabelTmp.fontStyle = FontStyles.Bold;

            // 4 Currency Pills along bottom of TopBar
            _coinsTmp = BuildCurrencyPill(topBg.transform, 0, UiIcons.Coins, UiStyle.TextAmber, "0");
            _gemsTmp = BuildCurrencyPill(topBg.transform, 1, UiIcons.Gems, UiStyle.TextCyan, "0");
            _stonesTmp = BuildCurrencyPill(topBg.transform, 2, UiIcons.Stones, UiStyle.TextEmerald, "0");
            _cellsTmp = BuildCurrencyPill(topBg.transform, 3, UiIcons.Cells, new Color(1.0f, 0.62f, 0.94f, 1f), "0");
        }

        private TextMeshProUGUI BuildCurrencyPill(Transform parent, int index, string iconId, Color textColor, string initText)
        {
            float startX = -381f + index * 254f;
            Image pill = UiFactory.CreateSdfImage(
                "CurPill_" + index, parent, _style.MatPill, new Color(0.08f, 0.11f, 0.16f, 0.95f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(startX, 14f), new Vector2(242f, 64f));
            _curPillRects[index] = pill.rectTransform;

            UiFactory.CreateIcon(
                "Icon", pill.transform, _style.GetIcon(iconId),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(38f, 38f));

            TextMeshProUGUI val = UiFactory.CreateTmp(
                "Val", pill.transform, _style.Font, initText,
                24f, textColor, TextAlignmentOptions.Right,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                Vector2.zero, Vector2.zero);
            val.rectTransform.offsetMin = new Vector2(56f, 4f);
            val.rectTransform.offsetMax = new Vector2(-14f, -4f);
            val.fontStyle = FontStyles.Bold;
            return val;
        }

        private void BuildCommandBar()
        {
            RectTransform bar = UiFactory.CreateRect(
                "CommandBar", _windowRect,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -228f), new Vector2(1040f, 114f));
            _commandBarRect = bar;

            // 1. Molten Core Skin Toggle (336x114)
            _skinBtn = UiFactory.CreateSdfButton(
                "SkinBtn", bar, _style, _style.MatCardLocked, new Color(0.12f, 0.15f, 0.20f, 0.95f),
                "СКИН MOLTEN CORE\nОТКРЫТИЕ: T1 W80", 18f, UiStyle.TextSecondary,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(336f, 114f),
                OnClickSkinToggle, out _skinBtnBg, out _skinBtnTmp);
            _skinBtnRect = _skinBtnBg.rectTransform;
            _skinBtnTmp.alignment = TextAlignmentOptions.Left;
            _skinBtnTmp.rectTransform.offsetMin = new Vector2(78f, 8f);
            _skinBtnTmp.rectTransform.offsetMax = new Vector2(-10f, -8f);
            _skinBtnTmp.textWrappingMode = TextWrappingModes.Normal;
            _skinBtnTmp.fontStyle = FontStyles.Bold;

            UiFactory.CreateIcon(
                "SkinIcon", _skinBtnBg.transform, _style.GetIcon(UiIcons.SkinMoltenCore),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(52f, 52f));

            // 2. Tier 1 / Tier 2 Toggle (266x114)
            _tierBtn = UiFactory.CreateSdfButton(
                "TierToggleBtn", bar, _style, _style.MatTabActive, new Color(0.16f, 0.22f, 0.30f, 0.96f),
                "ТИР 1 • ЦЕХ\n×1.0 МОНЕТ", 20f, UiStyle.TextPrimary,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(350f, 0f), new Vector2(266f, 114f),
                OnClickTierToggle, out _tierBtnBg, out _tierBtnTmp);
            _tierBtnRect = _tierBtnBg.rectTransform;
            _tierBtnTmp.textWrappingMode = TextWrappingModes.Normal;
            _tierBtnTmp.fontStyle = FontStyles.Bold;

            // 3. Primary Launch Button: ▶ В БОЙ! (410x114)
            _startRunBtn = UiFactory.CreateSdfButton(
                "StartRunBtn", bar, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                "▶ В БОЙ! (ТИР 1)", 28f, UiStyle.TextButtonDark,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(410f, 114f),
                OnClickStartRun, out Image startBg, out _startRunTmp);
            _startRunBtnRect = startBg.rectTransform;
            _startRunTmp.fontStyle = FontStyles.Bold;
        }

        private void BuildMetaOverviewStrip()
        {
            RectTransform strip = UiFactory.CreateRect(
                "MetaOverviewStrip", _windowRect,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -356f), new Vector2(1040f, 192f));
            _metaStripRect = strip;

            // Column 1: Equipped Cards Summary (338x192)
            Button cardsBox = UiFactory.CreateSdfButton(
                "MetaCardsSummary", strip, _style, _style.MatStatCard, new Color(0.11f, 0.15f, 0.21f, 0.96f),
                "", 18f, UiStyle.TextPrimary,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(338f, 192f),
                () => SelectMainTabByUser(TabCards), out Image cardsBg, out TextMeshProUGUI empty1);
            empty1.gameObject.SetActive(false);
            _metaBoxRects[0] = cardsBg.rectTransform;

            _metaCardsHeaderTmp = UiFactory.CreateTmp(
                "Hdr", cardsBg.transform, _style.Font, "КАРТЫ (0/1 СЛ.)",
                19f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-24f, 30f));
            _metaCardsHeaderTmp.fontStyle = FontStyles.Bold;

            for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
            {
                float x = 18f + i * 78f;
                Image slotBg = UiFactory.CreateSdfImage(
                    "SlotMini_" + i, cardsBg.transform, _style.MatPill, new Color(0.07f, 0.10f, 0.14f, 0.95f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(x, 16f), new Vector2(70f, 126f));
                _metaCardSlotRects[i] = slotBg.rectTransform;

                _metaCardIcons[i] = UiFactory.CreateIcon(
                    "Icon", slotBg.transform, _style.GetIcon(UiIcons.TabCards),
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -10f), new Vector2(46f, 46f));

                _metaCardStars[i] = UiFactory.CreateTmp(
                    "Stars", slotBg.transform, _style.Font, "—",
                    15f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 8f), new Vector2(-4f, 36f));
                _metaCardStars[i].fontStyle = FontStyles.Bold;
            }

            // Column 2: Active Labs Summary (338x192)
            Button labsBox = UiFactory.CreateSdfButton(
                "MetaLabsSummary", strip, _style, _style.MatStatCard, new Color(0.11f, 0.15f, 0.21f, 0.96f),
                "", 18f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(338f, 192f),
                () => SelectMainTabByUser(TabLabs), out Image labsBg, out TextMeshProUGUI empty2);
            empty2.gameObject.SetActive(false);
            _metaBoxRects[1] = labsBg.rectTransform;

            _metaLabsHeaderTmp = UiFactory.CreateTmp(
                "Hdr", labsBg.transform, _style.Font, "ЛАБОРАТОРИЯ (0/1)",
                19f, UiStyle.TextEmerald, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-24f, 30f));
            _metaLabsHeaderTmp.fontStyle = FontStyles.Bold;

            for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
            {
                float y = -48f - i * 44f;
                _metaLabNames[i] = UiFactory.CreateTmp(
                    "LabName_" + i, labsBg.transform, _style.Font, "Слот " + (i + 1) + ": Свободен",
                    18f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(14f, y), new Vector2(214f, 36f));

                _metaLabTimers[i] = UiFactory.CreateTmp(
                    "LabTime_" + i, labsBg.transform, _style.Font, "—",
                    18f, UiStyle.TextCyan, TextAlignmentOptions.Right,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-14f, y), new Vector2(96f, 36f));
                _metaLabTimers[i].fontStyle = FontStyles.Bold;
            }

            // Column 3: Unlocked Ultimate Weapons & Golden Bot Summary (338x192)
            Button uwsBox = UiFactory.CreateSdfButton(
                "MetaUwsSummary", strip, _style, _style.MatStatCard, new Color(0.11f, 0.15f, 0.21f, 0.96f),
                "", 18f, UiStyle.TextPrimary,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(338f, 192f),
                () => SelectMainTabByUser(TabUws), out Image uwsBg, out TextMeshProUGUI empty3);
            empty3.gameObject.SetActive(false);
            _metaBoxRects[2] = uwsBg.rectTransform;

            _metaUwsHeaderTmp = UiFactory.CreateTmp(
                "Hdr", uwsBg.transform, _style.Font, "УЛЬТЫ И ДРОН (0/3)",
                19f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-24f, 30f));
            _metaUwsHeaderTmp.fontStyle = FontStyles.Bold;

            string[] metaUwIconIds = { UiIcons.UwIcons[0], UiIcons.UwIcons[1], UiIcons.UwIcons[2], UiIcons.BotGolden };
            for (int i = 0; i < 4; i++)
            {
                float x = 18f + i * 78f;
                Image cell = UiFactory.CreateSdfImage(
                    "UwMini_" + i, uwsBg.transform, _style.MatPill, new Color(0.07f, 0.10f, 0.14f, 0.95f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(x, 16f), new Vector2(70f, 126f));
                _metaUwSlotRects[i] = cell.rectTransform;

                _metaUwIcons[i] = UiFactory.CreateIcon(
                    "Icon", cell.transform, _style.GetIcon(metaUwIconIds[i]),
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -10f), new Vector2(46f, 46f));

                _metaUwBadges[i] = UiFactory.CreateTmp(
                    "Badge", cell.transform, _style.Font, "ЗАКР",
                    15f, UiStyle.TextMuted, TextAlignmentOptions.Center,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 8f), new Vector2(-4f, 36f));
                _metaUwBadges[i].fontStyle = FontStyles.Bold;
            }
        }

        private void BuildMainTabsAndViewport()
        {
            // Content Viewport: top = -564, bottom = +160 => height = 1676 px on 1080x2400 (clamped ScrollRect in 1920x1080)
            _viewportRect = UiFactory.CreateStretchRect("HangarContentViewport", _windowRect, left: 20f, bottom: 160f, right: 20f, top: 564f);
            var vpHit = _viewportRect.gameObject.AddComponent<Image>();
            vpHit.color = new Color(0f, 0f, 0f, 0f);
            vpHit.raycastTarget = true;
            _viewportRect.gameObject.AddComponent<RectMask2D>();

            _scrollRect = _viewportRect.gameObject.AddComponent<ScrollRect>();
            _scrollRect.viewport = _viewportRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 38f;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = 0.12f;

            for (int i = 0; i < MainTabCount; i++)
            {
                _tabPanels[i] = UiFactory.CreateRect(
                    "TabPanel_" + i, _viewportRect,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(0f, TabContentHeightsPortrait[i]));
            }

            // Sleek vertical Scrollbar for 1920x1080 PC WebGL window
            _scrollbarRect = UiFactory.CreateRect(
                "HangarScrollbar", _windowRect,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-28f, 0f), new Vector2(12f, 0f));
            _scrollbarRect.offsetMin = new Vector2(-40f, 112f);
            _scrollbarRect.offsetMax = new Vector2(-28f, -400f);
            var sbTrack = _scrollbarRect.gameObject.AddComponent<Image>();
            sbTrack.color = new Color(0.06f, 0.09f, 0.14f, 0.85f);
            sbTrack.raycastTarget = true;

            RectTransform slideArea = UiFactory.CreateStretchRect("SlidingArea", _scrollbarRect, 2f, 2f, 2f, 2f);
            RectTransform handleRt = UiFactory.CreateStretchRect("Handle", slideArea);
            var handleImg = handleRt.gameObject.AddComponent<Image>();
            handleImg.color = new Color(0.32f, 0.88f, 1.00f, 0.85f);
            handleImg.raycastTarget = true;

            var scrollbar = _scrollbarRect.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImg;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            _scrollRect.verticalScrollbar = scrollbar;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scrollbarRect.gameObject.SetActive(false);

            BuildWorkshopTab(_tabPanels[TabWorkshop]);
            BuildCardsTab(_tabPanels[TabCards]);
            BuildLabsTab(_tabPanels[TabLabs]);
            BuildUwsTab(_tabPanels[TabUws]);
            BuildMilestonesTab(_tabPanels[TabMilestones]);

            _scrollRect.content = _tabPanels[_activeTab];

            // Bottom 5-Tab Bar (y = +16..+146 from bottom, aligned with thumb zone & smoke coordinates)
            RectTransform tabBar = UiFactory.CreateRect(
                "BottomTabBar", _windowRect,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(1040f, 130f));
            _bottomTabBarRect = tabBar;

            string[] icons = { UiIcons.TabWorkshop, UiIcons.TabCards, UiIcons.TabLabs, UiIcons.TabUw, UiIcons.SkinMoltenCore };

            for (int i = 0; i < MainTabCount; i++)
            {
                int captured = i;
                float x = i * 210f;
                UiFactory.CreateSdfButton(
                    "MainTab_" + i, tabBar, _style, _style.MatTabInactive, new Color(0.11f, 0.15f, 0.20f, 0.96f),
                    MainTabTitlesPortrait[i], 19f, UiStyle.TextSecondary,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(x, 0f), new Vector2(200f, 128f),
                    () => SelectMainTabByUser(captured), out _mainTabBgs[i], out _mainTabLabels[i]);

                _mainTabLabels[i].alignment = TextAlignmentOptions.Bottom;
                _mainTabLabels[i].rectTransform.offsetMin = new Vector2(4f, 22f);
                _mainTabLabels[i].rectTransform.offsetMax = new Vector2(-4f, -62f);
                _mainTabLabels[i].fontStyle = FontStyles.Bold;

                Image iconImg = UiFactory.CreateIcon(
                    "TabIcon", _mainTabBgs[i].transform, _style.GetIcon(icons[i]),
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -12f), new Vector2(52f, 52f));
                _mainTabIconRects[i] = iconImg.rectTransform;
            }
        }

        private void BuildWorkshopTab(RectTransform panel)
        {
            // Sub-category row: АТАКА | ЗАЩИТА | УТИЛИТИ + Buy Multiplier
            RectTransform subBar = UiFactory.CreateRect(
                "WsSubBar", panel,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 82f));

            string[] subNames = { "АТАКА", "ЗАЩИТА", "УТИЛИТИ" };
            string[] subIcons = { UiIcons.TabAttack, UiIcons.TabDefense, UiIcons.TabUtility };
            for (int i = 0; i < 3; i++)
            {
                int subIdx = i;
                UiFactory.CreateSdfButton(
                    "WsSub_" + i, subBar, _style, _style.MatTabInactive, new Color(0.12f, 0.16f, 0.22f, 0.96f),
                    subNames[i], 22f, UiStyle.TextSecondary,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(i * 252f, 0f), new Vector2(240f, 80f),
                    () =>
                    {
                        UiStyle.PlaySfx(SfxId.UiTap);
                        _wsSubTab = subIdx;
                        ApplyWsSubTabVisuals();
                        InvalidateWorkshopCache();
                    }, out _wsSubTabBgs[i], out _wsSubTabLabels[i]);

                _wsSubTabLabels[i].alignment = TextAlignmentOptions.Left;
                _wsSubTabLabels[i].rectTransform.offsetMin = new Vector2(68f, 6f);
                _wsSubTabLabels[i].rectTransform.offsetMax = new Vector2(-8f, -6f);
                _wsSubTabLabels[i].fontStyle = FontStyles.Bold;

                UiFactory.CreateIcon(
                    "Icon", _wsSubTabBgs[i].transform, _style.GetIcon(subIcons[i]),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(46f, 46f));
            }

            UiFactory.CreateSdfButton(
                "WsMultBtn", subBar, _style, _style.MatBuyCyan, UiStyle.TextCyan,
                BuyMultLabels[0], 22f, UiStyle.TextPrimary,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(268f, 80f),
                () =>
                {
                    UiStyle.PlaySfx(SfxId.UiTap);
                    _wsBuyMultIdx = (_wsBuyMultIdx + 1) % BuyMultipliers.Length;
                    _wsMultBtnTmp.text = BuyMultLabels[_wsBuyMultIdx];
                    InvalidateWorkshopCache();
                }, out _, out _wsMultBtnTmp);
            _wsMultBtnTmp.fontStyle = FontStyles.Bold;

            // FTUE Guidance Banner
            Image ftueBanner = UiFactory.CreateSdfImage(
                "WsFtueBanner", panel, _style.MatSynergyBanner, new Color(0.28f, 0.19f, 0.05f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -94f), new Vector2(1040f, 58f));
            _wsFtueBannerRect = ftueBanner.rectTransform;

            TextMeshProUGUI ftueTxt = UiFactory.CreateTmp(
                "Txt", _wsFtueBannerRect, _style.Font,
                "▲ ИНЖЕНЕРНЫЙ ГАЙД: ОТКРОЙТЕ ВЕТКУ [КАРБИДНЫЕ ЗУБЬЯ] (30 МОНЕТ) ВО ВКЛАДКЕ [ЗАЩИТА]!",
                19f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-24f, -6f));
            ftueTxt.fontStyle = FontStyles.Bold;

            // 10 Pooled Workshop Stat Cards (2 columns x 5 rows, 510x268 each)
            for (int i = 0; i < MaxWorkshopPool; i++)
            {
                int slotIdx = i;
                int col = i % 2;
                int row = i / 2;
                float x = col * 530f;
                float y = -164f - row * 284f;

                var w = new WorkshopCardWidget();
                w.Bg = UiFactory.CreateSdfImage(
                    "WsStatCard_" + i, panel, _style.MatStatCard, new Color(0.12f, 0.16f, 0.22f, 0.97f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, y), new Vector2(510f, 268f));
                w.Root = w.Bg.rectTransform;

                w.Icon = UiFactory.CreateIcon(
                    "Icon", w.Root, _style.GetIcon(UiIcons.StatIcons[0]),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(16f, -16f), new Vector2(68f, 68f));

                w.Title = UiFactory.CreateTmp(
                    "Title", w.Root, _style.Font, "Параметр",
                    24f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                w.Title.rectTransform.offsetMin = new Vector2(96f, -54f);
                w.Title.rectTransform.offsetMax = new Vector2(-14f, -14f);
                w.Title.fontStyle = FontStyles.Bold;

                w.LevelBadge = UiFactory.CreateTmp(
                    "LevelBadge", w.Root, _style.Font, "Ур. 0",
                    19f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                w.LevelBadge.rectTransform.offsetMin = new Vector2(96f, -86f);
                w.LevelBadge.rectTransform.offsetMax = new Vector2(-14f, -54f);

                w.ValueText = UiFactory.CreateTmp(
                    "ValueTransition", w.Root, _style.Font, "0 → 1",
                    24f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                w.ValueText.rectTransform.offsetMin = new Vector2(20f, -164f);
                w.ValueText.rectTransform.offsetMax = new Vector2(-20f, -98f);
                w.ValueText.fontStyle = FontStyles.Bold;

                w.ActionBtn = UiFactory.CreateSdfButton(
                    "ActionBtn", w.Root, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                    "УЛУЧШИТЬ", 22f, UiStyle.TextButtonDark,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 16f), new Vector2(474f, 76f),
                    () => OnClickWorkshopCard(slotIdx), out w.ActionBg, out w.ActionLabel);
                w.ActionLabel.fontStyle = FontStyles.Bold;

                _wsCards[i] = w;
            }

            // Floating Pulsing FTUE Arrow inside Workshop
            Image arrowImg = UiFactory.CreateSdfImage(
                "WsFtueArrow", panel, _style.MatFtueArrowHangar, UiStyle.TextAmber,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0f),
                new Vector2(372f, -10f), new Vector2(82f, 72f), raycast: false);
            _wsFtueArrowRect = arrowImg.rectTransform;
        }

        private void BuildCardsTab(RectTransform panel)
        {
            TextMeshProUGUI eqHdr = UiFactory.CreateTmp(
                "EqHeader", panel, _style.Font, "АКТИВНЫЕ СЛОТЫ МОДУЛЬНЫХ КАРТ (МАКС. 4)",
                21f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 34f));
            eqHdr.fontStyle = FontStyles.Bold;
            _eqHdrTmp = eqHdr;

            for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
            {
                int slotIdx = i;
                var es = new EquippedSlotWidget();
                es.Btn = UiFactory.CreateSdfButton(
                    "EquipSlot_" + i, panel, _style, _style.MatTabInactive, new Color(0.11f, 0.15f, 0.21f, 0.96f),
                    "", 18f, UiStyle.TextPrimary,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(i * 264f, -38f), new Vector2(248f, 124f),
                    () => OnClickEquippedCardSlot(slotIdx), out es.Bg, out TextMeshProUGUI unused);
                unused.gameObject.SetActive(false);
                es.Root = es.Bg.rectTransform;

                es.Icon = UiFactory.CreateIcon(
                    "Icon", es.Root, _style.GetIcon(UiIcons.TabCards),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(62f, 62f));

                es.Title = UiFactory.CreateTmp(
                    "Title", es.Root, _style.Font, "Слот " + (i + 1),
                    19f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero, wrap: true);
                es.Title.rectTransform.offsetMin = new Vector2(82f, -72f);
                es.Title.rectTransform.offsetMax = new Vector2(-10f, -12f);
                es.Title.fontStyle = FontStyles.Bold;

                es.Sub = UiFactory.CreateTmp(
                    "Sub", es.Root, _style.Font, "ПУСТО",
                    17f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                    Vector2.zero, Vector2.zero);
                es.Sub.rectTransform.offsetMin = new Vector2(82f, 12f);
                es.Sub.rectTransform.offsetMax = new Vector2(-10f, 48f);
                es.Sub.fontStyle = FontStyles.Bold;

                _equipSlots[i] = es;
            }

            // Gacha Bar (y = -176, height = 76)
            Image gachaBar = UiFactory.CreateSdfImage(
                "GachaBar", panel, _style.MatHeaderStrip, new Color(0.13f, 0.18f, 0.25f, 0.97f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -176f), new Vector2(0f, 80f));
            _gachaBarRect = gachaBar.rectTransform;

            _gachaStatusTmp = UiFactory.CreateTmp(
                "GachaInfo", gachaBar.transform, _style.Font,
                "ГАЧА МОДУЛЕЙ: ОБЫЧНЫЕ 55% • РЕДКИЕ 30% • ЭПИК 15%",
                20f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero, wrap: true);
            _gachaStatusTmp.rectTransform.offsetMin = new Vector2(20f, 6f);
            _gachaStatusTmp.rectTransform.offsetMax = new Vector2(-450f, -6f);
            _gachaStatusTmp.fontStyle = FontStyles.Bold;

            _gachaPullBtn = UiFactory.CreateSdfButton(
                "PullCardBtn", gachaBar.transform, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                "◆ ТЯНУТЬ КАРТУ • 20 ГЕМОВ", 22f, UiStyle.TextButtonDark,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(424f, 64f),
                OnClickPullCard, out _gachaPullBg, out _gachaPullLbl);
            _gachaPullLbl.fontStyle = FontStyles.Bold;

            // 8 Signature Cards Grid (4 rows x 2 cols, 510x326 each)
            for (int i = 0; i < GameIdsAndScale.CARD_COUNT; i++)
            {
                int cardIdx = i;
                int col = i % 2;
                int row = i / 2;
                float x = col * 530f;
                float y = -272f - row * 342f;

                var cw = new CardModuleWidget();
                cw.Bg = UiFactory.CreateSdfImage(
                    "CardMod_" + i, panel, _style.MatStatCard, new Color(0.12f, 0.16f, 0.22f, 0.97f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, y), new Vector2(510f, 326f));
                cw.Root = cw.Bg.rectTransform;

                cw.Icon = UiFactory.CreateIcon(
                    "Icon", cw.Root, _style.GetIcon(UiIcons.CardIcons[i]),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(18f, -18f), new Vector2(78f, 78f));

                cw.RarityTag = UiFactory.CreateTmp(
                    "Rarity", cw.Root, _style.Font, "МОДУЛЬ",
                    17f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                cw.RarityTag.rectTransform.offsetMin = new Vector2(108f, -42f);
                cw.RarityTag.rectTransform.offsetMax = new Vector2(-16f, -14f);
                cw.RarityTag.fontStyle = FontStyles.Bold;

                cw.Title = UiFactory.CreateTmp(
                    "Title", cw.Root, _style.Font, BalanceTables.Cards[i].TitleRu,
                    25f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                cw.Title.rectTransform.offsetMin = new Vector2(108f, -78f);
                cw.Title.rectTransform.offsetMax = new Vector2(-16f, -42f);
                cw.Title.fontStyle = FontStyles.Bold;

                cw.StarsAndCopies = UiFactory.CreateTmp(
                    "Stars", cw.Root, _style.Font, "☆☆☆  0/5",
                    20f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                cw.StarsAndCopies.rectTransform.offsetMin = new Vector2(108f, -108f);
                cw.StarsAndCopies.rectTransform.offsetMax = new Vector2(-16f, -76f);
                cw.StarsAndCopies.fontStyle = FontStyles.Bold;

                cw.Desc = UiFactory.CreateTmp(
                    "Desc", cw.Root, _style.Font, BalanceTables.Cards[i].DescRu,
                    20f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero, wrap: true);
                cw.Desc.rectTransform.offsetMin = new Vector2(20f, -226f);
                cw.Desc.rectTransform.offsetMax = new Vector2(-20f, -118f);

                cw.EquipBtn = UiFactory.CreateSdfButton(
                    "EquipBtn", cw.Root, _style, _style.MatBuyCyan, UiStyle.TextCyan,
                    "ВСТАВИТЬ В СЛОТ", 21f, UiStyle.TextPrimary,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 16f), new Vector2(474f, 70f),
                    () => OnClickToggleEquipCard((CardId)cardIdx), out cw.EquipBg, out cw.EquipLabel);
                cw.EquipLabel.fontStyle = FontStyles.Bold;

                _cardWidgets[i] = cw;
            }
        }

        private void BuildLabsTab(RectTransform panel)
        {
            // 3 Active Lab Slots (1040 x 154 each)
            for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
            {
                int slotIdx = i;
                var lw = new LabSlotWidget();
                lw.Bg = UiFactory.CreateSdfImage(
                    "LabSlot_" + i, panel, _style.MatLabProgress[i], new Color(0.11f, 0.16f, 0.23f, 0.97f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -i * 166f), new Vector2(0f, 154f));
                lw.Root = lw.Bg.rectTransform;

                lw.Icon = UiFactory.CreateIcon(
                    "Icon", lw.Root, _style.GetIcon(UiIcons.TabLabs),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(18f, 0f), new Vector2(72f, 72f));

                lw.Title = UiFactory.CreateTmp(
                    "Title", lw.Root, _style.Font, "Слот лаборатории #" + (i + 1),
                    23f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(106f, -18f), new Vector2(480f, 40f));
                lw.Title.fontStyle = FontStyles.Bold;

                lw.TimerTmp = UiFactory.CreateTmp(
                    "Timer", lw.Root, _style.Font, "00:00",
                    30f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(106f, 20f), new Vector2(210f, 48f));
                lw.TimerTmp.fontStyle = FontStyles.Bold;

                lw.BoostTag = UiFactory.CreateTmp(
                    "BoostTag", lw.Root, _style.Font, "",
                    19f, UiStyle.TextEmerald, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(310f, 24f), new Vector2(260f, 40f));
                lw.BoostTag.fontStyle = FontStyles.Bold;

                lw.UnlockBtn = UiFactory.CreateSdfButton(
                    "UnlockSlotBtn", lw.Root, _style, _style.MatBuyCyan, UiStyle.TextCyan,
                    "ОТКРЫТЬ СЛОТ", 21f, UiStyle.TextPrimary,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-18f, 0f), new Vector2(360f, 82f),
                    () => OnClickUnlockLabSlot(), out lw.UnlockBg, out lw.UnlockLbl);
                lw.UnlockLbl.fontStyle = FontStyles.Bold;

                lw.Boost2Btn = UiFactory.CreateSdfButton(
                    "Boost2xBtn", lw.Root, _style, _style.MatBuyCyan, UiStyle.TextCyan,
                    "×2 (" + BalanceTables.CELLS_COST_BOOST_2X + " кл.)", 18f, UiStyle.TextPrimary,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-240f, -14f), new Vector2(188f, 58f),
                    () => OnClickBoostLab(slotIdx, false), out lw.Boost2Bg, out TextMeshProUGUI b2Lbl);
                b2Lbl.fontStyle = FontStyles.Bold;

                lw.Boost3Btn = UiFactory.CreateSdfButton(
                    "Boost3xBtn", lw.Root, _style, _style.MatCardEpic, UiStyle.TextAmber,
                    "×3 (" + BalanceTables.CELLS_COST_BOOST_3X + " кл.)", 18f, UiStyle.TextAmber,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-240f, 14f), new Vector2(188f, 58f),
                    () => OnClickBoostLab(slotIdx, true), out lw.Boost3Bg, out TextMeshProUGUI b3Lbl);
                b3Lbl.fontStyle = FontStyles.Bold;

                lw.RushBtn = UiFactory.CreateSdfButton(
                    "RushGemsBtn", lw.Root, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                    "УСКОРИТЬ\n1 ГЕМ", 19f, UiStyle.TextButtonDark,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-18f, 0f), new Vector2(208f, 122f),
                    () => OnClickRushLab(slotIdx), out lw.RushBg, out lw.RushLbl);
                lw.RushLbl.textWrappingMode = TextWrappingModes.Normal;
                lw.RushLbl.fontStyle = FontStyles.Bold;

                _labSlotWidgets[i] = lw;
            }

            // 6 Research Projects Grid (3 rows x 2 cols, 510 x 366 each)
            for (int i = 0; i < GameIdsAndScale.LAB_COUNT; i++)
            {
                int labIdx = i;
                int col = i % 2;
                int row = i / 2;
                float x = col * 530f;
                float y = -512f - row * 382f;

                var rw = new LabResearchWidget();
                rw.Bg = UiFactory.CreateSdfImage(
                    "LabProject_" + i, panel, _style.MatStatCard, new Color(0.12f, 0.16f, 0.22f, 0.97f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, y), new Vector2(510f, 366f));
                rw.Root = rw.Bg.rectTransform;

                rw.Icon = UiFactory.CreateIcon(
                    "Icon", rw.Root, _style.GetIcon(UiIcons.LabIcons[i]),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(18f, -18f), new Vector2(74f, 74f));

                rw.Title = UiFactory.CreateTmp(
                    "Title", rw.Root, _style.Font, BalanceTables.Labs[i].TitleRu,
                    24f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                rw.Title.rectTransform.offsetMin = new Vector2(104f, -54f);
                rw.Title.rectTransform.offsetMax = new Vector2(-14f, -16f);
                rw.Title.fontStyle = FontStyles.Bold;

                rw.LevelBadge = UiFactory.CreateTmp(
                    "Level", rw.Root, _style.Font, "Ур. 0 / " + BalanceTables.Labs[i].MaxLevel,
                    19f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                rw.LevelBadge.rectTransform.offsetMin = new Vector2(104f, -88f);
                rw.LevelBadge.rectTransform.offsetMax = new Vector2(-14f, -54f);
                rw.LevelBadge.fontStyle = FontStyles.Bold;

                rw.Desc = UiFactory.CreateTmp(
                    "Desc", rw.Root, _style.Font, BalanceTables.Labs[i].DescRu,
                    20f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero, wrap: true);
                rw.Desc.rectTransform.offsetMin = new Vector2(20f, -212f);
                rw.Desc.rectTransform.offsetMax = new Vector2(-20f, -104f);

                rw.DurationTmp = UiFactory.CreateTmp(
                    "Dur", rw.Root, _style.Font, "Длительность: 00:45",
                    19f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                    Vector2.zero, Vector2.zero);
                rw.DurationTmp.rectTransform.offsetMin = new Vector2(20f, 102f);
                rw.DurationTmp.rectTransform.offsetMax = new Vector2(-20f, 138f);
                rw.DurationTmp.fontStyle = FontStyles.Bold;

                rw.StartBtn = UiFactory.CreateSdfButton(
                    "StartLabBtn", rw.Root, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                    "ИССЛЕДОВАТЬ", 21f, UiStyle.TextButtonDark,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 16f), new Vector2(474f, 74f),
                    () => OnClickStartLab((LabId)labIdx), out rw.StartBg, out rw.StartLbl);
                rw.StartLbl.fontStyle = FontStyles.Bold;

                _labWidgets[i] = rw;
            }
        }

        private void BuildUwsTab(RectTransform panel)
        {
            // Top Unlock Banner (1040 x 136)
            Image topBox = UiFactory.CreateSdfImage(
                "UwTopBanner", panel, _style.MatHeaderStrip, new Color(0.14f, 0.19f, 0.27f, 0.97f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 136f));

            _uwHeaderStatusTmp = UiFactory.CreateTmp(
                "UwTopStatus", topBox.transform, _style.Font,
                "РЕАКТОР АБСОЛЮТНОГО ОРУЖИЯ\nСИНХРОН GT + BH: КД 75с",
                22f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero, wrap: true);
            _uwHeaderStatusTmp.rectTransform.offsetMin = new Vector2(24f, 12f);
            _uwHeaderStatusTmp.rectTransform.offsetMax = new Vector2(-440f, -12f);
            _uwHeaderStatusTmp.fontStyle = FontStyles.Bold;

            _uwUnlockModalBtn = UiFactory.CreateSdfButton(
                "OpenUwChoiceBtn", topBox.transform, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                "⚡ ОТКРЫТЬ УЛЬТУ • 5 КАМНЕЙ", 23f, UiStyle.TextButtonDark,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(404f, 96f),
                () =>
                {
                    if (_boundSession == null) return;
                    UiStyle.PlaySfx(SfxId.UiTap);
                    _onRequestUwModal?.Invoke();
                }, out _uwUnlockModalBg, out _uwUnlockModalLbl);
            _uwUnlockModalLbl.fontStyle = FontStyles.Bold;

            // 3 Ultimate Weapon Reactor Cards (1040 x 350 each)
            for (int i = 0; i < GameIdsAndScale.UW_COUNT; i++)
            {
                int uwIdx = i;
                float y = -154f - i * 366f;

                var uw = new UwReactorWidget();
                uw.Bg = UiFactory.CreateSdfImage(
                    "UwReactor_" + i, panel, _style.MatCardEpic, new Color(0.16f, 0.14f, 0.10f, 0.97f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(0f, 350f));
                uw.Root = uw.Bg.rectTransform;

                uw.Icon = UiFactory.CreateIcon(
                    "Icon", uw.Root, _style.GetIcon(UiIcons.UwIcons[i]),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(24f, -24f), new Vector2(112f, 112f));

                uw.Title = UiFactory.CreateTmp(
                    "Title", uw.Root, _style.Font, BalanceTables.Uws[i].TitleRu,
                    29f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                uw.Title.rectTransform.offsetMin = new Vector2(156f, -66f);
                uw.Title.rectTransform.offsetMax = new Vector2(-260f, -20f);
                uw.Title.fontStyle = FontStyles.Bold;

                uw.StatusBadge = UiFactory.CreateTmp(
                    "Badge", uw.Root, _style.Font, "ЗАКРЫТО",
                    22f, UiStyle.TextCyan, TextAlignmentOptions.Right,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-24f, -24f), new Vector2(240f, 42f));
                uw.StatusBadge.fontStyle = FontStyles.Bold;

                uw.Desc = UiFactory.CreateTmp(
                    "Desc", uw.Root, _style.Font, BalanceTables.Uws[i].DescRu,
                    21f, UiStyle.TextSecondary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero, wrap: true);
                uw.Desc.rectTransform.offsetMin = new Vector2(156f, -138f);
                uw.Desc.rectTransform.offsetMax = new Vector2(-24f, -72f);

                uw.LiveStats = UiFactory.CreateTmp(
                    "LiveStats", uw.Root, _style.Font, "",
                    22f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                    Vector2.zero, Vector2.zero, wrap: true);
                uw.LiveStats.rectTransform.offsetMin = new Vector2(24f, 102f);
                uw.LiveStats.rectTransform.offsetMax = new Vector2(-24f, 198f);
                uw.LiveStats.fontStyle = FontStyles.Bold;

                uw.UpgradeBtn = UiFactory.CreateSdfButton(
                    "UpgradeUwBtn", uw.Root, _style, _style.MatBuyCyan, UiStyle.TextCyan,
                    "ПРОКАЧАТЬ", 23f, UiStyle.TextPrimary,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-24f, 18f), new Vector2(420f, 74f),
                    () => OnClickUpgradeUw((UwId)uwIdx), out uw.UpgradeBg, out uw.UpgradeLbl);
                uw.UpgradeLbl.fontStyle = FontStyles.Bold;

                _uwWidgets[i] = uw;
            }

            // Autonomous Bots Card (Golden Bot + Gem Drone, y = -1256, height = 390)
            _botCardBg = UiFactory.CreateSdfImage(
                "AutonomousBotsCard", panel, _style.MatCardRare, new Color(0.11f, 0.18f, 0.25f, 0.97f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -1256f), new Vector2(0f, 390f));

            UiFactory.CreateIcon(
                "GoldenBotIcon", _botCardBg.transform, _style.GetIcon(UiIcons.BotGolden),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -26f), new Vector2(104f, 104f));

            UiFactory.CreateIcon(
                "GemDroneIcon", _botCardBg.transform, _style.GetIcon(UiIcons.BotGemDrone),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 36f), new Vector2(96f, 96f));

            _botStatusTmp = UiFactory.CreateTmp(
                "BotStatus", _botCardBg.transform, _style.Font, "",
                22f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero, wrap: true);
            _botStatusTmp.rectTransform.offsetMin = new Vector2(148f, 20f);
            _botStatusTmp.rectTransform.offsetMax = new Vector2(-24f, -20f);
        }

        private void BuildMilestonesTab(RectTransform panel)
        {
            // Tier 1 / Tier 2 selector cards at top (y = 0, height = 108)
            UiFactory.CreateSdfButton(
                "SelectTier1Btn", panel, _style, _style.MatTabActive, new Color(0.15f, 0.21f, 0.29f, 0.97f),
                "ТИР 1: СТАНДАРТНЫЙ ЦЕХ\n×1.0 HP • ×1.0 МОНЕТ", 21f, UiStyle.TextPrimary,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(510f, 108f),
                () => SelectTierFromMilestones(1), out _msTier1BtnBg, out _msTier1Tmp);
            _msTier1Tmp.textWrappingMode = TextWrappingModes.Normal;
            _msTier1Tmp.fontStyle = FontStyles.Bold;

            UiFactory.CreateSdfButton(
                "SelectTier2Btn", panel, _style, _style.MatTabInactive, new Color(0.11f, 0.14f, 0.19f, 0.96f),
                "ТИР 2: ТИТАНОВЫЙ ФОРСАЖ\n×20 HP • ×1.8 МОНЕТ (W100)", 21f, UiStyle.TextSecondary,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(510f, 108f),
                () => SelectTierFromMilestones(2), out _msTier2BtnBg, out _msTier2Tmp);
            _msTier2Tmp.textWrappingMode = TextWrappingModes.Normal;
            _msTier2Tmp.fontStyle = FontStyles.Bold;

            // 12 Tier 1 Milestones Grid (6 rows x 2 cols, 510 x 240 each)
            for (int i = 0; i < BalanceTables.Tier1Milestones.Length; i++)
            {
                ref readonly MilestoneDef def = ref BalanceTables.Tier1Milestones[i];
                int col = i % 2;
                int row = i / 2;
                float x = col * 530f;
                float y = -124f - row * 254f;

                var mw = new MilestoneCardWidget();
                mw.Bg = UiFactory.CreateSdfImage(
                    "Milestone_" + i, panel, _style.MatStatCard, new Color(0.12f, 0.16f, 0.22f, 0.97f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, y), new Vector2(510f, 240f));
                mw.Root = mw.Bg.rectTransform;

                mw.WaveBadge = UiFactory.CreateTmp(
                    "Wave", mw.Root, _style.Font, "ВОЛНА " + def.Wave,
                    25f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(20f, -16f), new Vector2(260f, 40f));
                mw.WaveBadge.fontStyle = FontStyles.Bold;

                mw.StatusTag = UiFactory.CreateTmp(
                    "Status", mw.Root, _style.Font, "В ПРОЦЕССЕ",
                    19f, UiStyle.TextSecondary, TextAlignmentOptions.Right,
                    new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-20f, -18f), new Vector2(220f, 36f));
                mw.StatusTag.fontStyle = FontStyles.Bold;

                string safeBanner = def.BannerRu.Replace('\u00AB', '"').Replace('\u00BB', '"');
                mw.BannerTxt = UiFactory.CreateTmp(
                    "Banner", mw.Root, _style.Font, safeBanner,
                    21f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero, wrap: true);
                mw.BannerTxt.rectTransform.offsetMin = new Vector2(20f, -148f);
                mw.BannerTxt.rectTransform.offsetMax = new Vector2(-20f, -64f);
                mw.BannerTxt.fontStyle = FontStyles.Bold;

                _sb.Clear();
                _sb.Append("Награда: ");
                bool first = true;
                if (def.CoinsReward > 0) { _sb.Append('+').Append(def.CoinsReward).Append(" монет"); first = false; }
                if (def.GemsReward > 0) { if (!first) _sb.Append(" • "); _sb.Append('+').Append(def.GemsReward).Append(" гемов"); first = false; }
                if (def.StonesReward > 0) { if (!first) _sb.Append(" • "); _sb.Append('+').Append(def.StonesReward).Append(" камней"); }

                mw.RewardTxt = UiFactory.CreateTmp(
                    "Rewards", mw.Root, _style.Font, _sb.ToString(),
                    19f, UiStyle.TextCyan, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                    Vector2.zero, Vector2.zero, wrap: true);
                mw.RewardTxt.rectTransform.offsetMin = new Vector2(20f, 16f);
                mw.RewardTxt.rectTransform.offsetMax = new Vector2(-20f, 76f);

                _msWidgets[i] = mw;
            }
        }

        private void BuildOfflineModal()
        {
            _offlineModalRoot = UiFactory.CreateStretchRect("OfflineProgressModal", _root);
            var dim = _offlineModalRoot.gameObject.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.04f, 0.07f, 0.85f);
            dim.raycastTarget = true;
            _offlineModalGroup = _offlineModalRoot.gameObject.AddComponent<CanvasGroup>();

            Image win = UiFactory.CreateSdfImage(
                "OfflineWindow", _offlineModalRoot, _style.MatModalWindow, new Color(0.11f, 0.16f, 0.23f, 0.99f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(860f, 560f), raycast: true);
            _offlineWinRect = win.rectTransform;

            TextMeshProUGUI hdr = UiFactory.CreateTmp(
                "Title", win.transform, _style.Font, "ОТЧЁТ ЛАБОРАТОРИИ",
                34f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -32f), new Vector2(-40f, 56f));
            hdr.fontStyle = FontStyles.Bold;

            _offlineBodyTmp = UiFactory.CreateTmp(
                "Body", win.transform, _style.Font, "",
                25f, UiStyle.TextPrimary, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-64f, -190f), wrap: true);

            UiFactory.CreateSdfButton(
                "AcceptOfflineBtn", win.transform, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                "✓ ПРИНЯТЬ", 26f, UiStyle.TextButtonDark,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(360f, 96f),
                () =>
                {
                    UiStyle.PlaySfx(SfxId.UiTap);
                    _offlineModalRoot.gameObject.SetActive(false);
                }, out _, out TextMeshProUGUI okLbl);
            okLbl.fontStyle = FontStyles.Bold;

            _offlineModalRoot.gameObject.SetActive(false);
        }

        // --- Live Per-Frame Tick (Zero Allocations on Steady State) ---

        public void Tick(GameSession session, float unscaledDt)
        {
            if (session == null || _root == null || !_root.gameObject.activeSelf) return;
            _boundSession = session;

            RefreshMuteVisual(force: false);
            TickHeaderAndCurrencies(session);
            TickCommandBar(session);
            TickMetaOverviewStrip(session);

            switch (_activeTab)
            {
                case TabWorkshop:
                    TickWorkshopTab(session, unscaledDt);
                    break;
                case TabCards:
                    TickCardsTab(session);
                    break;
                case TabLabs:
                    TickLabsTab(session);
                    break;
                case TabUws:
                    TickUwsTab(session);
                    break;
                case TabMilestones:
                    TickMilestonesTab(session);
                    break;
            }
        }

        private void TickHeaderAndCurrencies(GameSession session)
        {
            UiFormat.SetCompactDouble(_coinsTmp, "", session.Coins, ref _cachedCoinsKey);
            UiFormat.SetInt(_gemsTmp, "", session.Gems, ref _cachedGemsVal);
            UiFormat.SetInt(_stonesTmp, "", session.Stones, ref _cachedStonesVal);
            UiFormat.SetInt(_cellsTmp, "", session.EliteCells, ref _cachedCellsVal);

            CoreVisualGrade grade = session.GetVisualCoreGrade();
            int hdrHash = ((int)grade * 1000003) ^ (session.BestWaveT1 * 997) ^ session.BestWaveT2 ^ (session.Tier2Unlocked ? 0x40000 : 0);
            if (hdrHash != _cachedHeaderHash)
            {
                _cachedHeaderHash = hdrHash;
                switch (grade)
                {
                    case CoreVisualGrade.G3_DiamondReactor:
                        _gradeBadgeTmp.text = "ШАССИ G3: АЛМАЗНЫЙ РЕАКТОР";
                        _gradeBadgeTmp.color = UiStyle.TextAmber;
                        break;
                    case CoreVisualGrade.G2_TitaniumTwin:
                        _gradeBadgeTmp.text = "ШАССИ G2: ТИТАНОВЫЙ ДВУХВАЛЬНИК";
                        _gradeBadgeTmp.color = UiStyle.TextCyan;
                        break;
                    case CoreVisualGrade.G1_SteelCarbide:
                        _gradeBadgeTmp.text = "ШАССИ G1: СТАЛЬ И КАРБИД";
                        _gradeBadgeTmp.color = UiStyle.TextEmerald;
                        break;
                    default:
                        _gradeBadgeTmp.text = "ШАССИ G0: БАЗОВЫЙ РОТОР";
                        _gradeBadgeTmp.color = UiStyle.TextSecondary;
                        break;
                }

                _sb.Clear();
                _sb.Append("РЕКОРД Т1: W").Append(session.BestWaveT1);
                if (session.Tier2Unlocked || session.BestWaveT2 > 0)
                {
                    _sb.Append(" • Т2: W").Append(session.BestWaveT2);
                }
                _recordBadgeTmp.text = _sb.ToString();
            }
        }

        private void TickCommandBar(GameSession session)
        {
            int cmdHash = (session.MoltenCoreSkinUnlocked ? 1 : 0)
                | (session.MoltenCoreSkinEquipped ? 2 : 0)
                | (session.Tier2Unlocked ? 4 : 0)
                | (_isLandscape ? 8 : 0)
                | (session.ActiveTier << 4);

            if (cmdHash == _cachedCmdHash) return;
            _cachedCmdHash = cmdHash;

            if (session.MoltenCoreSkinUnlocked)
            {
                _skinBtnBg.material = session.MoltenCoreSkinEquipped ? _style.MatCardEpic : _style.MatTabInactive;
                _skinBtnTmp.color = session.MoltenCoreSkinEquipped ? UiStyle.TextAmber : UiStyle.TextSecondary;
                _skinBtnTmp.text = session.MoltenCoreSkinEquipped
                    ? "MOLTEN CORE [ВКЛ]\n+2% ВСЕХ МОНЕТ"
                    : "MOLTEN CORE [ВЫКЛ]\nНАЖМИТЕ ДЛЯ +2%";
            }
            else
            {
                _skinBtnBg.material = _style.MatCardLocked;
                _skinBtnTmp.color = UiStyle.TextMuted;
                _skinBtnTmp.text = "MOLTEN CORE\nОТКРЫТИЕ: T1 W80";
            }

            if (session.ActiveTier == 2 && session.Tier2Unlocked)
            {
                _tierBtnBg.material = _style.MatCardEpic;
                _tierBtnTmp.color = UiStyle.TextAmber;
                _tierBtnTmp.text = "★ ТИР 2 АКТИВЕН\n×1.8 МОНЕТ (×20 HP)";
                _startRunTmp.text = _isLandscape ? "▶ В БОЙ! ТИР 2 [Space]" : "▶ В БОЙ! (ТИР 2)";
            }
            else
            {
                _tierBtnBg.material = _style.MatTabActive;
                _tierBtnTmp.color = UiStyle.TextPrimary;
                _tierBtnTmp.text = session.Tier2Unlocked
                    ? "ТИР 1 • НАЖМИТЕ\nДЛЯ ТИР 2 (×1.8)"
                    : "ТИР 1 • ЦЕХ\nТИР 2 НА W100";
                _startRunTmp.text = _isLandscape ? "▶ В БОЙ! ТИР 1 [Space]" : "▶ В БОЙ! (ТИР 1)";
            }
        }

        private void TickMetaOverviewStrip(GameSession session)
        {
            // 1. Cards mini-summary
            int cardsHash = session.UnlockedCardSlots * 31 + (session.CardsUnlocked ? 17 : 0);
            for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
            {
                int eq = session.EquippedCards[i];
                int star = eq >= 0 ? session.GetCardStar((CardId)eq) : 0;
                cardsHash = cardsHash * 397 ^ ((eq + 2) * 13 + star);
            }

            if (cardsHash != _cachedMetaCardsHash)
            {
                _cachedMetaCardsHash = cardsHash;
                int eqCount = 0;
                for (int i = 0; i < session.UnlockedCardSlots; i++)
                {
                    if (session.EquippedCards[i] >= 0) eqCount++;
                }

                _sb.Clear();
                if (!session.CardsUnlocked)
                {
                    _sb.Append("КАРТЫ (ОТКРЫТИЕ W20)");
                }
                else
                {
                    _sb.Append("КАРТЫ (").Append(eqCount).Append('/').Append(session.UnlockedCardSlots).Append(" В СЛОТАХ)");
                }
                _metaCardsHeaderTmp.text = _sb.ToString();

                for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
                {
                    if (i >= session.UnlockedCardSlots)
                    {
                        _metaCardIcons[i].sprite = _style.GetIcon(UiIcons.TabCards);
                        _metaCardIcons[i].color = new Color(0.35f, 0.40f, 0.48f, 0.45f);
                        _metaCardStars[i].color = UiStyle.TextMuted;
                        _metaCardStars[i].text = "ЗАКР";
                    }
                    else if (session.EquippedCards[i] >= 0)
                    {
                        int cid = session.EquippedCards[i];
                        int star = session.GetCardStar((CardId)cid);
                        _metaCardIcons[i].sprite = _style.GetIcon(UiIcons.CardIcons[cid]);
                        _metaCardIcons[i].color = Color.white;
                        _metaCardStars[i].color = UiStyle.TextAmber;
                        _metaCardStars[i].text = star >= 3 ? "★★★" : (star == 2 ? "★★☆" : "★☆☆");
                    }
                    else
                    {
                        _metaCardIcons[i].sprite = _style.GetIcon(UiIcons.TabCards);
                        _metaCardIcons[i].color = new Color(0.55f, 0.65f, 0.78f, 0.65f);
                        _metaCardStars[i].color = UiStyle.TextSecondary;
                        _metaCardStars[i].text = "ПУСТО";
                    }
                }
            }

            // 2. Labs mini-summary (names hashed, countdown timers updated zero-alloc via SetTimerMmSs)
            int labsHash = (session.LabsUnlocked ? 1000 : 0) + session.UnlockedLabSlots * 37;
            int activeLabsCount = 0;
            for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
            {
                ref readonly LabSlotState ls = ref session.LabSlots[i];
                if (i < session.UnlockedLabSlots && ls.Active) activeLabsCount++;
                int bKey = ls.BoostRemainingSeconds > 0f ? Mathf.RoundToInt(ls.BoostMult) : 1;
                labsHash = labsHash * 397 ^ ((ls.Active ? 1 : 0) * 97 + (int)ls.Lab * 7 + bKey);
            }

            if (labsHash != _cachedMetaLabsHash)
            {
                _cachedMetaLabsHash = labsHash;
                _sb.Clear();
                if (!session.LabsUnlocked)
                {
                    _sb.Append("ЛАБЫ (ОТКРЫТИЕ W15)");
                }
                else
                {
                    _sb.Append("ЛАБЫ (").Append(activeLabsCount).Append('/').Append(session.UnlockedLabSlots).Append(" АКТИВНЫ)");
                }
                _metaLabsHeaderTmp.text = _sb.ToString();

                for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
                {
                    _metaLabCachedSecs[i] = -1;
                    if (i >= session.UnlockedLabSlots)
                    {
                        _metaLabNames[i].color = UiStyle.TextMuted;
                        _metaLabNames[i].text = "Слот " + (i + 1) + ": Закрыт";
                        _metaLabTimers[i].color = UiStyle.TextMuted;
                        _metaLabTimers[i].text = "—";
                    }
                    else if (session.LabSlots[i].Active)
                    {
                        LabId lid = session.LabSlots[i].Lab;
                        _metaLabNames[i].color = UiStyle.TextPrimary;
                        if (session.LabSlots[i].BoostRemainingSeconds > 0f && session.LabSlots[i].BoostMult > 1.1f)
                        {
                            _sb.Clear();
                            _sb.Append("⚡×").Append(Mathf.RoundToInt(session.LabSlots[i].BoostMult)).Append(' ')
                               .Append(BalanceTables.Labs[(int)lid].TitleRu);
                            _metaLabNames[i].text = _sb.ToString();
                        }
                        else
                        {
                            _metaLabNames[i].text = BalanceTables.Labs[(int)lid].TitleRu;
                        }
                        _metaLabTimers[i].color = UiStyle.TextCyan;
                    }
                    else
                    {
                        _metaLabNames[i].color = UiStyle.TextSecondary;
                        _metaLabNames[i].text = "Слот " + (i + 1) + ": Свободен";
                        _metaLabTimers[i].color = UiStyle.TextSecondary;
                        _metaLabTimers[i].text = "ГОТОВ";
                    }
                }
            }

            for (int i = 0; i < session.UnlockedLabSlots && i < BalanceTables.MAX_LAB_SLOTS; i++)
            {
                if (session.LabSlots[i].Active)
                {
                    UiFormat.SetTimerMmSs(_metaLabTimers[i], session.LabSlots[i].RemainingSeconds, ref _metaLabCachedSecs[i]);
                }
            }

            // 3. UWs & Golden Bot mini-summary
            int uwsHash = (session.UwSystemUnlocked ? 1 : 0)
                | (session.GoldenBotUnlocked ? 2 : 0)
                | (session.UwUnlockedCount << 3);
            for (int i = 0; i < GameIdsAndScale.UW_COUNT; i++)
            {
                uwsHash = uwsHash * 31 + (session.UwUnlocked[i] ? (10 + session.UwLevels[i]) : 0);
            }

            if (uwsHash != _cachedMetaUwsHash)
            {
                _cachedMetaUwsHash = uwsHash;
                _sb.Clear();
                _sb.Append("УЛЬТЫ (").Append(session.UwUnlockedCount).Append("/3)")
                   .Append(session.GoldenBotUnlocked ? " • ДРОН ★" : " • ДРОН W50");
                _metaUwsHeaderTmp.text = _sb.ToString();

                for (int i = 0; i < GameIdsAndScale.UW_COUNT; i++)
                {
                    if (session.UwUnlocked[i])
                    {
                        _metaUwIcons[i].color = Color.white;
                        _metaUwBadges[i].color = UiStyle.TextAmber;
                        _metaUwBadges[i].text = "УР." + (session.UwLevels[i] + 1);
                    }
                    else
                    {
                        _metaUwIcons[i].color = new Color(0.38f, 0.42f, 0.50f, 0.45f);
                        _metaUwBadges[i].color = UiStyle.TextMuted;
                        _metaUwBadges[i].text = "ЗАКР";
                    }
                }

                if (session.GoldenBotUnlocked)
                {
                    _metaUwIcons[3].color = Color.white;
                    _metaUwBadges[3].color = UiStyle.TextEmerald;
                    _metaUwBadges[3].text = "АКТИВ";
                }
                else
                {
                    _metaUwIcons[3].color = new Color(0.38f, 0.42f, 0.50f, 0.45f);
                    _metaUwBadges[3].color = UiStyle.TextMuted;
                    _metaUwBadges[3].text = "W50";
                }
            }
        }

        private void TickWorkshopTab(GameSession session, float unscaledDt)
        {
            bool showFtue = !session.FtueDefenseArrowCompleted;
            if (_wsFtueBannerRect.gameObject.activeSelf != showFtue)
            {
                _wsFtueBannerRect.gameObject.SetActive(showFtue);
            }
            if (_wsFtueArrowRect.gameObject.activeSelf != showFtue)
            {
                _wsFtueArrowRect.gameObject.SetActive(showFtue);
            }

            if (showFtue)
            {
                _ftuePulseClock += unscaledDt * 5.2f;
                float bob = Mathf.Sin(_ftuePulseClock) * 9f;
                if (_wsSubTab != 1)
                {
                    // Point arrow at "ЗАЩИТА" sub-tab button
                    _wsFtueArrowRect.anchoredPosition = new Vector2(372f, -8f + bob);
                }
                else
                {
                    // Point arrow directly at "Карбидные зубья" (index 4 => row 2, col 0 => x = 255, y = -732)
                    _wsFtueArrowRect.anchoredPosition = new Vector2(380f, -678f + bob);
                }
            }

            StatId[] list = TabStats[_wsSubTab];
            long coinsQuant = (long)Math.Floor(session.Coins);

            for (int i = 0; i < MaxWorkshopPool; i++)
            {
                WorkshopCardWidget w = _wsCards[i];
                if (i >= list.Length)
                {
                    if (w.Root.gameObject.activeSelf) w.Root.gameObject.SetActive(false);
                    continue;
                }

                if (!w.Root.gameObject.activeSelf) w.Root.gameObject.SetActive(true);

                StatId id = list[i];
                w.BoundStat = id;
                ref readonly StatDef def = ref BalanceTables.Stats[(int)id];
                bool unlocked = session.IsStatUnlocked(id);
                int lv = session.WorkshopLevels[(int)id];
                int buyCount = ComputeWorkshopBuyCount(session, id, lv, def.MaxLevel, unlocked, out double totalCost);
                bool canAfford = unlocked
                    ? (lv < def.MaxLevel && buyCount > 0 && session.Coins >= totalCost)
                    : (session.Coins >= def.UnlockCoins);

                long hash = ((long)id << 48)
                    ^ ((long)lv << 32)
                    ^ ((long)_wsBuyMultIdx << 28)
                    ^ (unlocked ? (1L << 26) : 0L)
                    ^ (canAfford ? (1L << 25) : 0L)
                    ^ ((long)buyCount << 12)
                    ^ (coinsQuant & 0xFFFL);

                if (hash == w.CachedHash) continue;
                w.CachedHash = hash;

                w.Icon.sprite = _style.GetIcon(UiIcons.StatIcons[(int)id]);
                w.Title.text = def.TitleRu;

                if (!unlocked)
                {
                    w.Bg.material = _style.MatCardLocked;
                    w.Icon.color = new Color(0.65f, 0.72f, 0.82f, 0.75f);
                    w.LevelBadge.text = "МОДУЛЬ ЗАБЛОКИРОВАН";
                    w.LevelBadge.color = UiStyle.TextAmber;
                    w.ValueText.text = "Откройте инженерную ветку";
                    w.ValueText.color = UiStyle.TextSecondary;

                    w.ActionBtn.interactable = canAfford;
                    w.ActionBg.material = canAfford ? _style.MatBuyCyan : _style.MatBuyDisabled;
                    w.ActionLabel.color = canAfford ? UiStyle.TextPrimary : UiStyle.TextMuted;

                    _sb.Clear();
                    _sb.Append("ОТКРЫТЬ ВЕТКУ • ").Append(def.UnlockCoins).Append(" МОНЕТ");
                    w.ActionLabel.text = _sb.ToString();
                }
                else if (lv >= def.MaxLevel)
                {
                    w.Bg.material = _style.MatStatCard;
                    w.Icon.color = Color.white;
                    w.LevelBadge.text = "МАКС. УРОВЕНЬ (" + def.MaxLevel + ")";
                    w.LevelBadge.color = UiStyle.TextEmerald;
                    w.ValueText.text = UiFormat.BuildStatTransitionText(id, lv, 0, def.MaxLevel);
                    w.ValueText.color = UiStyle.TextPrimary;

                    w.ActionBtn.interactable = false;
                    w.ActionBg.material = _style.MatBuyGreen;
                    w.ActionLabel.color = UiStyle.TextPrimary;
                    w.ActionLabel.text = "✓ МАКСИМУМ";
                }
                else
                {
                    w.Bg.material = _style.MatStatCard;
                    w.Icon.color = Color.white;

                    _sb.Clear();
                    _sb.Append("Ур. ").Append(lv).Append(" / ").Append(def.MaxLevel);
                    w.LevelBadge.text = _sb.ToString();
                    w.LevelBadge.color = UiStyle.TextSecondary;

                    int previewAdd = Math.Max(1, buyCount);
                    w.ValueText.text = UiFormat.BuildStatTransitionText(id, lv, previewAdd, def.MaxLevel);
                    w.ValueText.color = UiStyle.TextPrimary;

                    w.ActionBtn.interactable = canAfford;
                    w.ActionBg.material = canAfford ? _style.MatBuyAmber : _style.MatBuyDisabled;
                    w.ActionLabel.color = canAfford ? UiStyle.TextButtonDark : UiStyle.TextMuted;

                    double displayCost = buyCount > 0 ? totalCost : BalanceTables.GetCoinCost(id, lv);
                    _sb.Clear();
                    _sb.Append('+').Append(previewAdd).Append(" УР. • ");
                    UiFormat.AppendCompactToSb(_sb, displayCost);
                    _sb.Append(" МОНЕТ");
                    w.ActionLabel.text = _sb.ToString();
                }
            }
        }

        private int ComputeWorkshopBuyCount(GameSession session, StatId id, int curLv, int maxLv, bool unlocked, out double totalCost)
        {
            totalCost = 0.0;
            if (!unlocked || curLv >= maxLv) return 0;

            int mult = BuyMultipliers[_wsBuyMultIdx];
            int targetMax = mult < 0 ? (maxLv - curLv) : Math.Min(mult, maxLv - curLv);

            if (mult < 0)
            {
                // MAX mode: buy as many affordable levels as possible (at least preview 1 if broke)
                double running = 0.0;
                int count = 0;
                for (int k = 0; k < targetMax; k++)
                {
                    double c = BalanceTables.GetCoinCost(id, curLv + k);
                    if (running + c > session.Coins) break;
                    running += c;
                    count++;
                }
                if (count == 0)
                {
                    totalCost = BalanceTables.GetCoinCost(id, curLv);
                    return 0;
                }
                totalCost = running;
                return count;
            }
            else
            {
                for (int k = 0; k < targetMax; k++)
                {
                    totalCost += BalanceTables.GetCoinCost(id, curLv + k);
                }
                return session.Coins >= totalCost ? targetMax : 0;
            }
        }

        private void TickCardsTab(GameSession session)
        {
            for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
            {
                EquippedSlotWidget es = _equipSlots[i];
                int eq = session.EquippedCards[i];
                int star = eq >= 0 ? session.GetCardStar((CardId)eq) : 0;
                int nextUnlockCost = i < BalanceTables.CardSlotUnlockGems.Length ? BalanceTables.CardSlotUnlockGems[i] : 999;
                bool canUnlockSlot = session.CardsUnlocked && i == session.UnlockedCardSlots && session.Gems >= nextUnlockCost;

                int h = (i * 997) ^ (session.UnlockedCardSlots * 31) ^ ((eq + 2) * 17) ^ star ^ (canUnlockSlot ? 0x1000 : 0) ^ (session.CardsUnlocked ? 0x2000 : 0);
                if (h != es.CachedHash)
                {
                    es.CachedHash = h;
                    if (i < session.UnlockedCardSlots)
                    {
                        if (eq >= 0)
                        {
                            es.Bg.material = _style.MatCardRare;
                            es.Icon.sprite = _style.GetIcon(UiIcons.CardIcons[eq]);
                            es.Icon.color = Color.white;
                            es.Title.text = BalanceTables.Cards[eq].TitleRu;
                            es.Title.color = UiStyle.TextPrimary;
                            es.Sub.text = (star >= 3 ? "★★★" : (star == 2 ? "★★☆" : "★☆☆")) + " • СНЯТЬ";
                            es.Sub.color = UiStyle.TextAmber;
                            es.Btn.interactable = true;
                        }
                        else
                        {
                            es.Bg.material = _style.MatTabInactive;
                            es.Icon.sprite = _style.GetIcon(UiIcons.TabCards);
                            es.Icon.color = new Color(0.55f, 0.65f, 0.76f, 0.6f);
                            es.Title.text = "СЛОТ #" + (i + 1) + "\nСВОБОДЕН";
                            es.Title.color = UiStyle.TextSecondary;
                            es.Sub.text = "ВЫБЕРИТЕ КАРТУ";
                            es.Sub.color = UiStyle.TextCyan;
                            es.Btn.interactable = false;
                        }
                    }
                    else if (i == session.UnlockedCardSlots)
                    {
                        es.Bg.material = canUnlockSlot ? _style.MatBuyCyan : _style.MatCardLocked;
                        es.Icon.sprite = _style.GetIcon(UiIcons.Gems);
                        es.Icon.color = canUnlockSlot ? Color.white : new Color(0.5f, 0.55f, 0.62f, 0.6f);
                        es.Title.text = "+ ОТКРЫТЬ СЛОТ #" + (i + 1);
                        es.Title.color = canUnlockSlot ? UiStyle.TextPrimary : UiStyle.TextSecondary;
                        es.Sub.text = nextUnlockCost + " ГЕМОВ";
                        es.Sub.color = canUnlockSlot ? UiStyle.TextAmber : UiStyle.TextMuted;
                        es.Btn.interactable = canUnlockSlot;
                    }
                    else
                    {
                        es.Bg.material = _style.MatCardLocked;
                        es.Icon.sprite = _style.GetIcon(UiIcons.TabCards);
                        es.Icon.color = new Color(0.35f, 0.40f, 0.46f, 0.4f);
                        es.Title.text = "СЛОТ #" + (i + 1);
                        es.Title.color = UiStyle.TextMuted;
                        es.Sub.text = "ЗАКРЫТ";
                        es.Sub.color = UiStyle.TextMuted;
                        es.Btn.interactable = false;
                    }
                }
            }

            bool allMaxed = session.AllCardsMaxed();
            bool canPull = session.CardsUnlocked && !allMaxed && session.Gems >= BalanceTables.CARD_PULL_COST_GEMS;
            int gachaHash = (session.CardsUnlocked ? 1 : 0) | (allMaxed ? 2 : 0) | (canPull ? 4 : 0);
            if (gachaHash != _cachedGachaBtnHash)
            {
                _cachedGachaBtnHash = gachaHash;
                _gachaPullBtn.interactable = canPull;
                if (!session.CardsUnlocked)
                {
                    _gachaPullBg.material = _style.MatBuyDisabled;
                    _gachaPullLbl.color = UiStyle.TextMuted;
                    _gachaPullLbl.text = "ОТКРЫТИЕ КАРТ: T1 W20";
                }
                else if (allMaxed)
                {
                    _gachaPullBg.material = _style.MatBuyGreen;
                    _gachaPullLbl.color = UiStyle.TextPrimary;
                    _gachaPullLbl.text = "✓ ВСЕ КАРТЫ ★★★ МАКС!";
                }
                else
                {
                    _gachaPullBg.material = canPull ? _style.MatBuyAmber : _style.MatBuyDisabled;
                    _gachaPullLbl.color = canPull ? UiStyle.TextButtonDark : UiStyle.TextMuted;
                    _gachaPullLbl.text = "◆ ТЯНУТЬ КАРТУ • 20 ГЕМОВ";
                }
            }

            for (int i = 0; i < GameIdsAndScale.CARD_COUNT; i++)
            {
                CardModuleWidget cw = _cardWidgets[i];
                CardId cid = (CardId)i;
                ref readonly CardDef def = ref BalanceTables.Cards[i];
                int copies = session.CardCopies[i];
                int star = session.GetCardStar(cid);
                bool equipped = session.IsCardEquipped(cid);

                int ch = (copies * 31) ^ (star * 7) ^ (equipped ? 0x400 : 0) ^ (session.UnlockedCardSlots << 12);
                if (ch == cw.CachedHash) continue;
                cw.CachedHash = ch;

                if (copies <= 0)
                {
                    cw.Bg.material = _style.MatCardLocked;
                    cw.Icon.color = new Color(0.42f, 0.48f, 0.56f, 0.50f);
                }
                else
                {
                    cw.Icon.color = Color.white;
                    cw.Bg.material = def.Rarity == CardRarity.Epic
                        ? _style.MatCardEpic
                        : (def.Rarity == CardRarity.Rare ? _style.MatCardRare : _style.MatStatCard);
                }

                switch (def.Rarity)
                {
                    case CardRarity.Epic:
                        cw.RarityTag.text = "◆ ЭПИЧЕСКИЙ МОДУЛЬ";
                        cw.RarityTag.color = UiStyle.TextAmber;
                        break;
                    case CardRarity.Rare:
                        cw.RarityTag.text = "◆ РЕДКИЙ МОДУЛЬ";
                        cw.RarityTag.color = UiStyle.TextCyan;
                        break;
                    default:
                        cw.RarityTag.text = "● ОБЫЧНЫЙ МОДУЛЬ";
                        cw.RarityTag.color = UiStyle.TextSecondary;
                        break;
                }

                _sb.Clear();
                if (star <= 0) _sb.Append("☆☆☆");
                else if (star == 1) _sb.Append("★☆☆");
                else if (star == 2) _sb.Append("★★☆");
                else _sb.Append("★★★");
                _sb.Append("  Копии: ").Append(copies).Append('/').Append(BalanceTables.MAX_CARD_COPIES);
                cw.StarsAndCopies.text = _sb.ToString();

                if (copies <= 0)
                {
                    cw.EquipBtn.interactable = false;
                    cw.EquipBg.material = _style.MatBuyDisabled;
                    cw.EquipLabel.color = UiStyle.TextMuted;
                    cw.EquipLabel.text = "НЕ НАЙДЕНА В ГАЧЕ";
                }
                else if (equipped)
                {
                    cw.EquipBtn.interactable = true;
                    cw.EquipBg.material = _style.MatBuyGreen;
                    cw.EquipLabel.color = UiStyle.TextPrimary;
                    cw.EquipLabel.text = "✓ АКТИВНА — СНЯТЬ";
                }
                else
                {
                    cw.EquipBtn.interactable = true;
                    cw.EquipBg.material = _style.MatBuyCyan;
                    cw.EquipLabel.color = UiStyle.TextPrimary;
                    cw.EquipLabel.text = "ВСТАВИТЬ В СЛОТ";
                }
            }
        }

        private void TickLabsTab(GameSession session)
        {
            int freeSlot = FindFirstFreeLabSlot(session);

            for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
            {
                LabSlotWidget lw = _labSlotWidgets[i];
                bool slotUnlocked = session.LabsUnlocked && i < session.UnlockedLabSlots;
                ref readonly LabSlotState ls = ref session.LabSlots[i];

                if (slotUnlocked && ls.Active && _style.MatLabProgress[i] != null)
                {
                    float progress = ls.TotalSeconds > 0.01f
                        ? Mathf.Clamp01(1f - ls.RemainingSeconds / ls.TotalSeconds)
                        : 1f;
                    _style.MatLabProgress[i].SetFloat(UiStyle.PropFill, progress);
                    UiFormat.SetTimerMmSs(lw.TimerTmp, ls.RemainingSeconds, ref lw.CachedSec);
                }
                else if (_style.MatLabProgress[i] != null)
                {
                    _style.MatLabProgress[i].SetFloat(UiStyle.PropFill, 0f);
                }

                int rushCost = (slotUnlocked && ls.Active) ? BalanceTables.GetLabRushGemCost(ls.RemainingSeconds) : 0;
                int boostRemMin = (slotUnlocked && ls.BoostRemainingSeconds > 0f) ? Mathf.CeilToInt(ls.BoostRemainingSeconds / 60f) : 0;
                int unlockCost = i < BalanceTables.LabSlotUnlockGems.Length ? BalanceTables.LabSlotUnlockGems[i] : 999;
                bool canUnlockNext = session.LabsUnlocked && i == session.UnlockedLabSlots && session.Gems >= unlockCost;
                bool canRush = slotUnlocked && ls.Active && session.Gems >= rushCost;
                bool canB2 = slotUnlocked && session.EliteCells >= BalanceTables.CELLS_COST_BOOST_2X;
                bool canB3 = slotUnlocked && session.EliteCells >= BalanceTables.CELLS_COST_BOOST_3X;

                int h = (slotUnlocked ? 0x10000 : 0)
                    ^ (ls.Active ? 0x20000 : 0)
                    ^ ((int)ls.Lab << 12)
                    ^ (rushCost * 31)
                    ^ (boostRemMin * 7)
                    ^ (canUnlockNext ? 1 : 0)
                    ^ (canRush ? 2 : 0)
                    ^ (canB2 ? 4 : 0)
                    ^ (canB3 ? 8 : 0)
                    ^ (session.LabsUnlocked ? 16 : 0);

                if (h != lw.CachedHash)
                {
                    lw.CachedHash = h;
                    lw.CachedSec = -1;

                    if (!slotUnlocked)
                    {
                        lw.Icon.sprite = _style.GetIcon(UiIcons.TabLabs);
                        lw.Icon.color = new Color(0.40f, 0.46f, 0.54f, 0.45f);
                        lw.TimerTmp.text = "ЗАКРЫТ";
                        lw.TimerTmp.color = UiStyle.TextMuted;
                        lw.BoostTag.text = "";
                        lw.Boost2Btn.gameObject.SetActive(false);
                        lw.Boost3Btn.gameObject.SetActive(false);
                        lw.RushBtn.gameObject.SetActive(false);

                        if (!session.LabsUnlocked)
                        {
                            lw.Title.text = "Слот лаборатории #" + (i + 1) + " (открытие на T1 W15)";
                            lw.UnlockBtn.gameObject.SetActive(false);
                        }
                        else if (i == session.UnlockedLabSlots)
                        {
                            lw.Title.text = "Дополнительный слот #" + (i + 1);
                            lw.UnlockBtn.gameObject.SetActive(true);
                            lw.UnlockBtn.interactable = canUnlockNext;
                            lw.UnlockBg.material = canUnlockNext ? _style.MatBuyCyan : _style.MatBuyDisabled;
                            lw.UnlockLbl.color = canUnlockNext ? UiStyle.TextPrimary : UiStyle.TextMuted;
                            lw.UnlockLbl.text = "+ ОТКРЫТЬ СЛОТ • " + unlockCost + " ГЕМОВ";
                        }
                        else
                        {
                            lw.Title.text = "Слот лаборатории #" + (i + 1);
                            lw.UnlockBtn.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        lw.UnlockBtn.gameObject.SetActive(false);
                        lw.Boost2Btn.gameObject.SetActive(true);
                        lw.Boost3Btn.gameObject.SetActive(true);
                        lw.Boost2Btn.interactable = canB2;
                        lw.Boost3Btn.interactable = canB3;
                        lw.Boost2Bg.material = canB2 ? _style.MatBuyCyan : _style.MatBuyDisabled;
                        lw.Boost3Bg.material = canB3 ? _style.MatCardEpic : _style.MatBuyDisabled;

                        if (ls.BoostRemainingSeconds > 0f && ls.BoostMult > 1.1f)
                        {
                            _sb.Clear();
                            _sb.Append("⚡ БУСТ ×").Append(Mathf.RoundToInt(ls.BoostMult))
                               .Append(" (").Append(boostRemMin).Append("м)");
                            lw.BoostTag.text = _sb.ToString();
                        }
                        else
                        {
                            lw.BoostTag.text = "Буст клеток: ×1.0";
                        }

                        if (ls.Active)
                        {
                            int nextLv = session.LabLevels[(int)ls.Lab] + 1;
                            lw.Icon.sprite = _style.GetIcon(UiIcons.LabIcons[(int)ls.Lab]);
                            lw.Icon.color = Color.white;
                            lw.Title.text = BalanceTables.Labs[(int)ls.Lab].TitleRu + " → Ур. " + nextLv;
                            lw.TimerTmp.color = UiStyle.TextCyan;
                            UiFormat.SetTimerMmSs(lw.TimerTmp, ls.RemainingSeconds, ref lw.CachedSec);

                            lw.RushBtn.gameObject.SetActive(true);
                            lw.RushBtn.interactable = canRush;
                            lw.RushBg.material = canRush ? _style.MatBuyAmber : _style.MatBuyDisabled;
                            lw.RushLbl.color = canRush ? UiStyle.TextButtonDark : UiStyle.TextMuted;
                            lw.RushLbl.text = "УСКОРИТЬ\n" + rushCost + " ГЕМ.";
                        }
                        else
                        {
                            lw.Icon.sprite = _style.GetIcon(UiIcons.TabLabs);
                            lw.Icon.color = new Color(0.65f, 0.78f, 0.92f, 0.8f);
                            lw.Title.text = "Слот #" + (i + 1) + " свободен — выберите проект ниже";
                            lw.TimerTmp.text = "ГОТОВ";
                            lw.TimerTmp.color = UiStyle.TextEmerald;
                            lw.RushBtn.gameObject.SetActive(false);
                        }
                    }
                }
            }

            // 6 Research Cards
            for (int i = 0; i < GameIdsAndScale.LAB_COUNT; i++)
            {
                LabResearchWidget rw = _labWidgets[i];
                LabId lid = (LabId)i;
                ref readonly LabDef def = ref BalanceTables.Labs[i];
                int curLv = session.LabLevels[i];
                bool activeNow = session.IsLabCurrentlyActive(lid);
                bool maxed = curLv >= def.MaxLevel;
                bool reqBhMissing = lid == LabId.BlackHoleCoinBonus && !session.UwUnlocked[(int)UwId.BlackHole];
                int coinCost = maxed ? 0 : BalanceTables.GetLabCoinCost(lid, curLv);
                bool canStart = session.LabsUnlocked && !maxed && !activeNow && !reqBhMissing && freeSlot >= 0 && session.Coins >= coinCost;

                int rh = (curLv * 97)
                    ^ (activeNow ? 0x100 : 0)
                    ^ (maxed ? 0x200 : 0)
                    ^ (reqBhMissing ? 0x400 : 0)
                    ^ (canStart ? 0x800 : 0)
                    ^ (freeSlot >= 0 ? 0x1000 : 0)
                    ^ (session.LabsUnlocked ? 0x2000 : 0);

                if (rh == rw.CachedHash) continue;
                rw.CachedHash = rh;

                _sb.Clear();
                _sb.Append("Ур. ").Append(curLv).Append(" / ").Append(def.MaxLevel);
                rw.LevelBadge.text = _sb.ToString();

                if (maxed)
                {
                    rw.Bg.material = _style.MatCardRare;
                    rw.DurationTmp.text = "Исследование полностью завершено";
                    rw.StartBtn.interactable = false;
                    rw.StartBg.material = _style.MatBuyGreen;
                    rw.StartLbl.color = UiStyle.TextPrimary;
                    rw.StartLbl.text = "✓ МАКСИМУМ";
                }
                else
                {
                    float durSec = BalanceTables.GetLabDurationSeconds(lid, curLv);
                    int dm = Mathf.FloorToInt(durSec / 60f);
                    int ds = Mathf.RoundToInt(durSec % 60f);
                    _sb.Clear();
                    _sb.Append("Время: ").Append(dm.ToString("00")).Append(':').Append(ds.ToString("00"));
                    rw.DurationTmp.text = _sb.ToString();

                    if (!session.LabsUnlocked)
                    {
                        rw.Bg.material = _style.MatCardLocked;
                        rw.StartBtn.interactable = false;
                        rw.StartBg.material = _style.MatBuyDisabled;
                        rw.StartLbl.color = UiStyle.TextMuted;
                        rw.StartLbl.text = "ОТКРЫТИЕ ЛАБ: T1 W15";
                    }
                    else if (activeNow)
                    {
                        rw.Bg.material = _style.MatCardRare;
                        rw.StartBtn.interactable = false;
                        rw.StartBg.material = _style.MatBuyGreen;
                        rw.StartLbl.color = UiStyle.TextPrimary;
                        rw.StartLbl.text = "⚡ ИССЛЕДУЕТСЯ...";
                    }
                    else if (reqBhMissing)
                    {
                        rw.Bg.material = _style.MatCardLocked;
                        rw.StartBtn.interactable = false;
                        rw.StartBg.material = _style.MatBuyDisabled;
                        rw.StartLbl.color = UiStyle.TextMuted;
                        rw.StartLbl.text = "НУЖЕН ЭЛЕКТРОМАГНИТ BH";
                    }
                    else
                    {
                        rw.Bg.material = _style.MatStatCard;
                        rw.StartBtn.interactable = canStart;
                        rw.StartBg.material = canStart ? _style.MatBuyAmber : _style.MatBuyDisabled;
                        rw.StartLbl.color = canStart ? UiStyle.TextButtonDark : UiStyle.TextMuted;

                        _sb.Clear();
                        if (freeSlot < 0)
                        {
                            _sb.Append("СЛОТЫ ЗАНЯТЫ (");
                            UiFormat.AppendCompactToSb(_sb, coinCost);
                            _sb.Append(')');
                        }
                        else
                        {
                            _sb.Append("ИССЛЕДОВАТЬ • ");
                            UiFormat.AppendCompactToSb(_sb, coinCost);
                            _sb.Append(" МОНЕТ");
                        }
                        rw.StartLbl.text = _sb.ToString();
                    }
                }
            }
        }

        private void TickUwsTab(GameSession session)
        {
            int nextUnlockCost = session.GetNextUwUnlockStoneCost();
            bool allUnlocked = session.UwUnlockedCount >= GameIdsAndScale.UW_COUNT;
            bool canUnlock = session.UwSystemUnlocked && !allUnlocked && session.Stones >= nextUnlockCost;

            int topHash = (session.UwSystemUnlocked ? 1 : 0)
                | (allUnlocked ? 2 : 0)
                | (canUnlock ? 4 : 0)
                | (session.UwUnlockedCount << 4)
                | (session.Stones << 8);

            if (topHash != _cachedUwTopHash)
            {
                _cachedUwTopHash = topHash;
                _sb.Clear();
                _sb.Append("РЕАКТОР АБСОЛЮТНОГО ОРУЖИЯ (").Append(session.UwUnlockedCount).Append("/3 ОТКРЫТО)\n");
                _sb.Append("<color=#52E5FF>СИНХРОН GT + BH: ЕДИНЫЙ ЦИКЛ ").Append((int)BalanceTables.UW_SYNCED_COOLDOWN_SEC).Append("с</color>");
                _uwHeaderStatusTmp.text = _sb.ToString();

                _uwUnlockModalBtn.interactable = canUnlock;
                if (!session.UwSystemUnlocked)
                {
                    _uwUnlockModalBg.material = _style.MatBuyDisabled;
                    _uwUnlockModalLbl.color = UiStyle.TextMuted;
                    _uwUnlockModalLbl.text = "ОТКРЫТИЕ УЛЬТ: T1 W30";
                }
                else if (allUnlocked)
                {
                    _uwUnlockModalBg.material = _style.MatBuyGreen;
                    _uwUnlockModalLbl.color = UiStyle.TextPrimary;
                    _uwUnlockModalLbl.text = "✓ ВСЕ 3 УЛЬТЫ ОТКРЫТЫ";
                }
                else
                {
                    _uwUnlockModalBg.material = canUnlock ? _style.MatBuyAmber : _style.MatBuyDisabled;
                    _uwUnlockModalLbl.color = canUnlock ? UiStyle.TextButtonDark : UiStyle.TextMuted;
                    _uwUnlockModalLbl.text = "⚡ ВЫБРАТЬ УЛЬТУ • " + nextUnlockCost + " КАМНЕЙ";
                }
            }

            var ic = CultureInfo.InvariantCulture;
            for (int i = 0; i < GameIdsAndScale.UW_COUNT; i++)
            {
                UwReactorWidget uw = _uwWidgets[i];
                UwId id = (UwId)i;
                ref readonly UwDef def = ref BalanceTables.Uws[i];
                bool unlocked = session.UwUnlocked[i];
                int lv = session.UwLevels[i];
                int upCost = session.GetUwUpgradeStoneCost(id);
                bool canUp = unlocked && lv < def.MaxUpgradeLevel && session.Stones >= upCost;
                int bhLabLv = session.LabLevels[(int)LabId.BlackHoleCoinBonus];

                int h = (unlocked ? 0x1000 : 0) ^ (lv * 97) ^ (canUp ? 0x2000 : 0) ^ (bhLabLv * 13);
                if (h == uw.CachedHash) continue;
                uw.CachedHash = h;

                uw.Bg.material = unlocked ? _style.MatCardEpic : _style.MatCardLocked;
                uw.Icon.color = unlocked ? Color.white : new Color(0.42f, 0.48f, 0.56f, 0.5f);

                if (unlocked)
                {
                    uw.StatusBadge.color = UiStyle.TextEmerald;
                    uw.StatusBadge.text = "АКТИВНА • УР. " + (lv + 1) + "/" + (def.MaxUpgradeLevel + 1);
                }
                else
                {
                    uw.StatusBadge.color = UiStyle.TextMuted;
                    uw.StatusBadge.text = "ЗАБЛОКИРОВАНО";
                }

                _sb.Clear();
                switch (id)
                {
                    case UwId.GoldenTower:
                        _sb.Append("Множитель Cash и Coins: <color=#FFD13D>×")
                           .Append(BalanceTables.GetGoldenTowerMult(lv).ToString("0.0", ic))
                           .Append("</color>  •  Длительность: <color=#52E5FF>")
                           .Append(BalanceTables.GetGoldenTowerDuration(lv).ToString("0", ic))
                           .Append("с</color>  •  КД: ").Append((int)BalanceTables.UW_SYNCED_COOLDOWN_SEC).Append('с');
                        break;
                    case UwId.BlackHole:
                        float radM = BalanceTables.GetBlackHoleRadiusSim(lv) * GameIdsAndScale.WORLD_SCALE;
                        _sb.Append("Монеты в воронке: <color=#FFD13D>×")
                           .Append(BalanceTables.GetBlackHoleCoinMult(bhLabLv).ToString("0.0", ic))
                           .Append("</color>  •  Радиус: <color=#52E5FF>")
                           .Append(radM.ToString("0.1", ic))
                           .Append("м</color>  •  Длит.: ")
                           .Append(BalanceTables.GetBlackHoleDuration(lv).ToString("0", ic)).Append('с');
                        break;
                    case UwId.ChainLightning:
                        _sb.Append("Урон дуги: <color=#FFD13D>×")
                           .Append(BalanceTables.GetChainLightningDamageMult(lv).ToString("0.0", ic))
                           .Append("</color>  •  Целей: <color=#52E5FF>")
                           .Append(BalanceTables.GetChainLightningTargets(lv))
                           .Append("</color>  •  Шанс: ")
                           .Append((BalanceTables.GetChainLightningChance(lv) * 100f).ToString("0", ic)).Append('%');
                        break;
                }
                uw.LiveStats.text = _sb.ToString();

                if (!unlocked)
                {
                    uw.UpgradeBtn.interactable = false;
                    uw.UpgradeBg.material = _style.MatBuyDisabled;
                    uw.UpgradeLbl.color = UiStyle.TextMuted;
                    uw.UpgradeLbl.text = "ОТКРОЙТЕ ЧЕРЕЗ РЕАКТОР ВЫШЕ";
                }
                else if (lv >= def.MaxUpgradeLevel)
                {
                    uw.UpgradeBtn.interactable = false;
                    uw.UpgradeBg.material = _style.MatBuyGreen;
                    uw.UpgradeLbl.color = UiStyle.TextPrimary;
                    uw.UpgradeLbl.text = "✓ МАКС. МОЩНОСТЬ";
                }
                else
                {
                    uw.UpgradeBtn.interactable = canUp;
                    uw.UpgradeBg.material = canUp ? _style.MatBuyCyan : _style.MatBuyDisabled;
                    uw.UpgradeLbl.color = canUp ? UiStyle.TextPrimary : UiStyle.TextMuted;
                    uw.UpgradeLbl.text = "ПРОКАЧАТЬ • " + upCost + " КАМНЕЙ";
                }
            }

            int botHash = session.GoldenBotUnlocked ? 1 : 0;
            if (botHash != _cachedBotHash)
            {
                _cachedBotHash = botHash;
                _botCardBg.material = session.GoldenBotUnlocked ? _style.MatCardRare : _style.MatStatCard;

                _sb.Clear();
                if (session.GoldenBotUnlocked)
                {
                    _sb.Append("<color=#FFD13D>★ ЗОЛОТОЙ ДРОН (АКТИВЕН В БОЮ)</color>\n");
                }
                else
                {
                    _sb.Append("<color=#ADBBD1>ЗОЛОТОЙ ДРОН (ОТКРЫТИЕ НА ВЕХЕ T1 W50)</color>\n");
                }
                _sb.Append("Автономное поле ×2.0 МОНЕТ на 10с каждые 25с (мультипликативно с GT × BH!).\n\n");
                _sb.Append("<color=#52E5FF>◆ КУРЬЕРСКИЙ ДРОН ГЕМОВ (ВСЕГДА АКТИВЕН)</color>\n");
                _sb.Append("Пролетает над ареной каждые 165с реального боя: перехват даёт +5 ГЕМОВ.");
                _botStatusTmp.text = _sb.ToString();
            }
        }

        private void TickMilestonesTab(GameSession session)
        {
            int tierHash = (session.ActiveTier * 31) ^ (session.Tier2Unlocked ? 0x100 : 0) ^ (session.BestWaveT1 * 7) ^ (session.BestWaveT2 * 13);
            if (tierHash != _cachedMsTierHash)
            {
                _cachedMsTierHash = tierHash;
                _msTier1BtnBg.material = session.ActiveTier == 1 ? _style.MatTabActive : _style.MatTabInactive;
                _msTier1Tmp.color = session.ActiveTier == 1 ? UiStyle.TextAmber : UiStyle.TextSecondary;
                _msTier1Tmp.text = "ТИР 1: СТАНДАРТНЫЙ ЦЕХ\n×1.0 HP • ×1.0 МОНЕТ (РЕК: W" + session.BestWaveT1 + ")";

                if (session.Tier2Unlocked)
                {
                    _msTier2BtnBg.material = session.ActiveTier == 2 ? _style.MatCardEpic : _style.MatTabInactive;
                    _msTier2Tmp.color = session.ActiveTier == 2 ? UiStyle.TextAmber : UiStyle.TextPrimary;
                    _msTier2Tmp.text = "★ ТИР 2: ТИТАНОВЫЙ ФОРСАЖ\n×20 HP • ×1.8 МОНЕТ (РЕК: W" + session.BestWaveT2 + ")";
                }
                else
                {
                    _msTier2BtnBg.material = _style.MatCardLocked;
                    _msTier2Tmp.color = UiStyle.TextMuted;
                    _msTier2Tmp.text = "ТИР 2: ТИТАНОВЫЙ ФОРСАЖ\nОТКРОЕТСЯ НА T1 ВОЛНЕ 100";
                }
            }

            for (int i = 0; i < BalanceTables.Tier1Milestones.Length; i++)
            {
                MilestoneCardWidget mw = _msWidgets[i];
                int st = session.ClaimedMilestonesT1[i] ? 1 : 0;
                if (st == mw.CachedState) continue;
                mw.CachedState = st;

                if (st == 1)
                {
                    mw.Bg.material = _style.MatCardRare;
                    mw.StatusTag.color = UiStyle.TextEmerald;
                    mw.StatusTag.text = "✓ ПОЛУЧЕНО";
                }
                else
                {
                    mw.Bg.material = _style.MatStatCard;
                    mw.StatusTag.color = UiStyle.TextSecondary;
                    mw.StatusTag.text = "ЦЕЛЬ: W" + BalanceTables.Tier1Milestones[i].Wave;
                }
            }
        }

        // --- Button Callbacks ---

        private void SelectMainTabByUser(int index)
        {
            UiStyle.PlaySfx(SfxId.UiTap);
            SetActiveTab(index);
            _tweens.PunchScale(_mainTabBgs[index].rectTransform, 0.08f, 0.16f);
        }

        private void ApplyMainTabVisuals()
        {
            for (int i = 0; i < MainTabCount; i++)
            {
                bool active = i == _activeTab;
                if (_tabPanels[i] != null && _tabPanels[i].gameObject.activeSelf != active)
                {
                    _tabPanels[i].gameObject.SetActive(active);
                }
                if (_mainTabBgs[i] != null)
                {
                    _mainTabBgs[i].material = active ? _style.MatTabActive : _style.MatTabInactive;
                }
                if (_mainTabLabels[i] != null)
                {
                    _mainTabLabels[i].color = active ? UiStyle.TextAmber : UiStyle.TextSecondary;
                }
            }
        }

        private void ApplyWsSubTabVisuals()
        {
            for (int i = 0; i < 3; i++)
            {
                bool active = i == _wsSubTab;
                if (_wsSubTabBgs[i] != null)
                {
                    _wsSubTabBgs[i].material = active ? _style.MatTabActive : _style.MatTabInactive;
                }
                if (_wsSubTabLabels[i] != null)
                {
                    _wsSubTabLabels[i].color = active ? UiStyle.TextAmber : UiStyle.TextSecondary;
                }
            }
        }

        private void InvalidateWorkshopCache()
        {
            for (int i = 0; i < MaxWorkshopPool; i++)
            {
                if (_wsCards[i] != null) _wsCards[i].CachedHash = long.MinValue;
            }
        }

        private void OnClickSkinToggle()
        {
            if (_boundSession == null || !_boundSession.MoltenCoreSkinUnlocked) return;
            _boundSession.MoltenCoreSkinEquipped = !_boundSession.MoltenCoreSkinEquipped;
            if (_boundSession.InRun) _boundSession.PushStatsToCombat();
            _cachedCmdHash = int.MinValue;
            UiStyle.PlaySfx(SfxId.UiTap);
            _tweens.PunchScale(_skinBtnBg.rectTransform, 0.08f, 0.16f);
        }

        private void OnClickTierToggle()
        {
            if (_boundSession == null) return;
            if (!_boundSession.Tier2Unlocked)
            {
                UiStyle.PlaySfx(SfxId.UiTap);
                SetActiveTab(TabMilestones);
                return;
            }
            _boundSession.ActiveTier = _boundSession.ActiveTier == 1 ? 2 : 1;
            _cachedCmdHash = int.MinValue;
            _cachedMsTierHash = int.MinValue;
            UiStyle.PlaySfx(SfxId.UiUpgrade);
            _tweens.PunchScale(_tierBtnBg.rectTransform, 0.09f, 0.16f);
        }

        private void SelectTierFromMilestones(int tier)
        {
            if (_boundSession == null) return;
            if (tier == 2 && !_boundSession.Tier2Unlocked) return;
            _boundSession.ActiveTier = tier;
            _cachedCmdHash = int.MinValue;
            _cachedMsTierHash = int.MinValue;
            UiStyle.PlaySfx(SfxId.UiTap);
        }

        private void OnClickStartRun()
        {
            if (_boundSession == null) return;
            UiStyle.PlaySfx(SfxId.UiUnlock);
            _boundSession.StartNewRun(_boundSession.ActiveTier);
            _onStartRun?.Invoke();
        }

        private void OnClickWorkshopCard(int slotIdx)
        {
            if (_boundSession == null || (uint)slotIdx >= (uint)MaxWorkshopPool) return;
            WorkshopCardWidget w = _wsCards[slotIdx];
            StatId id = w.BoundStat;

            if (!_boundSession.IsStatUnlocked(id))
            {
                if (_boundSession.TryUnlockWorkshopBranch(id))
                {
                    UiStyle.PlaySfx(SfxId.UiUnlock);
                    _tweens.PunchScale(w.Root, 0.10f, 0.18f);
                    InvalidateWorkshopCache();
                }
            }
            else
            {
                int curLv = _boundSession.WorkshopLevels[(int)id];
                int maxLv = BalanceTables.Stats[(int)id].MaxLevel;
                int count = ComputeWorkshopBuyCount(_boundSession, id, curLv, maxLv, true, out _);
                if (count > 0 && _boundSession.TryBuyWorkshopUpgrade(id, count))
                {
                    UiStyle.PlaySfx(SfxId.UiUpgrade);
                    _tweens.PunchScale(w.ActionBg.rectTransform, 0.10f, 0.16f);
                    InvalidateWorkshopCache();
                }
            }
        }

        private void OnClickEquippedCardSlot(int slotIdx)
        {
            if (_boundSession == null) return;
            if (slotIdx < _boundSession.UnlockedCardSlots)
            {
                if (_boundSession.EquippedCards[slotIdx] >= 0)
                {
                    _boundSession.EquippedCards[slotIdx] = -1;
                    if (_boundSession.InRun) _boundSession.PushStatsToCombat();
                    UiStyle.PlaySfx(SfxId.UiTap);
                }
            }
            else if (slotIdx == _boundSession.UnlockedCardSlots)
            {
                if (_boundSession.TryUnlockCardSlot())
                {
                    UiStyle.PlaySfx(SfxId.UiUnlock);
                    _tweens.PunchScale(_equipSlots[slotIdx].Root, 0.11f, 0.18f);
                }
            }
        }

        private void OnClickPullCard()
        {
            if (_boundSession == null) return;
            if (_boundSession.TryPullCard(out CardId pulled, out int newStar))
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _tweens.PunchScale(_gachaPullBg.rectTransform, 0.10f, 0.18f);
                _tweens.PunchScale(_cardWidgets[(int)pulled].Root, 0.09f, 0.20f);

                _sb.Clear();
                _sb.Append("★ ПОЛУЧЕН МОДУЛЬ: <color=#FFD13D>")
                   .Append(BalanceTables.Cards[(int)pulled].TitleRu)
                   .Append("</color> (★").Append(newStar).Append(")!");
                _gachaStatusTmp.text = _sb.ToString();
            }
        }

        private void OnClickToggleEquipCard(CardId id)
        {
            if (_boundSession == null || _boundSession.CardCopies[(int)id] <= 0) return;

            if (_boundSession.IsCardEquipped(id))
            {
                for (int i = 0; i < _boundSession.UnlockedCardSlots; i++)
                {
                    if (_boundSession.EquippedCards[i] == (int)id)
                    {
                        _boundSession.EquippedCards[i] = -1;
                    }
                }
                if (_boundSession.InRun) _boundSession.PushStatsToCombat();
                UiStyle.PlaySfx(SfxId.UiTap);
                return;
            }

            int targetSlot = 0;
            for (int i = 0; i < _boundSession.UnlockedCardSlots; i++)
            {
                if (_boundSession.EquippedCards[i] < 0)
                {
                    targetSlot = i;
                    break;
                }
                targetSlot = i;
            }

            if (_boundSession.TryEquipCard(id, targetSlot))
            {
                UiStyle.PlaySfx(SfxId.UiUpgrade);
                _tweens.PunchScale(_cardWidgets[(int)id].EquipBg.rectTransform, 0.09f, 0.16f);
            }
        }

        private void OnClickUnlockLabSlot()
        {
            if (_boundSession == null) return;
            if (_boundSession.TryUnlockLabSlot())
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
            }
        }

        private void OnClickBoostLab(int slotIdx, bool boost3x)
        {
            if (_boundSession == null) return;
            if (_boundSession.TryBoostLabSlotWithCells(slotIdx, boost3x))
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _tweens.PunchScale(_labSlotWidgets[slotIdx].Root, 0.06f, 0.16f);
            }
        }

        private void OnClickRushLab(int slotIdx)
        {
            if (_boundSession == null) return;
            if (_boundSession.TryRushLabSlotWithGems(slotIdx))
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _tweens.PunchScale(_labSlotWidgets[slotIdx].Root, 0.07f, 0.18f);
            }
        }

        private void OnClickStartLab(LabId id)
        {
            if (_boundSession == null) return;
            int freeSlot = FindFirstFreeLabSlot(_boundSession);
            if (freeSlot < 0) return;

            if (_boundSession.TryStartLab(id, freeSlot))
            {
                UiStyle.PlaySfx(SfxId.UiUpgrade);
                _tweens.PunchScale(_labWidgets[(int)id].StartBg.rectTransform, 0.09f, 0.16f);
            }
        }

        private void OnClickUpgradeUw(UwId id)
        {
            if (_boundSession == null) return;
            if (_boundSession.TryUpgradeUw(id))
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _tweens.PunchScale(_uwWidgets[(int)id].UpgradeBg.rectTransform, 0.10f, 0.18f);
            }
        }

        public void ApplyLayoutMode(bool isLandscape)
        {
            _isLandscape = isLandscape;
            _cachedCmdHash = int.MinValue;
            _cachedMuteState = -1;

            if (_windowRect == null) return;

            if (!isLandscape)
            {
                // --- PORTRAIT 1080x2400 MOBILE HANGAR ---
                _bgOverlay.color = new Color(0.04f, 0.06f, 0.09f, 0.78f);
                _windowBgImg.enabled = false;
                _windowRect.anchorMin = Vector2.zero;
                _windowRect.anchorMax = Vector2.one;
                _windowRect.pivot = new Vector2(0.5f, 0.5f);
                _windowRect.offsetMin = Vector2.zero;
                _windowRect.offsetMax = Vector2.zero;

                _topBarRect.anchoredPosition = new Vector2(0f, -16f);
                _topBarRect.sizeDelta = new Vector2(1040f, 198f);
                _topTitleTmp.fontSize = 30f;
                _topTitleTmp.rectTransform.anchoredPosition = new Vector2(22f, -14f);
                _gradeBadgeTmp.fontSize = 20f;
                _gradeBadgeTmp.rectTransform.anchoredPosition = new Vector2(22f, -58f);
                _recordBadgeTmp.fontSize = 22f;
                _recordBadgeTmp.rectTransform.anchoredPosition = new Vector2(-22f, -14f);
                _muteBtnRect.anchoredPosition = new Vector2(-22f, -54f);
                _muteBtnRect.sizeDelta = new Vector2(168f, 40f);
                _muteLabelTmp.fontSize = 17f;

                for (int i = 0; i < 4; i++)
                {
                    _curPillRects[i].anchoredPosition = new Vector2(-381f + i * 254f, 14f);
                    _curPillRects[i].sizeDelta = new Vector2(242f, 64f);
                }

                _commandBarRect.anchoredPosition = new Vector2(0f, -228f);
                _commandBarRect.sizeDelta = new Vector2(1040f, 114f);
                _skinBtnRect.anchoredPosition = Vector2.zero;
                _skinBtnRect.sizeDelta = new Vector2(336f, 114f);
                _tierBtnRect.anchoredPosition = new Vector2(350f, 0f);
                _tierBtnRect.sizeDelta = new Vector2(266f, 114f);
                _startRunBtnRect.anchoredPosition = Vector2.zero;
                _startRunBtnRect.sizeDelta = new Vector2(410f, 114f);
                _startRunTmp.fontSize = 28f;

                _metaStripRect.anchoredPosition = new Vector2(0f, -356f);
                _metaStripRect.sizeDelta = new Vector2(1040f, 192f);
                for (int i = 0; i < 3; i++) _metaBoxRects[i].sizeDelta = new Vector2(338f, 192f);
                for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
                {
                    _metaCardSlotRects[i].anchoredPosition = new Vector2(18f + i * 78f, 16f);
                    _metaCardSlotRects[i].sizeDelta = new Vector2(70f, 126f);
                }
                for (int i = 0; i < 4; i++)
                {
                    _metaUwSlotRects[i].anchoredPosition = new Vector2(18f + i * 78f, 16f);
                    _metaUwSlotRects[i].sizeDelta = new Vector2(70f, 126f);
                }
                for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
                {
                    float y = -48f - i * 44f;
                    _metaLabNames[i].rectTransform.anchoredPosition = new Vector2(14f, y);
                    _metaLabTimers[i].rectTransform.anchoredPosition = new Vector2(-14f, y);
                }

                _viewportRect.offsetMin = new Vector2(20f, 160f);
                _viewportRect.offsetMax = new Vector2(-20f, -564f);
                _scrollbarRect.gameObject.SetActive(false);
                _scrollRect.enabled = false;

                for (int i = 0; i < MainTabCount; i++)
                {
                    _tabPanels[i].sizeDelta = new Vector2(0f, TabContentHeightsPortrait[i]);
                    _tabPanels[i].anchoredPosition = Vector2.zero;
                }

                // Portrait Cards Tab spacing
                _eqHdrTmp.rectTransform.sizeDelta = new Vector2(0f, 34f);
                for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
                {
                    _equipSlots[i].Root.anchoredPosition = new Vector2(i * 264f, -38f);
                    _equipSlots[i].Root.sizeDelta = new Vector2(248f, 124f);
                }
                _gachaBarRect.anchoredPosition = new Vector2(0f, -176f);
                _gachaBarRect.sizeDelta = new Vector2(0f, 80f);
                for (int i = 0; i < GameIdsAndScale.CARD_COUNT; i++)
                {
                    int col = i % 2;
                    int row = i / 2;
                    _cardWidgets[i].Root.anchoredPosition = new Vector2(col * 530f, -272f - row * 342f);
                    _cardWidgets[i].Root.sizeDelta = new Vector2(510f, 326f);
                    _cardWidgets[i].Desc.rectTransform.offsetMin = new Vector2(20f, -226f);
                    _cardWidgets[i].EquipBg.rectTransform.anchoredPosition = new Vector2(0f, 16f);
                    _cardWidgets[i].EquipBg.rectTransform.sizeDelta = new Vector2(474f, 70f);
                }

                _bottomTabBarRect.anchoredPosition = new Vector2(0f, 16f);
                _bottomTabBarRect.sizeDelta = new Vector2(1040f, 130f);
                for (int i = 0; i < MainTabCount; i++)
                {
                    _mainTabBgs[i].rectTransform.anchoredPosition = new Vector2(i * 210f, 0f);
                    _mainTabBgs[i].rectTransform.sizeDelta = new Vector2(200f, 128f);
                    _mainTabIconRects[i].anchorMin = new Vector2(0.5f, 1f);
                    _mainTabIconRects[i].anchorMax = new Vector2(0.5f, 1f);
                    _mainTabIconRects[i].pivot = new Vector2(0.5f, 1f);
                    _mainTabIconRects[i].anchoredPosition = new Vector2(0f, -12f);
                    _mainTabIconRects[i].sizeDelta = new Vector2(52f, 52f);
                    _mainTabLabels[i].text = MainTabTitlesPortrait[i];
                    _mainTabLabels[i].fontSize = 19f;
                    _mainTabLabels[i].alignment = TextAlignmentOptions.Bottom;
                    _mainTabLabels[i].rectTransform.offsetMin = new Vector2(4f, 22f);
                    _mainTabLabels[i].rectTransform.offsetMax = new Vector2(-4f, -62f);
                }

                _offlineWinRect.sizeDelta = new Vector2(860f, 560f);
            }
            else
            {
                // --- LANDSCAPE 1920x1080 PC WEBGL CENTERED ARMORED WINDOW (1180x1010) ---
                _bgOverlay.color = new Color(0.02f, 0.04f, 0.07f, 0.36f);
                _windowBgImg.enabled = true;
                _windowRect.anchorMin = new Vector2(0.5f, 0.5f);
                _windowRect.anchorMax = new Vector2(0.5f, 0.5f);
                _windowRect.pivot = new Vector2(0.5f, 0.5f);
                _windowRect.anchoredPosition = Vector2.zero;
                _windowRect.sizeDelta = new Vector2(1180f, 1010f);

                _topBarRect.anchoredPosition = new Vector2(0f, -14f);
                _topBarRect.sizeDelta = new Vector2(1120f, 136f);
                _topTitleTmp.fontSize = 25f;
                _topTitleTmp.rectTransform.anchoredPosition = new Vector2(20f, -10f);
                _gradeBadgeTmp.fontSize = 18f;
                _gradeBadgeTmp.rectTransform.anchoredPosition = new Vector2(20f, -44f);
                _recordBadgeTmp.fontSize = 20f;
                _recordBadgeTmp.rectTransform.anchoredPosition = new Vector2(-20f, -10f);
                _muteBtnRect.anchoredPosition = new Vector2(-20f, -44f);
                _muteBtnRect.sizeDelta = new Vector2(182f, 36f);
                _muteLabelTmp.fontSize = 16f;

                for (int i = 0; i < 4; i++)
                {
                    _curPillRects[i].anchoredPosition = new Vector2(-405f + i * 270f, 10f);
                    _curPillRects[i].sizeDelta = new Vector2(256f, 48f);
                }

                _commandBarRect.anchoredPosition = new Vector2(0f, -158f);
                _commandBarRect.sizeDelta = new Vector2(1120f, 80f);
                _skinBtnRect.anchoredPosition = Vector2.zero;
                _skinBtnRect.sizeDelta = new Vector2(360f, 80f);
                _tierBtnRect.anchoredPosition = new Vector2(372f, 0f);
                _tierBtnRect.sizeDelta = new Vector2(304f, 80f);
                _startRunBtnRect.anchoredPosition = Vector2.zero;
                _startRunBtnRect.sizeDelta = new Vector2(432f, 80f);
                _startRunTmp.fontSize = 23f;

                _metaStripRect.anchoredPosition = new Vector2(0f, -246f);
                _metaStripRect.sizeDelta = new Vector2(1120f, 146f);
                for (int i = 0; i < 3; i++) _metaBoxRects[i].sizeDelta = new Vector2(364f, 146f);
                for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
                {
                    _metaCardSlotRects[i].anchoredPosition = new Vector2(20f + i * 84f, 10f);
                    _metaCardSlotRects[i].sizeDelta = new Vector2(74f, 96f);
                }
                for (int i = 0; i < 4; i++)
                {
                    _metaUwSlotRects[i].anchoredPosition = new Vector2(20f + i * 84f, 10f);
                    _metaUwSlotRects[i].sizeDelta = new Vector2(74f, 96f);
                }
                for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
                {
                    float y = -40f - i * 33f;
                    _metaLabNames[i].rectTransform.anchoredPosition = new Vector2(14f, y);
                    _metaLabTimers[i].rectTransform.anchoredPosition = new Vector2(-14f, y);
                }

                // Exact 1040px inner viewport width (1180 - 58 - 82 = 1040) x 498px height + sleek Scrollbar
                _viewportRect.offsetMin = new Vector2(58f, 110f);
                _viewportRect.offsetMax = new Vector2(-82f, -402f);
                _scrollbarRect.offsetMin = new Vector2(-42f, 110f);
                _scrollbarRect.offsetMax = new Vector2(-28f, -402f);
                _scrollbarRect.gameObject.SetActive(true);
                _scrollRect.enabled = true;

                for (int i = 0; i < MainTabCount; i++)
                {
                    _tabPanels[i].sizeDelta = new Vector2(0f, TabContentHeightsLandscape[i]);
                }

                // Compact Cards Tab top so Row 0 of Signature Cards fits 100% unclipped inside 498px viewport
                _eqHdrTmp.rectTransform.sizeDelta = new Vector2(0f, 26f);
                for (int i = 0; i < BalanceTables.MAX_CARD_SLOTS; i++)
                {
                    _equipSlots[i].Root.anchoredPosition = new Vector2(i * 264f, -30f);
                    _equipSlots[i].Root.sizeDelta = new Vector2(248f, 96f);
                }
                _gachaBarRect.anchoredPosition = new Vector2(0f, -134f);
                _gachaBarRect.sizeDelta = new Vector2(0f, 64f);
                for (int i = 0; i < GameIdsAndScale.CARD_COUNT; i++)
                {
                    int col = i % 2;
                    int row = i / 2;
                    _cardWidgets[i].Root.anchoredPosition = new Vector2(col * 530f, -206f - row * 298f);
                    _cardWidgets[i].Root.sizeDelta = new Vector2(510f, 286f);
                    _cardWidgets[i].Desc.rectTransform.offsetMin = new Vector2(20f, -196f);
                    _cardWidgets[i].EquipBg.rectTransform.anchoredPosition = new Vector2(0f, 12f);
                    _cardWidgets[i].EquipBg.rectTransform.sizeDelta = new Vector2(474f, 62f);
                }

                _bottomTabBarRect.anchoredPosition = new Vector2(0f, 14f);
                _bottomTabBarRect.sizeDelta = new Vector2(1120f, 84f);
                for (int i = 0; i < MainTabCount; i++)
                {
                    _mainTabBgs[i].rectTransform.anchoredPosition = new Vector2(i * 226f, 0f);
                    _mainTabBgs[i].rectTransform.sizeDelta = new Vector2(216f, 82f);
                    _mainTabIconRects[i].anchorMin = new Vector2(0f, 0.5f);
                    _mainTabIconRects[i].anchorMax = new Vector2(0f, 0.5f);
                    _mainTabIconRects[i].pivot = new Vector2(0f, 0.5f);
                    _mainTabIconRects[i].anchoredPosition = new Vector2(12f, 0f);
                    _mainTabIconRects[i].sizeDelta = new Vector2(40f, 40f);
                    _mainTabLabels[i].text = MainTabTitlesLandscape[i];
                    _mainTabLabels[i].fontSize = 17.5f;
                    _mainTabLabels[i].alignment = TextAlignmentOptions.Left;
                    _mainTabLabels[i].rectTransform.offsetMin = new Vector2(58f, 6f);
                    _mainTabLabels[i].rectTransform.offsetMax = new Vector2(-8f, -6f);
                }

                _offlineWinRect.sizeDelta = new Vector2(820f, 520f);
            }

            BindActiveTabToScroll();
            RefreshMuteVisual(force: true);
        }

        private void BindActiveTabToScroll()
        {
            if (_scrollRect == null || _tabPanels[_activeTab] == null) return;
            _scrollRect.content = _tabPanels[_activeTab];
            _tabPanels[_activeTab].anchoredPosition = Vector2.zero;
            if (_scrollRect.enabled)
            {
                _scrollRect.velocity = Vector2.zero;
                _scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        public void RefreshMuteVisual(bool force)
        {
            if (_muteBtnBg == null || _muteLabelTmp == null) return;
            int state = (UiStyle.IsMuted ? 1 : 0) | (_isLandscape ? 2 : 0);
            if (!force && state == _cachedMuteState) return;
            _cachedMuteState = state;

            bool muted = UiStyle.IsMuted;
            _muteBtnBg.material = muted ? _style.MatTabInactive : _style.MatPill;
            _muteLabelTmp.color = muted ? UiStyle.TextMuted : UiStyle.TextCyan;
            if (_isLandscape)
            {
                _muteLabelTmp.text = muted ? "[M] ЗВУК ВЫКЛ" : "[M] ЗВУК ВКЛ";
            }
            else
            {
                _muteLabelTmp.text = muted ? "ЗВУК ВЫКЛ" : "ЗВУК ВКЛ";
            }
        }

        public void SelectTabFromHotkey(int index)
        {
            if (index >= 0 && index < MainTabCount)
            {
                SelectMainTabByUser(index);
            }
        }

        public void TriggerCycleWorkshopMultiplier()
        {
            if (_activeTab != TabWorkshop) return;
            UiStyle.PlaySfx(SfxId.UiTap);
            _wsBuyMultIdx = (_wsBuyMultIdx + 1) % BuyMultipliers.Length;
            _wsMultBtnTmp.text = BuyMultLabels[_wsBuyMultIdx];
            InvalidateWorkshopCache();
        }

        public void TriggerStartRun()
        {
            OnClickStartRun();
        }

        private static int FindFirstFreeLabSlot(GameSession session)
        {
            if (!session.LabsUnlocked) return -1;
            for (int i = 0; i < session.UnlockedLabSlots; i++)
            {
                if (!session.LabSlots[i].Active) return i;
            }
            return -1;
        }
    }
}
