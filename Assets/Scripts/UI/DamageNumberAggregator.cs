using Crusher.Config;
using Crusher.Sim;
using TMPro;
using UnityEngine;

namespace Crusher.UI
{
    /// <summary>
    /// Pooled <= 6 floating combat damage numbers reading aggregated spatial DamageBuckets from CombatSim.
    /// Positioned in the FullScreenArenaOverlay (outside SafeAreaRoot) via exact ScreenPointToLocalPointInRectangle
    /// so numbers align 1:1 with 3D world positions in both 9:20 Portrait and 16:9 Landscape.
    /// Zero GC allocations per frame (writes directly via TMP_Text.SetCharArray).
    /// </summary>
    public sealed class DamageNumberAggregator
    {
        public const int MaxPopups = CombatSim.DAMAGE_BUCKET_COUNT; // Strictly <= 6 on screen

        private struct PopupSlot
        {
            public bool Active;
            public RectTransform Rect;
            public TextMeshProUGUI Label;
            public Vector2 StartPos;
            public float Elapsed;
            public float Duration;
            public bool IsCrit;
        }

        private readonly PopupSlot[] _pool = new PopupSlot[MaxPopups];
        private readonly char[] _charScratch = new char[32];
        private RectTransform _layerRect;
        private Canvas _canvas;
        private Camera _worldCam;
        private float _drainTimer;
        private int _roundRobinIdx;

        private static readonly Color NormalColor = new Color(0.95f, 0.98f, 1.00f, 1.0f);
        private static readonly Color CritColor = new Color(1.00f, 0.84f, 0.22f, 1.0f);

        public RectTransform LayerRect => _layerRect;

        public void Build(Transform fullScreenOverlayParent, Canvas canvas, UiStyle style, Camera worldCam)
        {
            _canvas = canvas;
            _worldCam = worldCam;
            _drainTimer = 0f;
            _roundRobinIdx = 0;

            _layerRect = UiFactory.CreateStretchRect("DamageNumbersLayer", fullScreenOverlayParent);

            for (int i = 0; i < MaxPopups; i++)
            {
                TextMeshProUGUI tmp = UiFactory.CreateTmp(
                    "DmgNum_" + i,
                    _layerRect,
                    style.Font,
                    "",
                    28f,
                    NormalColor,
                    TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(220f, 54f),
                    wrap: false);

                tmp.fontStyle = FontStyles.Bold;
                tmp.outlineWidth = 0.22f;
                tmp.outlineColor = new Color32(8, 12, 18, 235);
                tmp.gameObject.SetActive(false);

                _pool[i] = new PopupSlot
                {
                    Active = false,
                    Rect = tmp.rectTransform,
                    Label = tmp,
                    Elapsed = 0f,
                    Duration = 0.68f
                };
            }
        }

        public void SetWorldCamera(Camera worldCam)
        {
            _worldCam = worldCam;
        }

        public void ClearAll()
        {
            for (int i = 0; i < MaxPopups; i++)
            {
                if (_pool[i].Active)
                {
                    _pool[i].Active = false;
                    if (_pool[i].Rect != null) _pool[i].Rect.gameObject.SetActive(false);
                }
            }
        }

        public void Tick(CombatSim sim, float unscaledDt)
        {
            if (sim == null || _layerRect == null) return;

            float dt = Mathf.Clamp(unscaledDt, 0f, 0.1f);
            _drainTimer -= dt;

            if (_drainTimer <= 0f)
            {
                _drainTimer = 0.10f;
                DrainSimBuckets(sim);
            }

            for (int i = 0; i < MaxPopups; i++)
            {
                if (!_pool[i].Active) continue;

                ref PopupSlot s = ref _pool[i];
                s.Elapsed += dt;
                float t = Mathf.Clamp01(s.Elapsed / s.Duration);

                if (t >= 1f)
                {
                    s.Active = false;
                    s.Rect.gameObject.SetActive(false);
                    continue;
                }

                float rise = 62f * (1f - (1f - t) * (1f - t));
                s.Rect.anchoredPosition = new Vector2(s.StartPos.x, s.StartPos.y + rise);

                float scale = t < 0.18f
                    ? Mathf.Lerp(s.IsCrit ? 1.32f : 1.15f, 1.0f, t / 0.18f)
                    : 1.0f;
                s.Rect.localScale = new Vector3(scale, scale, 1f);

                float alpha = t < 0.55f ? 1f : Mathf.Clamp01(1f - (t - 0.55f) / 0.45f);
                Color baseCol = s.IsCrit ? CritColor : NormalColor;
                s.Label.color = new Color(baseCol.r, baseCol.g, baseCol.b, alpha);
            }
        }

        private void DrainSimBuckets(CombatSim sim)
        {
            for (int b = 0; b < CombatSim.DAMAGE_BUCKET_COUNT; b++)
            {
                if (!sim.DamageBuckets[b].Active) continue;

                double dmg = sim.DamageBuckets[b].AccumulatedDamage;
                bool crit = sim.DamageBuckets[b].HasCrit;
                float sx = sim.DamageBuckets[b].PosX;
                float sy = sim.DamageBuckets[b].PosY;

                sim.DamageBuckets[b].Active = false;
                sim.DamageBuckets[b].AccumulatedDamage = 0.0;
                sim.DamageBuckets[b].HasCrit = false;

                if (dmg <= 0.05) continue;

                Vector2 localPos = ProjectSimToFullScreen(sx, sy, 0.85f);
                SpawnPopup(localPos, dmg, crit);
            }
        }

        private void SpawnPopup(Vector2 anchoredPos, double dmg, bool isCrit)
        {
            int chosen = -1;
            for (int i = 0; i < MaxPopups; i++)
            {
                if (!_pool[i].Active)
                {
                    chosen = i;
                    break;
                }
            }
            if (chosen < 0)
            {
                chosen = _roundRobinIdx;
                _roundRobinIdx = (_roundRobinIdx + 1) % MaxPopups;
            }

            ref PopupSlot s = ref _pool[chosen];
            s.Active = true;
            s.IsCrit = isCrit;
            s.Elapsed = 0f;
            s.Duration = isCrit ? 0.76f : 0.62f;
            s.StartPos = anchoredPos;

            int len = UiFormat.WriteDamagePopupChars(_charScratch, dmg, isCrit);
            s.Label.fontSize = isCrit ? 34f : 26f;
            s.Label.color = isCrit ? CritColor : NormalColor;
            s.Label.SetCharArray(_charScratch, 0, len);

            s.Rect.anchoredPosition = anchoredPos;
            s.Rect.localScale = Vector3.one * (isCrit ? 1.28f : 1.12f);
            if (!s.Rect.gameObject.activeSelf)
            {
                s.Rect.gameObject.SetActive(true);
            }
        }

        public Vector2 ProjectSimToFullScreen(float simX, float simY, float worldHeightY)
        {
            Vector3 worldPos = new Vector3(
                simX * GameIdsAndScale.WORLD_SCALE,
                worldHeightY,
                simY * GameIdsAndScale.WORLD_SCALE);

            return UiScreenSpace.ProjectWorldToOverlay(_worldCam, _canvas, _layerRect, worldPos);
        }
    }
}
