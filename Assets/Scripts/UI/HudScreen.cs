using System;
using System.Text;
using Crusher.Config;
using Crusher.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Crusher.UI
{
    /// <summary>
    /// Full 1080x2400 industrial portrait Combat HUD:
    /// Top telemetry & currencies, diegetic rotor HP/regen ring, <=6 aggregated damage popups,
    /// interactive Gem Drone (+5), toast/FTUE banners, 42% bottom 3-tab upgrade deck with FTUE arrow,
    /// Ultimate Weapon 3-choice modal, and Run Summary modal.
    /// </summary>
    public sealed class HudScreen
    {
        private const int MaxDeckCards = 10; // Defense has 10 stats max (2 cols x 5 rows)
        private static readonly int[] BuyMultSteps = { 1, 5, 10, -1 }; // -1 = MAX
        private static readonly string[] BuyMultLabels = { "КРАТН ×1", "КРАТН ×5", "КРАТН ×10", "КРАТН MAX" };
        private static readonly float[] SpeedSteps = { 1.0f, 1.5f, 2.0f, 2.7f, 3.5f, 4.3f, 5.0f };

        private struct StatCardWidget
        {
            public RectTransform Root;
            public Image CardBg;
            public Button CardWholeBtn;
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI LevelBadge;
            public TextMeshProUGUI ValueTransition;
            public Button BuyBtn;
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
            public Image Icon;
            public TextMeshProUGUI Title;
            public TextMeshProUGUI Desc;
            public Button PickBtn;
            public Image PickBg;
            public TextMeshProUGUI PickLabel;
            public UwId BoundUw;
        }

        private RectTransform _root;
        private RectTransform _combatLayer;
        private Canvas _canvas;
        private UiStyle _style;
        private TweenRunner _tweens;
        private Camera _worldCam;
        private Action<int> _onOpenHangar;

        // Top HUD widgets & caches
        private TextMeshProUGUI _tierBadgeTmp;
        private TextMeshProUGUI _waveTitleTmp;
        private TextMeshProUGUI _waveSubTmp;
        private Button _speedBtn;
        private RectTransform _speedBtnRect;
        private TextMeshProUGUI _speedLabelTmp;
        private Button _retreatBtn;
        private TextMeshProUGUI _cashTmp;
        private TextMeshProUGUI _coinsTmp;
        private TextMeshProUGUI _gemsTmp;
        private TextMeshProUGUI _stonesTmp;
        private TextMeshProUGUI _cellsTmp;

        private int _cachedTier = -1;
        private int _cachedWaveKey = -1;
        private int _cachedWaveSubSec = -999;
        private bool _cachedInCooldown;
        private int _cachedSpeedTenths = -1;
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

        // Diegetic Core HP Ring
        private RectTransform _hpRingRoot;
        private TextMeshProUGUI _hpNumericTmp;
        private TextMeshProUGUI _hpArmorSubTmp;
        private float _displayedTrailRatio = 1.0f;
        private long _cachedHpKey = -1;
        private long _cachedArmorKey = -1;

        // Damage numbers & Gem Drone
        private readonly DamageNumberAggregator _dmgNumbers = new DamageNumberAggregator();
        private RectTransform _gemDroneBtnRect;
        private Button _gemDroneBtn;

        // Bottom Combat Deck (42% height = 1004 px)
        private RectTransform _deckRect;
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

        // Bottom Tabs & Multiplier buttons
        private Image _tabAttackBg;
        private Image _tabDefenseBg;
        private Image _tabUtilityBg;
        private RectTransform _tabDefenseRect;
        private Button _multBtn;
        private RectTransform _multBtnRect;
        private TextMeshProUGUI _multLabelTmp;

        // FTUE Arrow in HUD
        private RectTransform _ftueArrowRect;
        private TextMeshProUGUI _ftueArrowTmp;
        private bool _run1DefensePurchased;

        // UW Choice Modal
        private RectTransform _uwModalRoot;
        private CanvasGroup _uwModalGroup;
        private TextMeshProUGUI _uwModalSubTmp;
        private readonly UwChoiceRow[] _uwRows = new UwChoiceRow[3];

        // Run Summary Modal (Game Over)
        private RectTransform _summaryModalRoot;
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

            _root = UiFactory.CreateStretchRect("HudScreen", safeParent);
            _combatLayer = UiFactory.CreateStretchRect("CombatLayer", _root);

            BuildArenaOverlay();
            BuildTopTelemetryBar();
            BuildBottomDeck();
            BuildUwChoiceModal(safeParent);
            BuildRunSummaryModal();

            ApplyTabVisuals();
        }

        public void BringSharedModalsToFront()
        {
            if (_uwModalRoot != null)
            {
                _uwModalRoot.SetAsLastSibling();
            }
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
        }

        public void DismissModals()
        {
            _summaryOpen = false;
            if (_summaryModalRoot != null) _summaryModalRoot.gameObject.SetActive(false);
            if (_uwModalRoot != null) _uwModalRoot.gameObject.SetActive(false);
        }

        public void OpenSummaryForLastRun(GameSession session)
        {
            if (session == null || _summaryModalRoot == null) return;
            _boundSession = session;
            _summaryOpen = true;
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

            if (_summaryOpen)
            {
                AnimateFtueBob(_sumFtueArrowRect, -128f);
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

            UiFormat.SetCompactDouble(_cashTmp, "$", session.RunCash, ref _cachedCash);
            UiFormat.SetCompactDouble(_coinsTmp, "", session.Coins, ref _cachedCoins);
            UiFormat.SetInt(_gemsTmp, "", session.Gems, ref _cachedGems);
            UiFormat.SetInt(_stonesTmp, "", session.Stones, ref _cachedStones);
            UiFormat.SetInt(_cellsTmp, "", session.EliteCells, ref _cachedCells);

            // 2. Synergy / UW Banner
            TickSynergyBanner(session, sim);

            // 3. Toasts & Run 1 W3 FTUE Prototype Thorns Banner
            TickToasts(session, sim, unscaledDt);

            // 4. Diegetic Core HP + Regen Ring
            TickCoreHpRing(sim, unscaledDt);

            // 5. Pooled <=6 Damage Numbers
            _dmgNumbers.Tick(sim, unscaledDt);

            // 6. Floating Gem Drone (+5) Button
            TickGemDroneButton(sim);

            // 7. Bottom Combat Upgrade Deck + FTUE Arrow
            TickDeck(session, sim);
        }

        private void BuildArenaOverlay()
        {
            // Diegetic Rotor HP Ring (projected onto world origin)
            _hpRingRoot = UiFactory.CreateRect(
                "CoreHpRingRoot", _combatLayer,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 300f), new Vector2(264f, 264f));

            UiFactory.CreateSdfImage(
                "RadialRing", _hpRingRoot, _style.MatCoreHpRing, new Color(0.1f, 0.8f, 0.5f, 0.45f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, raycast: false);

            Image hpPlate = UiFactory.CreateSdfImage(
                "HpReadoutPlate", _hpRingRoot, _style.MatPill, new Color(0.06f, 0.09f, 0.14f, 0.90f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -152f), new Vector2(316f, 64f), raycast: false);

            _hpNumericTmp = UiFactory.CreateTmp(
                "HpNumeric", hpPlate.transform, _style.Font, "22 / 22  +0.2/с",
                21f, UiStyle.TextPrimary, TextAlignmentOptions.Center,
                new Vector2(0f, 0.42f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-10f, 0f));
            _hpNumericTmp.fontStyle = FontStyles.Bold;

            _hpArmorSubTmp = UiFactory.CreateTmp(
                "HpArmorSub", hpPlate.transform, _style.Font, "РОТОР ШРЕДЕРА",
                16f, UiStyle.TextCyan, TextAlignmentOptions.Center,
                new Vector2(0f, 0f), new Vector2(1f, 0.48f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-10f, 0f));

            // <= 6 Pooled Damage Numbers
            _dmgNumbers.Build(_combatLayer, _canvas, _style, _worldCam);

            // Interactive Floating Gem Drone (+5 Gems) Button
            _gemDroneBtn = UiFactory.CreateSdfButton(
                "GemDroneBtn", _combatLayer, _style, _style.MatBuyCyan, new Color(0.10f, 0.55f, 0.78f, 0.96f),
                "", 21f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(280f, 480f), new Vector2(226f, 78f),
                OnClickGemDrone, out _, out TextMeshProUGUI droneLbl);

            _gemDroneBtnRect = _gemDroneBtn.GetComponent<RectTransform>();
            UiFactory.CreateIcon(
                "DroneIcon", _gemDroneBtnRect, _style.GetIcon(UiIcons.BotGemDrone),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(54f, 54f));

            droneLbl.rectTransform.offsetMin = new Vector2(68f, 4f);
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
                new Vector2(0f, -14f), new Vector2(1044f, 198f), raycast: false);

            Transform top = topPanel.transform;

            // Row 1: Tier Pill + Wave Progress Bar + Speed Toggle + Hangar/Retreat Button
            Image tierPill = UiFactory.CreateSdfImage(
                "TierBadge", top, _style.MatPill, new Color(0.12f, 0.18f, 0.26f, 0.95f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -16f), new Vector2(138f, 78f));

            _tierBadgeTmp = UiFactory.CreateTmp(
                "TierText", tierPill.transform, _style.Font, "ТИР 1",
                25f, UiStyle.TextCyan, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _tierBadgeTmp.fontStyle = FontStyles.Bold;

            Image waveBox = UiFactory.CreateSdfImage(
                "WaveProgressBox", top, _style.MatWaveBar, new Color(0.11f, 0.16f, 0.24f, 0.96f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(168f, -16f), new Vector2(468f, 78f));

            _waveTitleTmp = UiFactory.CreateTmp(
                "WaveTitle", waveBox.transform, _style.Font, "ВОЛНА 1",
                26f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 0.35f), new Vector2(0.68f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(10f, 2f), new Vector2(-16f, 0f));
            _waveTitleTmp.fontStyle = FontStyles.Bold;

            _waveSubTmp = UiFactory.CreateTmp(
                "WaveSub", waveBox.transform, _style.Font, "БОЙ 26с",
                20f, UiStyle.TextSecondary, TextAlignmentOptions.Right,
                new Vector2(0.60f, 0.35f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-10f, 2f), new Vector2(-16f, 0f));

            _speedBtn = UiFactory.CreateSdfButton(
                "SpeedBtn", top, _style, _style.MatBuyCyan, new Color(0.12f, 0.45f, 0.65f, 0.96f),
                "×1.0", 26f, UiStyle.TextPrimary,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(650f, -16f), new Vector2(168f, 78f),
                OnClickSpeedToggle, out _, out _speedLabelTmp);
            _speedLabelTmp.fontStyle = FontStyles.Bold;
            _speedBtnRect = _speedBtn.GetComponent<RectTransform>();

            _retreatBtn = UiFactory.CreateSdfButton(
                "HangarRetreatBtn", top, _style, _style.MatDangerBtn, new Color(0.45f, 0.15f, 0.15f, 0.96f),
                "АНГАР", 23f, UiStyle.TextPrimary,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(830f, -16f), new Vector2(196f, 78f),
                OnClickRetreatToHangar, out _, out TextMeshProUGUI retLbl);
            retLbl.fontStyle = FontStyles.Bold;

            // Row 2: 5 Currency Pills ($ Cash, Coins, Gems, Stones, Cells)
            _cashTmp = BuildCurrencyPill(top, 18f, 224f, UiIcons.Cash, "$55", UiStyle.TextEmerald);
            _coinsTmp = BuildCurrencyPill(top, 252f, 212f, UiIcons.Coins, "0", UiStyle.TextAmber);
            _gemsTmp = BuildCurrencyPill(top, 474f, 182f, UiIcons.Gems, "0", UiStyle.TextCyan);
            _stonesTmp = BuildCurrencyPill(top, 666f, 176f, UiIcons.Stones, "0", new Color(0.45f, 0.98f, 0.72f, 1f));
            _cellsTmp = BuildCurrencyPill(top, 852f, 174f, UiIcons.Cells, "0", new Color(0.84f, 0.62f, 1.00f, 1f));

            // Active UW / Synergy Overdrive Strip below Top HUD
            Image synImg = UiFactory.CreateSdfImage(
                "SynergyBanner", _combatLayer, _style.MatSynergyBanner, new Color(0.28f, 0.18f, 0.04f, 0.95f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -220f), new Vector2(860f, 56f), raycast: false);
            _synergyRect = synImg.rectTransform;
            _synergyTmp = UiFactory.CreateTmp(
                "SynergyText", _synergyRect, _style.Font, "",
                21f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, -4f));
            _synergyTmp.fontStyle = FontStyles.Bold;
            _synergyRect.gameObject.SetActive(false);

            // Milestone / FTUE Toast Banner
            Image toastImg = UiFactory.CreateSdfImage(
                "ToastBanner", _combatLayer, _style.MatToastBanner, new Color(0.22f, 0.16f, 0.06f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -284f), new Vector2(940f, 70f), raycast: false);
            _toastRect = toastImg.rectTransform;
            _toastGroup = toastImg.gameObject.AddComponent<CanvasGroup>();
            _toastTmp = UiFactory.CreateTmp(
                "ToastText", _toastRect, _style.Font, "",
                22f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-26f, -6f));
            _toastTmp.fontStyle = FontStyles.Bold;
            _toastRect.gameObject.SetActive(false);
        }

        private TextMeshProUGUI BuildCurrencyPill(Transform parent, float xLeft, float width, string iconId, string initText, Color textCol)
        {
            Image pill = UiFactory.CreateSdfImage(
                "Cur_" + iconId, parent, _style.MatPill, new Color(0.11f, 0.14f, 0.20f, 0.95f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(xLeft, -108f), new Vector2(width, 72f));

            UiFactory.CreateIcon(
                "Icon", pill.transform, _style.GetIcon(iconId),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(46f, 46f));

            TextMeshProUGUI tmp = UiFactory.CreateTmp(
                "Val", pill.transform, _style.Font, initText,
                23f, textCol, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            tmp.rectTransform.offsetMin = new Vector2(62f, 4f);
            tmp.rectTransform.offsetMax = new Vector2(-10f, -4f);
            tmp.fontStyle = FontStyles.Bold;
            return tmp;
        }

        private void BuildBottomDeck()
        {
            // ~41.8% of 2400px = 1004px, leaving upper 58.2% clean for 3D Arena
            Image deckBg = UiFactory.CreateSdfImage(
                "BottomUpgradeDeck", _combatLayer, _style.MatDeckBg, new Color(0.08f, 0.10f, 0.14f, 0.98f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(1080f, 1004f), raycast: true);
            _deckRect = deckBg.rectTransform;

            // Header Strip (y_from_bottom = 918..992)
            Image hdr = UiFactory.CreateSdfImage(
                "DeckHeader", _deckRect, _style.MatHeaderStrip, new Color(0.13f, 0.18f, 0.25f, 0.96f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 918f), new Vector2(1040f, 72f));

            _deckTitleTmp = UiFactory.CreateTmp(
                "DeckTitle", hdr.transform, _style.Font, "ПАЛУБА МОДИФИКАЦИЙ • АТАКА",
                23f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(0.56f, 1f), new Vector2(0f, 0.5f),
                new Vector2(20f, 0f), new Vector2(-20f, 0f));
            _deckTitleTmp.fontStyle = FontStyles.Bold;

            _coreGradeTmp = UiFactory.CreateTmp(
                "CoreGradeLabel", hdr.transform, _style.Font, "G0 РЖАВЫЙ РОТОР",
                19f, UiStyle.TextAmber, TextAlignmentOptions.Right,
                new Vector2(0.54f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-20f, 0f), new Vector2(-20f, 0f));
            _coreGradeTmp.fontStyle = FontStyles.Bold;

            // Quick UW Unlock Button overlaid on right side of deck header when Stones ready
            _uwQuickBtn = UiFactory.CreateSdfButton(
                "UwQuickBtn", hdr.transform, _style, _style.MatBuyGreen, new Color(0.14f, 0.62f, 0.36f, 0.98f),
                "⚡ ВЫБРАТЬ УЛЬТУ!", 20f, UiStyle.TextPrimary,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-12f, 0f), new Vector2(280f, 58f),
                OnClickUwQuick, out _, out _uwQuickLabel);
            _uwQuickLabel.fontStyle = FontStyles.Bold;
            _uwQuickRect = _uwQuickBtn.GetComponent<RectTransform>();
            _uwQuickRect.gameObject.SetActive(false);

            // 10 Pre-allocated Stat Cards (2 columns x 5 rows inside y_from_bottom = 192..908)
            // Card size: 512 x 134, col centers at x = -264 and +264, rows from top (y=838) down by 142px
            for (int i = 0; i < MaxDeckCards; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float cx = col == 0 ? -264f : 264f;
                float cy = 838f - row * 142f; // rows at y = 838, 696, 554, 412, 270 (covers smoke taps ny=0.78..0.88!)

                _cards[i] = BuildStatCardSlot(i, cx, cy);
            }

            // Bottom Tab & Multiplier Bar (y_from_bottom = 30..174, center y = 102 -> ny = 0.957!)
            // Coordinates match tools/smoke_tt36p.py: Attack (0.14, 0.95), Defense (0.38, 0.95)!
            BuildTabButton("TabAttack", -392f, 250f, UiIcons.TabAttack, "АТАКА", () => SelectTab(StatTab.Attack), out _tabAttackBg, out _);
            BuildTabButton("TabDefense", -132f, 250f, UiIcons.TabDefense, "ЗАЩИТА", () => SelectTab(StatTab.Defense), out _tabDefenseBg, out _tabDefenseRect);
            BuildTabButton("TabUtility", 128f, 250f, UiIcons.TabUtility, "УТИЛИТИ", () => SelectTab(StatTab.Utility), out _tabUtilityBg, out _);

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
                new Vector2(-132f, 178f), new Vector2(96f, 84f), raycast: false);
            _ftueArrowRect = arrowImg.rectTransform;

            Image callout = UiFactory.CreateSdfImage(
                "CalloutPlate", _ftueArrowRect, _style.MatToastBanner, new Color(0.28f, 0.18f, 0.04f, 0.98f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(260f, 46f), raycast: false);

            _ftueArrowTmp = UiFactory.CreateTmp(
                "CalloutText", callout.transform, _style.Font, "КАЧАЙТЕ ЗУБЬЯ!",
                19f, UiStyle.TextAmber, TextAlignmentOptions.Center,
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

            w.Icon = UiFactory.CreateIcon(
                "Icon", w.Root, null,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(14f, -14f), new Vector2(58f, 58f));

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
            w.BuyLabel.fontStyle = FontStyles.Bold;

            return w;
        }

        private void BuildTabButton(
            string name, float centerX, float width, string iconId, string title,
            UnityEngine.Events.UnityAction onClick, out Image bgOut, out RectTransform rectOut)
        {
            Button btn = UiFactory.CreateSdfButton(
                name, _deckRect, _style, _style.MatTabInactive, new Color(0.12f, 0.16f, 0.22f, 0.96f),
                title, 22f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(centerX, 32f), new Vector2(width, 140f),
                onClick, out bgOut, out TextMeshProUGUI lbl);

            rectOut = btn.GetComponent<RectTransform>();
            UiFactory.CreateIcon(
                "TabIcon", rectOut, _style.GetIcon(iconId),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(56f, 56f));

            lbl.alignment = TextAlignmentOptions.Bottom;
            lbl.rectTransform.offsetMin = new Vector2(8f, 16f);
            lbl.rectTransform.offsetMax = new Vector2(-8f, -68f);
            lbl.fontStyle = FontStyles.Bold;
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

            TextMeshProUGUI hdr = UiFactory.CreateTmp(
                "Header", win.transform, _style.Font, "⚡ АБСОЛЮТНОЕ ОРУЖИЕ ЯДРА",
                32f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(-40f, 52f));
            hdr.fontStyle = FontStyles.Bold;

            _uwModalSubTmp = UiFactory.CreateTmp(
                "SubHeader", win.transform, _style.Font, "Выберите модуль абсолютного оружия:",
                22f, UiStyle.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -86f), new Vector2(-40f, 40f));

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
                    new Vector2(24f, 0f), new Vector2(118f, 118f));

                row.Title = UiFactory.CreateTmp(
                    "Title", row.Root, _style.Font, "Ульта",
                    28f, UiStyle.TextAmber, TextAlignmentOptions.Left,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, Vector2.zero);
                row.Title.rectTransform.offsetMin = new Vector2(162f, -72f);
                row.Title.rectTransform.offsetMax = new Vector2(-24f, -20f);
                row.Title.fontStyle = FontStyles.Bold;

                row.Desc = UiFactory.CreateTmp(
                    "Desc", row.Root, _style.Font, "",
                    21f, UiStyle.TextPrimary, TextAlignmentOptions.Left,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                    Vector2.zero, Vector2.zero, wrap: true);
                row.Desc.rectTransform.offsetMin = new Vector2(162f, 84f);
                row.Desc.rectTransform.offsetMax = new Vector2(-28f, -74f);

                row.PickBtn = UiFactory.CreateSdfButton(
                    "PickBtn", row.Root, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                    "ОТКРЫТЬ", 24f, UiStyle.TextButtonDark,
                    new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-22f, 18f), new Vector2(280f, 66f),
                    () => OnClickPickUw(slot), out row.PickBg, out row.PickLabel);
                row.PickLabel.fontStyle = FontStyles.Bold;

                _uwRows[i] = row;
            }

            UiFactory.CreateSdfButton(
                "CloseUwModalBtn", win.transform, _style, _style.MatTabInactive, new Color(0.18f, 0.22f, 0.28f, 0.96f),
                "ЗАКРЫТЬ", 23f, UiStyle.TextSecondary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(320f, 74f),
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

            _sumTitleTmp = UiFactory.CreateTmp(
                "SummaryTitle", win.transform, _style.Font, "ИТОГИ ЗАБЕГА",
                38f, UiStyle.TextCrimson, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -34f), new Vector2(-40f, 64f));
            _sumTitleTmp.fontStyle = FontStyles.Bold;

            TextMeshProUGUI sub = UiFactory.CreateTmp(
                "SummarySub", win.transform, _style.Font, "ОТЧЁТ БОЕВОЙ ТЕЛЕМЕТРИИ",
                21f, UiStyle.TextSecondary, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -98f), new Vector2(-40f, 36f));

            Image statsBox = UiFactory.CreateSdfImage(
                "StatsBox", win.transform, _style.MatStatCard, new Color(0.08f, 0.11f, 0.16f, 0.96f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -156f), new Vector2(820f, 420f));

            _sumStatsTmp = UiFactory.CreateTmp(
                "SummaryStats", statsBox.transform, _style.Font, "",
                28f, UiStyle.TextPrimary, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-48f, -36f), wrap: true);

            // Button 1: В МАСТЕРСКУЮ (with FTUE pulsing arrow after Run 1 death)
            Button wsBtn = UiFactory.CreateSdfButton(
                "ToWorkshopBtn", win.transform, _style, _style.MatBuyAmber, UiStyle.TextAmber,
                "В МАСТЕРСКУЮ", 26f, UiStyle.TextButtonDark,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-210f, 44f), new Vector2(390f, 112f),
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
                new Vector2(-210f, 166f), new Vector2(96f, 84f), raycast: false);
            _sumFtueArrowRect = arrow.rectTransform;

            TextMeshProUGUI arrowTip = UiFactory.CreateTmp(
                "Tip", _sumFtueArrowRect, _style.Font, "ОТКРОЙТЕ ЗУБЬЯ!",
                20f, UiStyle.TextAmber, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 2f), new Vector2(280f, 36f));
            arrowTip.fontStyle = FontStyles.Bold;

            // Button 2: НОВЫЙ ЗАБЕГ
            Button retryBtn = UiFactory.CreateSdfButton(
                "NewRunBtn", win.transform, _style, _style.MatBuyGreen, UiStyle.TextEmerald,
                "▶ НОВЫЙ ЗАБЕГ", 26f, UiStyle.TextPrimary,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(210f, 44f), new Vector2(390f, 112f),
                () =>
                {
                    if (_boundSession == null) return;
                    UiStyle.PlaySfx(SfxId.UiUnlock);
                    _summaryOpen = false;
                    _summaryModalRoot.gameObject.SetActive(false);
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
                       .Append(sim.GoldenTower.Multiplier.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                       .Append(" + ЧЁРНАЯ ДЫРА (").Append(Mathf.Max(1, remGt)).Append("с)");
                }
                else if (gtAct)
                {
                    _sb.Append("⚡ ЗОЛОТАЯ ПЛАВИЛЬНЯ АКТИВНА ×")
                       .Append(sim.GoldenTower.Multiplier.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
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
            _tweens.SlideAnchoredY(_toastRect, -248f, -284f, 0.20f);
        }

        private void TickCoreHpRing(CombatSim sim, float unscaledDt)
        {
            // Project 3D Rotor Core (0, 0.25, 0) onto HUD layer
            Vector2 coreUiPos = _dmgNumbers.ProjectSimToLayer(0f, 0f, 0.25f);
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
                       .Append((sim.Stats.DefPercent * 100.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
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

            Vector2 dronePos = _dmgNumbers.ProjectSimToLayer(sim.GemDrone.PosX, sim.GemDrone.PosY, 1.6f);
            float bob = Mathf.Sin(Time.unscaledTime * 4.2f) * 7f;
            dronePos.x = Mathf.Clamp(dronePos.x, -400f, 400f);
            dronePos.y = Mathf.Clamp(dronePos.y + 44f + bob, 80f, 760f);
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

                _deckTitleTmp.text = _activeTab == StatTab.Attack ? "ПАЛУБА ПРОКАЧКИ • АТАКА"
                    : (_activeTab == StatTab.Defense ? "ПАЛУБА ПРОКАЧКИ • ЗАЩИТА" : "ПАЛУБА ПРОКАЧКИ • УТИЛИТИ");

                _coreGradeTmp.text = gradeInt switch
                {
                    0 => "G0 РЖАВЫЙ РОТОР",
                    1 => "G1 КАРБИДНЫЕ ЗУБЬЯ",
                    2 => "G2 ТИТАНОВЫЕ ПИЛЫ",
                    _ => "G3 АЛМАЗНЫЙ РЕАКТОР"
                };
            }

            // Populate unlocked stats for _activeTab
            int slot = 0;
            int thornsSlotIdx = -1;
            int healthSlotIdx = -1;

            for (int i = 0; i < GameIdsAndScale.STAT_COUNT && slot < MaxDeckCards; i++)
            {
                ref readonly StatDef def = ref BalanceTables.Stats[i];
                if (def.Tab != _activeTab) continue;

                StatId sid = def.Id;
                bool unlocked = session.IsStatUnlocked(sid);

                // In Run 1 on Defense tab, show Thorns preview card if prototype teeth triggered so novice sees where Thorns lives
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
            for (int i = slot; i < MaxDeckCards; i++)
            {
                if (_cards[i].Root.gameObject.activeSelf)
                {
                    _cards[i].Root.gameObject.SetActive(false);
                }
            }

            // FTUE Pulsing Arrow Logic:
            // Starts at Run 1 Wave 3+ (or while Thorns is unlocked at level 0) until player upgrades Defense / Thorns!
            bool thornsNeedsFirstBuy = session.IsStatUnlocked(StatId.Thorns) && session.GetTotalStatLevel(StatId.Thorns) == 0;
            bool run1W3Guide = session.TotalRunsStarted == 1 && (sim.CurrentWave >= 3 || sim.PrototypeThornsTriggered) && !_run1DefensePurchased;
            bool showFtueArrow = thornsNeedsFirstBuy || run1W3Guide;

            if (!showFtueArrow)
            {
                if (_ftueArrowRect.gameObject.activeSelf) _ftueArrowRect.gameObject.SetActive(false);
                return;
            }

            if (!_ftueArrowRect.gameObject.activeSelf) _ftueArrowRect.gameObject.SetActive(true);
            float bobY = Mathf.Sin(Time.unscaledTime * 7.5f) * 7f;

            if (_activeTab != StatTab.Defense)
            {
                _ftueArrowTmp.text = "ЗАЩИТА → ЗУБЬЯ!";
                _ftueArrowRect.anchoredPosition = new Vector2(-132f, 174f + bobY);
            }
            else
            {
                int targetSlot = thornsSlotIdx >= 0 ? thornsSlotIdx : healthSlotIdx;
                if (targetSlot >= 0 && targetSlot < _visibleCardCount)
                {
                    Vector2 cardPos = _cards[targetSlot].Root.anchoredPosition;
                    _ftueArrowTmp.text = thornsSlotIdx >= 0 ? "КАЧАЙТЕ ЗУБЬЯ!" : "УКРЕПИТЕ ЯДРО!";
                    _ftueArrowRect.anchoredPosition = new Vector2(cardPos.x + 120f, cardPos.y + 42f + bobY);
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
                    w.ValueTransition.text = "8% шипы • Откр. в Ангаре (W10)";
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

        private void ComputeRunPurchasePlan(GameSession session, StatId id, int curTotalLv, int maxLv, out int count, out double totalCost, out bool affordable)
        {
            if (curTotalLv >= maxLv)
            {
                count = 0;
                totalCost = 0.0;
                affordable = false;
                return;
            }

            int mode = BuyMultSteps[_buyMultIdx];
            double cash = session.RunCash;

            if (mode == -1) // MAX affordable (at least 1 shown for price reference)
            {
                double sum = 0.0;
                int n = 0;
                for (int lv = curTotalLv; lv < maxLv && n < 100; lv++)
                {
                    double c = BalanceTables.GetCashCost(id, lv);
                    if (sum + c > cash)
                    {
                        if (n == 0)
                        {
                            count = 1;
                            totalCost = c;
                            affordable = false;
                            return;
                        }
                        break;
                    }
                    sum += c;
                    n++;
                }
                count = Math.Max(1, n);
                totalCost = sum;
                affordable = n > 0;
                return;
            }

            int want = Math.Min(mode, maxLv - curTotalLv);
            double acc = 0.0;
            for (int k = 0; k < want; k++)
            {
                acc += BalanceTables.GetCashCost(id, curTotalLv + k);
            }
            count = want;
            totalCost = acc;
            affordable = cash >= acc;
        }

        private void SelectTab(StatTab tab)
        {
            if (_activeTab == tab) return;
            _activeTab = tab;
            UiStyle.PlaySfx(SfxId.UiTap);
            ApplyTabVisuals();
        }

        private void ApplyTabVisuals()
        {
            if (_tabAttackBg != null) _tabAttackBg.material = _activeTab == StatTab.Attack ? _style.MatTabActive : _style.MatTabInactive;
            if (_tabDefenseBg != null) _tabDefenseBg.material = _activeTab == StatTab.Defense ? _style.MatTabActive : _style.MatTabInactive;
            if (_tabUtilityBg != null) _tabUtilityBg.material = _activeTab == StatTab.Utility ? _style.MatTabActive : _style.MatTabInactive;
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
                ShowToast("★ ДРОН ПЕРЕХВАЧЕН: +5 ГЕМОВ!", 2.4f);
            }
        }

        private void OnClickUwQuick()
        {
            if (_boundSession == null) return;
            UiStyle.PlaySfx(SfxId.UiTap);
            OpenUwModal(_boundSession);
        }

        private void OnClickPickUw(int rowIdx)
        {
            if (_boundSession == null || rowIdx < 0 || rowIdx >= 3) return;
            UwId chosen = _uwRows[rowIdx].BoundUw;
            if (_boundSession.TryUnlockUw(chosen))
            {
                UiStyle.PlaySfx(SfxId.UiUnlock);
                _uwModalRoot.gameObject.SetActive(false);
                ShowToast("⚡ УЛЬТА АКТИВИРОВАНА: " + BalanceTables.Uws[(int)chosen].TitleRu + "!", 3.5f);
                _cachedSynergyHash = int.MinValue;
            }
        }

        private static void AnimateFtueBob(RectTransform rect, float baseY)
        {
            if (rect == null || !rect.gameObject.activeSelf) return;
            Vector2 p = rect.anchoredPosition;
            p.y = baseY + Mathf.Sin(Time.unscaledTime * 7.5f) * 8f;
            rect.anchoredPosition = p;
        }
    }
}
