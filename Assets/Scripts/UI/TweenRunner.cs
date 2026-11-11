using System;
using System.Text;
using Crusher.Audio;
using Crusher.Config;
using Crusher.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Crusher.UI
{
    /// <summary>
    /// Zero-allocation fixed-pool tween runner for industrial UI micro-animations
    /// (scale punch on upgrade taps, hover/press uniform scales, CanvasGroup fades, anchored Y slides).
    /// </summary>
    public sealed class TweenRunner
    {
        private enum TweenKind : byte
        {
            None = 0,
            PunchScale = 1,
            CanvasAlpha = 2,
            AnchoredY = 3,
            UniformScale = 4
        }

        private struct TweenSlot
        {
            public TweenKind Kind;
            public RectTransform Rect;
            public CanvasGroup Group;
            public float Elapsed;
            public float Duration;
            public float From;
            public float To;
            public Vector3 BaseScale;
        }

        private const int MaxTweens = 64;
        private readonly TweenSlot[] _slots = new TweenSlot[MaxTweens];

        public void PunchScale(RectTransform target, float amplitude = 0.11f, float duration = 0.18f)
        {
            if (target == null) return;

            // Cancel any UniformScale on same rect so punch plays cleanly around Vector3.one
            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.UniformScale && ReferenceEquals(_slots[i].Rect, target))
                {
                    _slots[i].Kind = TweenKind.None;
                }
            }

            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.PunchScale && ReferenceEquals(_slots[i].Rect, target))
                {
                    target.localScale = Vector3.one;
                    _slots[i].BaseScale = Vector3.one;
                    _slots[i].Elapsed = 0f;
                    _slots[i].Duration = Mathf.Max(0.04f, duration);
                    _slots[i].From = amplitude;
                    return;
                }
            }

            int idx = AllocateSlot();
            _slots[idx] = new TweenSlot
            {
                Kind = TweenKind.PunchScale,
                Rect = target,
                Elapsed = 0f,
                Duration = Mathf.Max(0.04f, duration),
                From = amplitude,
                BaseScale = Vector3.one
            };
        }

        public void SetScale(RectTransform target, float toUniformScale, float duration = 0.11f)
        {
            if (target == null) return;
            float curScale = target.localScale.x;

            // Do not override an active punch animation until it settles
            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.PunchScale && ReferenceEquals(_slots[i].Rect, target))
                {
                    _slots[i].BaseScale = new Vector3(toUniformScale, toUniformScale, 1f);
                    return;
                }
            }

            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.UniformScale && ReferenceEquals(_slots[i].Rect, target))
                {
                    _slots[i].Elapsed = 0f;
                    _slots[i].Duration = Mathf.Max(0.02f, duration);
                    _slots[i].From = curScale;
                    _slots[i].To = toUniformScale;
                    return;
                }
            }

            int idx = AllocateSlot();
            _slots[idx] = new TweenSlot
            {
                Kind = TweenKind.UniformScale,
                Rect = target,
                Elapsed = 0f,
                Duration = Mathf.Max(0.02f, duration),
                From = curScale,
                To = toUniformScale
            };
        }

        public void FadeCanvasGroup(CanvasGroup group, float from, float to, float duration = 0.18f)
        {
            if (group == null) return;
            group.alpha = from;

            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.CanvasAlpha && ReferenceEquals(_slots[i].Group, group))
                {
                    _slots[i].Elapsed = 0f;
                    _slots[i].Duration = Mathf.Max(0.02f, duration);
                    _slots[i].From = from;
                    _slots[i].To = to;
                    return;
                }
            }

            int idx = AllocateSlot();
            _slots[idx] = new TweenSlot
            {
                Kind = TweenKind.CanvasAlpha,
                Group = group,
                Elapsed = 0f,
                Duration = Mathf.Max(0.02f, duration),
                From = from,
                To = to
            };
        }

        public void SlideAnchoredY(RectTransform target, float fromY, float toY, float duration = 0.22f)
        {
            if (target == null) return;
            Vector2 p = target.anchoredPosition;
            p.y = fromY;
            target.anchoredPosition = p;

            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.AnchoredY && ReferenceEquals(_slots[i].Rect, target))
                {
                    _slots[i].Elapsed = 0f;
                    _slots[i].Duration = Mathf.Max(0.03f, duration);
                    _slots[i].From = fromY;
                    _slots[i].To = toY;
                    return;
                }
            }

            int idx = AllocateSlot();
            _slots[idx] = new TweenSlot
            {
                Kind = TweenKind.AnchoredY,
                Rect = target,
                Elapsed = 0f,
                Duration = Mathf.Max(0.03f, duration),
                From = fromY,
                To = toY
            };
        }

        public void Tick(float unscaledDt)
        {
            if (unscaledDt <= 0f) return;
            float dt = Mathf.Min(unscaledDt, 0.1f);

            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.None) continue;

                ref TweenSlot s = ref _slots[i];
                s.Elapsed += dt;
                float t = Mathf.Clamp01(s.Elapsed / s.Duration);

                switch (s.Kind)
                {
                    case TweenKind.PunchScale:
                        if (s.Rect == null)
                        {
                            s.Kind = TweenKind.None;
                            break;
                        }
                        if (t >= 1f)
                        {
                            s.Rect.localScale = s.BaseScale;
                            s.Kind = TweenKind.None;
                        }
                        else
                        {
                            float wave = Mathf.Sin(t * Mathf.PI) * (1f - 0.35f * t);
                            s.Rect.localScale = s.BaseScale * (1f + s.From * wave);
                        }
                        break;

                    case TweenKind.CanvasAlpha:
                        if (s.Group == null)
                        {
                            s.Kind = TweenKind.None;
                            break;
                        }
                        float easeA = 1f - (1f - t) * (1f - t);
                        s.Group.alpha = Mathf.Lerp(s.From, s.To, easeA);
                        if (t >= 1f) s.Kind = TweenKind.None;
                        break;

                    case TweenKind.AnchoredY:
                        if (s.Rect == null)
                        {
                            s.Kind = TweenKind.None;
                            break;
                        }
                        float inv = 1f - t;
                        float easeY = 1f - inv * inv * inv;
                        Vector2 pos = s.Rect.anchoredPosition;
                        pos.y = Mathf.LerpUnclamped(s.From, s.To, easeY);
                        s.Rect.anchoredPosition = pos;
                        if (t >= 1f) s.Kind = TweenKind.None;
                        break;

                    case TweenKind.UniformScale:
                        if (s.Rect == null)
                        {
                            s.Kind = TweenKind.None;
                            break;
                        }
                        float easeS = 1f - (1f - t) * (1f - t);
                        float sc = Mathf.Lerp(s.From, s.To, easeS);
                        s.Rect.localScale = new Vector3(sc, sc, 1f);
                        if (t >= 1f) s.Kind = TweenKind.None;
                        break;
                }
            }
        }

        private int AllocateSlot()
        {
            for (int i = 0; i < MaxTweens; i++)
            {
                if (_slots[i].Kind == TweenKind.None) return i;
            }
            // Evict slot 0 cleanly if saturated
            if (_slots[0].Kind == TweenKind.PunchScale && _slots[0].Rect != null)
            {
                _slots[0].Rect.localScale = _slots[0].BaseScale;
            }
            _slots[0].Kind = TweenKind.None;
            return 0;
        }
    }

    /// <summary>
    /// Pre-allocated exact icon ID tables matching Assets/Art/Generated/Icons/atlas_icons.json 1-to-1.
    /// Zero string concatenation at runtime.
    /// </summary>
    public static class UiIcons
    {
        public const string Cash = "icon_cur_cash";
        public const string Coins = "icon_cur_coins";
        public const string Gems = "icon_cur_gems";
        public const string Stones = "icon_cur_stones";
        public const string Cells = "icon_cur_cells";

        public const string TabAttack = "icon_tab_attack";
        public const string TabDefense = "icon_tab_defense";
        public const string TabUtility = "icon_tab_utility";

        public const string TabWorkshop = "icon_tab_workshop";
        public const string TabCards = "icon_tab_cards";
        public const string TabLabs = "icon_tab_labs";
        public const string TabUw = "icon_tab_uw";

        public const string BotGolden = "bot_GoldenBot";
        public const string BotGemDrone = "bot_GemDrone";
        public const string SkinMoltenCore = "skin_MoltenCore";

        public static readonly string[] StatIcons = new string[GameIdsAndScale.STAT_COUNT]
        {
            "stat_Damage",
            "stat_AttackSpeed",
            "stat_CritChance",
            "stat_CritFactor",
            "stat_Range",
            "stat_MultishotChance",
            "stat_MultishotTargets",
            "stat_Health",
            "stat_HealthRegen",
            "stat_DefenseAbsolute",
            "stat_DefensePercent",
            "stat_Thorns",
            "stat_KnockbackChance",
            "stat_KnockbackForce",
            "stat_OrbsCount",
            "stat_OrbSpeed",
            "stat_Lifesteal",
            "stat_CashBonus",
            "stat_CashPerWave",
            "stat_CoinsPerKill",
            "stat_CoinsPerWave",
            "stat_FreeUpgrades"
        };

        public static readonly string[] CardIcons = new string[GameIdsAndScale.CARD_COUNT]
        {
            "card_EnemyBalance",
            "card_Coins",
            "card_Damage",
            "card_Health",
            "card_ExtraOrb",
            "card_PlasmaCannon",
            "card_DeathRay",
            "card_WaveAccelerator"
        };

        public static readonly string[] LabIcons = new string[GameIdsAndScale.LAB_COUNT]
        {
            "lab_GameSpeed",
            "lab_MagneticGarlic",
            "lab_CoinsPerKill",
            "lab_CarbideOverdrive",
            "lab_OrbAndRotorSpeed",
            "lab_BlackHoleCoinBonus"
        };

        public static readonly string[] UwIcons = new string[GameIdsAndScale.UW_COUNT]
        {
            "uw_GoldenTower",
            "uw_BlackHole",
            "uw_ChainLightning"
        };
    }

    /// <summary>
    /// Shared ProceduralSDFUI material presets and shader property IDs.
    /// Cloned once in BuildOrBind so uGUI batches shared cards/buttons with zero per-frame allocations.
    /// </summary>
    public sealed class UiStyle
    {
        public static readonly int PropBgTop = Shader.PropertyToID("_BgTopColor");
        public static readonly int PropBgBot = Shader.PropertyToID("_BgBottomColor");
        public static readonly int PropBorder = Shader.PropertyToID("_BorderColor");
        public static readonly int PropAccent = Shader.PropertyToID("_AccentColor");
        public static readonly int PropTrail = Shader.PropertyToID("_TrailColor");
        public static readonly int PropShapeMode = Shader.PropertyToID("_ShapeMode");
        public static readonly int PropRectSize = Shader.PropertyToID("_RectSize");
        public static readonly int PropChamfer = Shader.PropertyToID("_ChamferPx");
        public static readonly int PropBorderPx = Shader.PropertyToID("_BorderPx");
        public static readonly int PropFill = Shader.PropertyToID("_FillAmount");
        public static readonly int PropTrailAmt = Shader.PropertyToID("_TrailAmount");
        public static readonly int PropRingThick = Shader.PropertyToID("_RingThickness");
        public static readonly int PropGlow = Shader.PropertyToID("_GlowStrength");

        // Industrial Palette
        public static readonly Color TextPrimary = new Color(0.95f, 0.97f, 1.00f, 1.0f);
        public static readonly Color TextSecondary = new Color(0.68f, 0.76f, 0.86f, 1.0f);
        public static readonly Color TextMuted = new Color(0.48f, 0.55f, 0.65f, 1.0f);
        public static readonly Color TextAmber = new Color(1.00f, 0.82f, 0.24f, 1.0f);
        public static readonly Color TextCyan = new Color(0.32f, 0.90f, 1.00f, 1.0f);
        public static readonly Color TextEmerald = new Color(0.36f, 0.96f, 0.62f, 1.0f);
        public static readonly Color TextCrimson = new Color(1.00f, 0.36f, 0.34f, 1.0f);
        public static readonly Color TextButtonDark = new Color(0.08f, 0.06f, 0.02f, 1.0f);

        public readonly CrusherAssetRegistry Registry;
        public readonly TMP_FontAsset Font;

        // Shared batched SDF materials
        public readonly Material MatTopHud;
        public readonly Material MatDeckBg;
        public readonly Material MatHeaderStrip;
        public readonly Material MatPill;
        public readonly Material MatStatCard;
        public readonly Material MatCardRare;
        public readonly Material MatCardEpic;
        public readonly Material MatCardLocked;
        public readonly Material MatBuyAmber;
        public readonly Material MatBuyCyan;
        public readonly Material MatBuyGreen;
        public readonly Material MatBuyDisabled;
        public readonly Material MatDangerBtn;
        public readonly Material MatTabActive;
        public readonly Material MatTabInactive;
        public readonly Material MatModalWindow;

        // Unique dynamic SDF materials (updated via shader properties in TickUI)
        public readonly Material MatCoreHpRing;
        public readonly Material MatWaveBar;
        public readonly Material MatSynergyBanner;
        public readonly Material MatToastBanner;
        public readonly Material MatFtueArrowHud;
        public readonly Material MatFtueArrowModal;
        public readonly Material MatFtueArrowHangar;
        public readonly Material[] MatLabProgress = new Material[BalanceTables.MAX_LAB_SLOTS];

        public UiStyle(CrusherAssetRegistry registry)
        {
            Registry = registry;
            Font = registry != null ? registry.MainFont : null;
            Material baseSdf = registry != null ? registry.MatSdfUi : null;

            MatTopHud = MakeRectMat(baseSdf, 1040f, 204f, 16f, 2.5f,
                new Color(0.13f, 0.17f, 0.23f, 0.95f), new Color(0.07f, 0.09f, 0.13f, 0.97f),
                new Color(0.34f, 0.46f, 0.60f, 1f), new Color(0.25f, 0.80f, 1.00f, 1f), 0.15f);

            MatDeckBg = MakeRectMat(baseSdf, 1080f, 1004f, 22f, 3.0f,
                new Color(0.12f, 0.15f, 0.20f, 0.97f), new Color(0.06f, 0.08f, 0.11f, 0.99f),
                new Color(0.32f, 0.42f, 0.54f, 1f), new Color(1.00f, 0.68f, 0.14f, 1f), 0.12f);

            MatHeaderStrip = MakeRectMat(baseSdf, 1040f, 76f, 12f, 2.0f,
                new Color(0.16f, 0.21f, 0.28f, 0.96f), new Color(0.10f, 0.13f, 0.18f, 0.98f),
                new Color(0.36f, 0.48f, 0.62f, 1f), new Color(0.30f, 0.85f, 1.00f, 1f), 0.18f);

            MatPill = MakeRectMat(baseSdf, 196f, 58f, 10f, 2.0f,
                new Color(0.14f, 0.18f, 0.24f, 0.95f), new Color(0.09f, 0.11f, 0.16f, 0.97f),
                new Color(0.28f, 0.38f, 0.50f, 1f), new Color(0.30f, 0.82f, 1.00f, 1f), 0f);

            MatStatCard = MakeRectMat(baseSdf, 504f, 128f, 13f, 2.2f,
                new Color(0.17f, 0.22f, 0.29f, 0.97f), new Color(0.10f, 0.13f, 0.18f, 0.98f),
                new Color(0.33f, 0.43f, 0.55f, 1f), new Color(0.35f, 0.75f, 0.95f, 1f), 0.08f);

            MatCardRare = MakeRectMat(baseSdf, 504f, 200f, 14f, 2.6f,
                new Color(0.13f, 0.23f, 0.33f, 0.97f), new Color(0.08f, 0.14f, 0.21f, 0.98f),
                new Color(0.26f, 0.78f, 1.00f, 1f), new Color(0.26f, 0.84f, 1.15f, 1f), 0.42f);

            MatCardEpic = MakeRectMat(baseSdf, 504f, 200f, 14f, 2.8f,
                new Color(0.28f, 0.20f, 0.10f, 0.97f), new Color(0.15f, 0.10f, 0.05f, 0.98f),
                new Color(1.00f, 0.74f, 0.20f, 1f), new Color(1.20f, 0.80f, 0.18f, 1f), 0.55f);

            MatCardLocked = MakeRectMat(baseSdf, 504f, 128f, 13f, 2.0f,
                new Color(0.11f, 0.13f, 0.17f, 0.92f), new Color(0.07f, 0.09f, 0.12f, 0.94f),
                new Color(0.24f, 0.29f, 0.36f, 0.9f), new Color(0.4f, 0.45f, 0.5f, 1f), 0f);

            MatBuyAmber = MakeRectMat(baseSdf, 194f, 64f, 10f, 2.4f,
                new Color(0.95f, 0.62f, 0.10f, 0.99f), new Color(0.72f, 0.40f, 0.04f, 0.99f),
                new Color(1.00f, 0.86f, 0.42f, 1f), new Color(1.25f, 0.82f, 0.18f, 1f), 0.85f);

            MatBuyCyan = MakeRectMat(baseSdf, 210f, 68f, 11f, 2.4f,
                new Color(0.12f, 0.52f, 0.72f, 0.98f), new Color(0.07f, 0.32f, 0.48f, 0.99f),
                new Color(0.38f, 0.88f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.20f, 1f), 0.75f);

            MatBuyGreen = MakeRectMat(baseSdf, 210f, 68f, 11f, 2.4f,
                new Color(0.14f, 0.58f, 0.34f, 0.98f), new Color(0.08f, 0.36f, 0.20f, 0.99f),
                new Color(0.42f, 0.96f, 0.64f, 1f), new Color(0.32f, 1.10f, 0.58f, 1f), 0.75f);

            MatBuyDisabled = MakeRectMat(baseSdf, 194f, 64f, 10f, 1.8f,
                new Color(0.14f, 0.17f, 0.22f, 0.94f), new Color(0.10f, 0.12f, 0.16f, 0.96f),
                new Color(0.24f, 0.30f, 0.38f, 0.9f), new Color(0.25f, 0.30f, 0.36f, 1f), 0f);

            MatDangerBtn = MakeRectMat(baseSdf, 176f, 64f, 10f, 2.2f,
                new Color(0.56f, 0.16f, 0.16f, 0.97f), new Color(0.34f, 0.09f, 0.10f, 0.99f),
                new Color(0.96f, 0.40f, 0.38f, 1f), new Color(1.10f, 0.30f, 0.25f, 1f), 0.45f);

            MatTabActive = MakeRectMat(baseSdf, 248f, 134f, 13f, 2.8f,
                new Color(0.22f, 0.30f, 0.40f, 0.99f), new Color(0.13f, 0.18f, 0.26f, 0.99f),
                new Color(1.00f, 0.76f, 0.22f, 1f), new Color(1.20f, 0.78f, 0.16f, 1f), 0.95f, 1.0f);

            MatTabInactive = MakeRectMat(baseSdf, 248f, 134f, 13f, 2.0f,
                new Color(0.13f, 0.17f, 0.22f, 0.95f), new Color(0.08f, 0.11f, 0.15f, 0.97f),
                new Color(0.26f, 0.34f, 0.44f, 1f), new Color(0.30f, 0.40f, 0.50f, 1f), 0f, 0f);

            MatModalWindow = MakeRectMat(baseSdf, 940f, 1120f, 22f, 3.2f,
                new Color(0.15f, 0.20f, 0.27f, 0.99f), new Color(0.08f, 0.11f, 0.16f, 0.99f),
                new Color(1.00f, 0.74f, 0.22f, 1f), new Color(1.15f, 0.75f, 0.16f, 1f), 0.55f);

            // Unique dynamic materials
            MatCoreHpRing = CloneSdf(baseSdf);
            if (MatCoreHpRing != null)
            {
                MatCoreHpRing.SetFloat(PropShapeMode, 1.0f);
                MatCoreHpRing.SetColor(PropBgBot, new Color(0.06f, 0.09f, 0.13f, 0.85f));
                MatCoreHpRing.SetColor(PropBorder, new Color(0.32f, 0.46f, 0.60f, 0.95f));
                MatCoreHpRing.SetColor(PropAccent, new Color(0.22f, 0.98f, 0.58f, 1.0f));
                MatCoreHpRing.SetColor(PropTrail, new Color(1.15f, 0.26f, 0.20f, 0.96f));
                MatCoreHpRing.SetFloat(PropRingThick, 0.125f);
                MatCoreHpRing.SetFloat(PropFill, 1.0f);
                MatCoreHpRing.SetFloat(PropTrailAmt, 1.0f);
            }

            MatWaveBar = MakeRectMat(baseSdf, 420f, 54f, 10f, 2.0f,
                new Color(0.13f, 0.18f, 0.25f, 0.97f), new Color(0.08f, 0.11f, 0.16f, 0.98f),
                new Color(0.32f, 0.52f, 0.70f, 1f), new Color(0.24f, 0.88f, 1.12f, 1f), 0.35f, 0.5f);

            MatSynergyBanner = MakeRectMat(baseSdf, 760f, 56f, 11f, 2.2f,
                new Color(0.34f, 0.23f, 0.06f, 0.96f), new Color(0.18f, 0.11f, 0.03f, 0.98f),
                new Color(1.00f, 0.78f, 0.24f, 1f), new Color(1.30f, 0.88f, 0.20f, 1f), 0.95f, 1.0f);

            MatToastBanner = MakeRectMat(baseSdf, 880f, 68f, 12f, 2.5f,
                new Color(0.24f, 0.18f, 0.08f, 0.97f), new Color(0.12f, 0.09f, 0.04f, 0.99f),
                new Color(1.00f, 0.76f, 0.20f, 1f), new Color(1.25f, 0.82f, 0.18f, 1f), 0.85f, 1.0f);

            MatFtueArrowHud = MakeArrowMat(baseSdf);
            MatFtueArrowModal = MakeArrowMat(baseSdf);
            MatFtueArrowHangar = MakeArrowMat(baseSdf);

            for (int i = 0; i < BalanceTables.MAX_LAB_SLOTS; i++)
            {
                MatLabProgress[i] = MakeRectMat(baseSdf, 980f, 134f, 13f, 2.2f,
                    new Color(0.15f, 0.21f, 0.29f, 0.97f), new Color(0.09f, 0.13f, 0.18f, 0.98f),
                    new Color(0.30f, 0.68f, 0.92f, 1f), new Color(0.25f, 0.88f, 1.15f, 1f), 0.40f, 0f);
            }
        }

        public Sprite GetIcon(string iconId)
        {
            return Registry != null ? Registry.GetIcon(iconId) : null;
        }

        public static void PlaySfx(SfxId id)
        {
            ProceduralSfxPlayer inst = ProceduralSfxPlayer.Instance;
            if (inst != null)
            {
                inst.PlayUI(id);
            }
        }

        public static bool IsMuted =>
            ProceduralSfxPlayer.Instance != null ? ProceduralSfxPlayer.Instance.IsMuted : AudioListener.pause;

        public static bool ToggleMute()
        {
            ProceduralSfxPlayer inst = ProceduralSfxPlayer.Instance;
            if (inst != null)
            {
                inst.ToggleMute();
                if (!inst.IsMuted)
                {
                    inst.PlayUI(SfxId.UiTap);
                }
                return inst.IsMuted;
            }
            AudioListener.pause = !AudioListener.pause;
            return AudioListener.pause;
        }

        private static Material CloneSdf(Material baseSdf)
        {
            if (baseSdf == null) return null;
            return new Material(baseSdf);
        }

        private static Material MakeArrowMat(Material baseSdf)
        {
            Material m = CloneSdf(baseSdf);
            if (m != null)
            {
                m.SetFloat(PropShapeMode, 2.0f);
                m.SetColor(PropBorder, new Color(0.45f, 0.22f, 0.02f, 1.0f));
                m.SetColor(PropAccent, new Color(1.35f, 0.84f, 0.14f, 1.0f));
            }
            return m;
        }

        private static Material MakeRectMat(
            Material baseSdf, float w, float h, float chamfer, float borderPx,
            Color bgTop, Color bgBot, Color borderCol, Color accentCol, float glow, float fill = 0f)
        {
            Material m = CloneSdf(baseSdf);
            if (m != null)
            {
                m.SetFloat(PropShapeMode, 0.0f);
                m.SetVector(PropRectSize, new Vector4(w, h, 0f, 0f));
                m.SetFloat(PropChamfer, chamfer);
                m.SetFloat(PropBorderPx, borderPx);
                m.SetColor(PropBgTop, bgTop);
                m.SetColor(PropBgBot, bgBot);
                m.SetColor(PropBorder, borderCol);
                m.SetColor(PropAccent, accentCol);
                m.SetFloat(PropGlow, glow);
                m.SetFloat(PropFill, fill);
            }
            return m;
        }
    }

    /// <summary>
    /// Tactile desktop/mobile hover & press feedback (1.025x scale + border rim highlight)
    /// driven by the zero-allocation TweenRunner pool.
    /// </summary>
    public sealed class UiHoverFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private TweenRunner _tweens;
        private RectTransform _target;
        private Image _hoverRim;
        private Image _hoverSheen;
        private Selectable _selectable;
        private float _hoverScale = 1.025f;
        private float _pressScale = 0.972f;
        private bool _hovered;

        public void Configure(TweenRunner tweens, RectTransform target, Image hoverRim, Image hoverSheen, float hoverScale = 1.025f, float pressScale = 0.972f)
        {
            _tweens = tweens;
            _target = target;
            _hoverRim = hoverRim;
            _hoverSheen = hoverSheen;
            _hoverScale = hoverScale;
            _pressScale = pressScale;
            _selectable = GetComponent<Selectable>();
            SetHighlightVisible(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.IsInteractable()) return;
            _hovered = true;
            SetHighlightVisible(true);
            if (_tweens != null && _target != null)
            {
                _tweens.SetScale(_target, _hoverScale, 0.10f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            SetHighlightVisible(false);
            if (_tweens != null && _target != null)
            {
                _tweens.SetScale(_target, 1.0f, 0.12f);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.IsInteractable()) return;
            if (_tweens != null && _target != null)
            {
                _tweens.SetScale(_target, _pressScale, 0.06f);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_tweens != null && _target != null)
            {
                _tweens.SetScale(_target, _hovered ? _hoverScale : 1.0f, 0.10f);
            }
        }

        private void OnDisable()
        {
            _hovered = false;
            SetHighlightVisible(false);
            if (_target != null)
            {
                _target.localScale = Vector3.one;
            }
        }

        private void SetHighlightVisible(bool vis)
        {
            if (_hoverRim != null && _hoverRim.enabled != vis) _hoverRim.enabled = vis;
            if (_hoverSheen != null && _hoverSheen.enabled != vis) _hoverSheen.enabled = vis;
        }
    }

    /// <summary>
    /// Exact full-screen projection helper (outside SafeAreaRoot) so diegetic rotor HP ring,
    /// GemDrone badge, and damage numbers stay 1:1 concentric on 16:9 Desktop, 9:20 Mobile, and OffscreenGate.
    /// </summary>
    public static class UiScreenSpace
    {
        public static Vector2 ProjectWorldToOverlay(Camera worldCam, Canvas canvas, RectTransform fullScreenOverlayRect, Vector3 worldPos)
        {
            if (worldCam != null && canvas != null && fullScreenOverlayRect != null)
            {
                Vector3 vp = worldCam.WorldToViewportPoint(worldPos);
                if (vp.z > 0.05f)
                {
                    int sw = worldCam.targetTexture != null ? worldCam.targetTexture.width : Screen.width;
                    int sh = worldCam.targetTexture != null ? worldCam.targetTexture.height : Screen.height;
                    if (sw <= 0 || sh <= 0)
                    {
                        sw = 1080;
                        sh = 2400;
                    }

                    Rect r = fullScreenOverlayRect.rect;
                    float rw = r.width;
                    float rh = r.height;
                    bool targetLandscape = sw > sh * 1.1f;
                    bool rectMatchesOrientation = rw > 10f && rh > 10f && ((rw > rh) == targetLandscape);
                    if (!rectMatchesOrientation)
                    {
                        float refW = targetLandscape ? 1920f : 1080f;
                        float refH = targetLandscape ? 1080f : 2400f;
                        float scaleFactor = targetLandscape
                            ? Mathf.Min(sw / refW, sh / refH)
                            : Mathf.Pow(2f, (Mathf.Log(sw / refW, 2f) + Mathf.Log(sh / refH, 2f)) * 0.5f);
                        rw = sw / scaleFactor;
                        rh = sh / scaleFactor;
                    }
                    return new Vector2((vp.x - 0.5f) * rw, (vp.y - 0.5f) * rh);
                }
            }
            return new Vector2(0f, 160f);
        }
    }

    /// <summary>
    /// Programmatic uGUI widget builders (adaptive 1080x2400 Portrait & 1920x1080 Landscape, zero runtime prefab dependencies).
    /// Note: Images using ProceduralSDFUI keep sprite=null so UV stays [0..1]; atlas icons use child Images with default UI material.
    /// </summary>
    public static class UiFactory
    {
        private static TweenRunner _activeTweens;

        public static void SetGlobalTweenRunner(TweenRunner tweens)
        {
            _activeTweens = tweens;
        }

        public static UiHoverFeedback AttachHover(RectTransform target, float hoverScale = 1.025f, float pressScale = 0.972f, Color? rimTint = null)
        {
            if (target == null) return null;
            UiHoverFeedback hf = target.GetComponent<UiHoverFeedback>();
            if (hf != null) return hf;

            // Subtle interior sheen + crisp top accent rim (default UI material, batched, zero raycast)
            RectTransform sheenRt = CreateStretchRect("_HoverSheen", target, 4f, 4f, 4f, 4f);
            var sheenImg = sheenRt.gameObject.AddComponent<Image>();
            sheenImg.color = new Color(0.75f, 0.92f, 1.00f, 0.065f);
            sheenImg.raycastTarget = false;
            sheenImg.enabled = false;

            RectTransform rimRt = CreateRect("_HoverRim", target,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -2f), new Vector2(-18f, 3f));
            var rimImg = rimRt.gameObject.AddComponent<Image>();
            rimImg.color = rimTint ?? new Color(0.45f, 0.92f, 1.00f, 0.85f);
            rimImg.raycastTarget = false;
            rimImg.enabled = false;

            hf = target.gameObject.AddComponent<UiHoverFeedback>();
            hf.Configure(_activeTweens, target, rimImg, sheenImg, hoverScale, pressScale);
            return hf;
        }
        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            rt.localScale = Vector3.one;
            return rt;
        }

        public static RectTransform CreateStretchRect(string name, Transform parent, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            rt.localScale = Vector3.one;
            return rt;
        }

        public static Image CreateSdfImage(
            string name, Transform parent, Material sdfMat, Color fallbackColor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta,
            bool raycast = false)
        {
            RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = null;
            if (sdfMat != null)
            {
                img.material = sdfMat;
                img.color = Color.white;
            }
            else
            {
                img.color = fallbackColor;
            }
            img.raycastTarget = raycast;
            return img;
        }

        public static Image CreateIcon(
            string name, Transform parent, Sprite sprite,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta,
            Color? tint = null)
        {
            RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.color = tint ?? Color.white;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI CreateTmp(
            string name, Transform parent, TMP_FontAsset font, string initialText,
            float fontSize, Color color, TextAlignmentOptions align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta,
            bool wrap = false)
        {
            RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = initialText;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateSdfButton(
            string name, Transform parent, UiStyle style, Material sdfMat, Color fallbackColor,
            string labelText, float fontSize, Color textColor,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta,
            UnityAction onClick, out Image bgImage, out TextMeshProUGUI labelTmp)
        {
            bgImage = CreateSdfImage(name, parent, sdfMat, fallbackColor, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta, raycast: true);
            var btn = bgImage.gameObject.AddComponent<Button>();
            btn.targetGraphic = bgImage;
            btn.transition = Selectable.Transition.None;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            labelTmp = CreateTmp(
                "Label", bgImage.transform, style.Font, labelText, fontSize, textColor,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-12f, -6f), wrap: false);

            AttachHover(bgImage.rectTransform, 1.025f, 0.972f);
            return btn;
        }
    }

    /// <summary>
    /// Zero-allocation numeric / stat / timer formatters using char[] + TMP_Text.SetCharArray
    /// and dirty-checked StringBuilder only when quantized values actually change.
    /// </summary>
    public static class UiFormat
    {
        private static readonly char[] SharedChars = new char[128];
        private static readonly StringBuilder SharedSb = new StringBuilder(160);

        public static void SetCombatTelemetry(TMP_Text label, double dps, int rpm, int kills, ref long cachedKey)
        {
            if (label == null) return;
            long dpsQ = ComputeCompactKey(Math.Max(0.0, dps));
            long key = (dpsQ * 1000003L) ^ ((long)rpm * 7919L) ^ (long)kills;
            if (key == cachedKey) return;
            cachedKey = key;

            int pos = 0;
            const string p1 = "⚡ DPS ";
            for (int i = 0; i < p1.Length; i++) SharedChars[pos++] = p1[i];
            pos = AppendCompactDouble(SharedChars, pos, Math.Max(0.1, dps));

            const string p2 = "  •  ОБОРОТЫ ";
            for (int i = 0; i < p2.Length; i++) SharedChars[pos++] = p2[i];
            pos = AppendNonNegInt(SharedChars, pos, Math.Max(0, rpm));

            const string p3 = " RPM  •  УБИТО ";
            for (int i = 0; i < p3.Length; i++) SharedChars[pos++] = p3[i];
            pos = AppendNonNegInt(SharedChars, pos, Math.Max(0, kills));

            label.SetCharArray(SharedChars, 0, pos);
        }

        public static void SetCompactDouble(TMP_Text label, string prefix, double value, ref long cachedKey)
        {
            if (label == null) return;
            if (value < 0.0 || double.IsNaN(value)) value = 0.0;

            // Quantize to 0.01 precision in compact mantissa so cache hits on unchanged frames
            long key = ComputeCompactKey(value);
            if (key == cachedKey) return;
            cachedKey = key;

            int pos = 0;
            if (!string.IsNullOrEmpty(prefix))
            {
                for (int i = 0; i < prefix.Length; i++) SharedChars[pos++] = prefix[i];
            }
            pos = AppendCompactDouble(SharedChars, pos, value);
            label.SetCharArray(SharedChars, 0, pos);
        }

        public static void SetInt(TMP_Text label, string prefix, int value, ref int cachedVal)
        {
            if (label == null) return;
            if (value == cachedVal) return;
            cachedVal = value;

            int pos = 0;
            if (!string.IsNullOrEmpty(prefix))
            {
                for (int i = 0; i < prefix.Length; i++) SharedChars[pos++] = prefix[i];
            }
            pos = AppendNonNegInt(SharedChars, pos, Math.Max(0, value));
            label.SetCharArray(SharedChars, 0, pos);
        }

        public static void SetWaveHeader(TMP_Text label, int wave, bool isBoss, ref int cachedKey)
        {
            if (label == null) return;
            int key = (wave << 1) | (isBoss ? 1 : 0);
            if (key == cachedKey) return;
            cachedKey = key;

            int pos = 0;
            const string wPrefix = "ВОЛНА ";
            for (int i = 0; i < wPrefix.Length; i++) SharedChars[pos++] = wPrefix[i];
            pos = AppendNonNegInt(SharedChars, pos, Math.Max(1, wave));
            if (isBoss)
            {
                const string bTag = " • БОСС!";
                for (int i = 0; i < bTag.Length; i++) SharedChars[pos++] = bTag[i];
            }
            label.SetCharArray(SharedChars, 0, pos);
        }

        public static void SetHpReadout(TMP_Text label, double hp, double maxHp, double regen, ref long cachedKey)
        {
            if (label == null) return;
            long hpQ = (long)Math.Max(0.0, Math.Ceiling(hp));
            long maxQ = (long)Math.Max(1.0, Math.Round(maxHp));
            long regQ = (long)Math.Round(regen * 10.0);
            long key = (hpQ * 397L) ^ (maxQ * 7919L) ^ regQ;
            if (key == cachedKey) return;
            cachedKey = key;

            int pos = 0;
            pos = AppendCompactDouble(SharedChars, pos, hpQ);
            SharedChars[pos++] = ' ';
            SharedChars[pos++] = '/';
            SharedChars[pos++] = ' ';
            pos = AppendCompactDouble(SharedChars, pos, maxQ);
            const string rMid = "  +";
            for (int i = 0; i < rMid.Length; i++) SharedChars[pos++] = rMid[i];
            pos = AppendOneDecimal(SharedChars, pos, regen);
            SharedChars[pos++] = '/';
            SharedChars[pos++] = 'с';
            label.SetCharArray(SharedChars, 0, pos);
        }

        public static void SetTimerMmSs(TMP_Text label, float remainingSec, ref int cachedSec)
        {
            if (label == null) return;
            int totalSec = Mathf.Max(0, Mathf.CeilToInt(remainingSec));
            if (totalSec == cachedSec) return;
            cachedSec = totalSec;

            int mins = totalSec / 60;
            int secs = totalSec % 60;
            int pos = 0;
            if (mins >= 60)
            {
                int hrs = mins / 60;
                mins %= 60;
                pos = AppendNonNegInt(SharedChars, pos, hrs);
                SharedChars[pos++] = ':';
            }
            SharedChars[pos++] = (char)('0' + (mins / 10));
            SharedChars[pos++] = (char)('0' + (mins % 10));
            SharedChars[pos++] = ':';
            SharedChars[pos++] = (char)('0' + (secs / 10));
            SharedChars[pos++] = (char)('0' + (secs % 10));
            label.SetCharArray(SharedChars, 0, pos);
        }

        public static int WriteDamagePopupChars(char[] dest, double dmg, bool isCrit)
        {
            int pos = 0;
            pos = AppendCompactDouble(dest, pos, Math.Max(1.0, dmg));
            if (isCrit)
            {
                dest[pos++] = '!';
            }
            return pos;
        }

        public static string BuildStatTransitionText(StatId id, int curLv, int addLv, int maxLv)
        {
            SharedSb.Clear();
            double curVal = BalanceTables.GetStatValue(id, curLv);
            AppendFormattedStatValue(SharedSb, id, curVal);

            if (curLv >= maxLv)
            {
                SharedSb.Append("  (МАКС)");
            }
            else
            {
                int nextLv = Math.Min(maxLv, curLv + Math.Max(1, addLv));
                double nextVal = BalanceTables.GetStatValue(id, nextLv);
                SharedSb.Append(" → <color=#52E5FF>");
                AppendFormattedStatValue(SharedSb, id, nextVal);
                SharedSb.Append("</color>");
            }
            return SharedSb.ToString();
        }

        public static void AppendFormattedStatValue(StringBuilder sb, StatId id, double rawVal)
        {
            switch (id)
            {
                case StatId.CritChance:
                case StatId.MultishotChance:
                case StatId.DefensePercent:
                case StatId.Thorns:
                case StatId.KnockbackChance:
                case StatId.Lifesteal:
                case StatId.FreeUpgrades:
                    sb.Append((rawVal * 100.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append('%');
                    break;

                case StatId.Range:
                    double meters = rawVal * GameIdsAndScale.WORLD_SCALE;
                    sb.Append(meters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(" м");
                    break;

                case StatId.MultishotTargets:
                case StatId.OrbsCount:
                    sb.Append(((int)Math.Round(rawVal)).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(BalanceTables.Stats[(int)id].UnitSuffix);
                    break;

                case StatId.CashPerWave:
                    sb.Append('$');
                    AppendCompactToSb(sb, rawVal);
                    break;

                case StatId.CritFactor:
                case StatId.CashBonus:
                case StatId.CoinsPerKill:
                    sb.Append(rawVal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append('×');
                    break;

                case StatId.AttackSpeed:
                    sb.Append(rawVal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append("/с");
                    break;

                case StatId.HealthRegen:
                    if (rawVal < 100.0)
                    {
                        sb.Append(rawVal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        AppendCompactToSb(sb, rawVal);
                    }
                    sb.Append("/с");
                    break;

                case StatId.OrbSpeed:
                case StatId.KnockbackForce:
                    sb.Append(rawVal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    break;

                default:
                    AppendCompactToSb(sb, rawVal);
                    break;
            }
        }

        public static void AppendCompactToSb(StringBuilder sb, double v)
        {
            int len = AppendCompactDouble(SharedChars, 0, v);
            sb.Append(SharedChars, 0, len);
        }

        private static long ComputeCompactKey(double v)
        {
            if (v < 1000.0) return (long)Math.Floor(v);
            if (v < 1_000_000.0) return 1_000_000L + (long)Math.Floor(v / 10.0);
            if (v < 1_000_000_000.0) return 2_000_000_000L + (long)Math.Floor(v / 10_000.0);
            return 3_000_000_000_000L + (long)Math.Floor(v / 10_000_000.0);
        }

        public static int AppendCompactDouble(char[] buf, int pos, double val)
        {
            if (val < 0.0) val = 0.0;
            if (val < 1000.0)
            {
                if (val < 10.0 && Math.Abs(val - Math.Round(val)) > 0.05)
                {
                    return AppendOneDecimal(buf, pos, val);
                }
                return AppendNonNegInt(buf, pos, (int)Math.Floor(val + 1e-6));
            }

            char suffix;
            double scaled;
            if (val < 1_000_000.0) { scaled = val * 0.001; suffix = 'K'; }
            else if (val < 1_000_000_000.0) { scaled = val * 0.000001; suffix = 'M'; }
            else { scaled = val * 0.000000001; suffix = 'B'; }

            if (scaled >= 100.0)
            {
                pos = AppendNonNegInt(buf, pos, (int)Math.Floor(scaled));
            }
            else if (scaled >= 10.0)
            {
                pos = AppendOneDecimal(buf, pos, scaled);
            }
            else
            {
                int hundredths = (int)Math.Floor(scaled * 100.0 + 0.5);
                int whole = hundredths / 100;
                int frac = hundredths % 100;
                pos = AppendNonNegInt(buf, pos, whole);
                buf[pos++] = '.';
                buf[pos++] = (char)('0' + (frac / 10));
                buf[pos++] = (char)('0' + (frac % 10));
            }
            buf[pos++] = suffix;
            return pos;
        }

        private static int AppendOneDecimal(char[] buf, int pos, double val)
        {
            int tenths = (int)Math.Floor(Math.Max(0.0, val) * 10.0 + 0.5);
            pos = AppendNonNegInt(buf, pos, tenths / 10);
            buf[pos++] = '.';
            buf[pos++] = (char)('0' + (tenths % 10));
            return pos;
        }

        public static int AppendNonNegInt(char[] buf, int pos, int value)
        {
            if (value <= 0)
            {
                buf[pos++] = '0';
                return pos;
            }
            int start = pos;
            int v = value;
            while (v > 0)
            {
                buf[pos++] = (char)('0' + (v % 10));
                v /= 10;
            }
            int end = pos - 1;
            while (start < end)
            {
                char tmp = buf[start];
                buf[start] = buf[end];
                buf[end] = tmp;
                start++;
                end--;
            }
            return pos;
        }
    }
}
