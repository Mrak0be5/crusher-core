using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Crusher.Core
{
    /// <summary>
    /// Direct serialized asset registry for Crusher Core V1.
    /// Eliminates runtime Resources.Load / Shader.Find so IL2CPP Medium stripping preserves 100% of assets.
    /// </summary>
    public sealed class CrusherAssetRegistry : ScriptableObject
    {
        [Header("UI & Typography")]
        public TMP_FontAsset MainFont;
        public TMP_FontAsset SymbolsFallbackFont;
        public Material MatSdfUi;
        public Texture2D IconAtlasTexture;
        public TextAsset IconAtlasJson;

        [Header("Skeletal Models & Animator Controllers")]
        public GameObject PrefabShredderCore;
        public RuntimeAnimatorController AnimCore;

        public GameObject PrefabBossAtm;
        public RuntimeAnimatorController AnimBossAtm;

        public GameObject PrefabElectromagnetBh;
        public RuntimeAnimatorController AnimElectromagnetBh;

        public GameObject PrefabRailEmitterDr;
        public RuntimeAnimatorController AnimRailEmitterDr;

        public GameObject PrefabGoldenBot;
        public RuntimeAnimatorController AnimGoldenBot;

        [Header("Static Meshes")]
        public Mesh MeshEnemyBasic;
        public Mesh MeshEnemyFast;
        public Mesh MeshEnemyTank;
        public Mesh MeshEnemyRanged;
        public Mesh MeshEnemyVampire;
        public Mesh MeshGoldIngot;
        public Mesh MeshPlasmaSawOrb;
        public Mesh MeshProjectile;
        public Mesh[] MeshesDebrisChunks;

        [Header("Arena & Skeletal Materials")]
        public Material MatArenaFloor;
        public Material MatCoreChassis;
        public Material[] MatCoreGrades; // [0..3] G0_Rusty .. G3_DiamondReactor
        public Material MatBossAtm;
        public Material MatElectromagnetBh;
        public Material MatRailEmitterDr;
        public Material MatGoldenBot;
        public Material MatGemDrone;
        public Material MatOrbSaw;

        [Header("GPU Instanced Materials")]
        public Material[] MatEnemiesInstanced; // Indexed by (int)EnemyType (0=Basic,1=Fast,2=Tank,3=Ranged,4=unused,5=Vampire)
        public Material MatProjectilesInstanced;
        public Material MatDebrisInstanced;
        public Material MatWeldingSparksInstanced;
        public Material MatGoldIngotInstanced;

        [Header("VFX Field & Beam Materials")]
        public Material MatBlackHoleVortex;
        public Material MatGoldenBotField;
        public Material MatBeamDeathRay;
        public Material MatBeamVampireSiphon;
        public Material MatBeamChainZap;

        [Header("Procedural Industrial Audio (22 WAVs)")]
        public AudioClip[] ClipsCrunch;
        public AudioClip ClipBossShatter;
        public AudioClip ClipRotorHumLoop;
        public AudioClip[] ClipsOrbSaw;
        public AudioClip ClipPneumoShot;
        public AudioClip ClipBlackHoleLoop;
        public AudioClip ClipGoldenStart;
        public AudioClip[] ClipsIngotDing;
        public AudioClip ClipChainZap;
        public AudioClip ClipPlasmaHit;
        public AudioClip ClipUiTap;
        public AudioClip ClipUiUpgrade;
        public AudioClip ClipUiUnlock;
        public AudioClip ClipOverloadDeath;

        [NonSerialized] private Dictionary<string, Sprite> _iconSprites;
        [NonSerialized] private Texture2D _cachedAtlasRef;
        [NonSerialized] private Sprite _fallbackSprite;

        public Sprite GetIcon(string iconId)
        {
            EnsureIconCache();

            if (!string.IsNullOrEmpty(iconId))
            {
                if (_iconSprites.TryGetValue(iconId, out Sprite found) && found != null)
                {
                    return found;
                }

                if (!iconId.StartsWith("icon_", StringComparison.OrdinalIgnoreCase))
                {
                    string prefixed = "icon_" + iconId;
                    if (_iconSprites.TryGetValue(prefixed, out Sprite byPrefix) && byPrefix != null)
                    {
                        return byPrefix;
                    }
                }
            }

            return GetOrCreateFallbackSprite();
        }

        private void EnsureIconCache()
        {
            if (_iconSprites != null && _cachedAtlasRef == IconAtlasTexture && _iconSprites.Count > 0)
            {
                return;
            }

            _iconSprites = new Dictionary<string, Sprite>(64, StringComparer.OrdinalIgnoreCase);
            _cachedAtlasRef = IconAtlasTexture;

            if (IconAtlasTexture == null || IconAtlasJson == null)
            {
                return;
            }

            string json = IconAtlasJson.text;
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            int texW = IconAtlasTexture.width;
            int texH = IconAtlasTexture.height;
            int iconsBlockIdx = json.IndexOf("\"icons\"", StringComparison.Ordinal);
            if (iconsBlockIdx < 0)
            {
                return;
            }

            int pos = json.IndexOf('{', iconsBlockIdx);
            if (pos < 0)
            {
                return;
            }
            pos++; // inside "icons": { ... }

            while (pos < json.Length)
            {
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
                int objOpen = json.IndexOf('{', keyEnd);
                if (objOpen < 0) break;
                int objClose = json.IndexOf('}', objOpen + 1);
                if (objClose < 0) break;

                string body = json.Substring(objOpen + 1, objClose - objOpen - 1);
                float u0 = ExtractJsonFloat(body, "\"u0\"", 0f);
                float v0 = ExtractJsonFloat(body, "\"v0\"", 0f);
                float u1 = ExtractJsonFloat(body, "\"u1\"", 0f);
                float v1 = ExtractJsonFloat(body, "\"v1\"", 0f);

                if (u1 > u0 && v1 > v0)
                {
                    // u0,v0..u1,v1 are already bottom-left normalized UV coordinates in Unity space.
                    // Inset by 1px to guarantee zero bilinear bleed from neighboring cells.
                    float rx = Mathf.Clamp(u0 * texW + 1f, 0f, texW - 2f);
                    float ry = Mathf.Clamp(v0 * texH + 1f, 0f, texH - 2f);
                    float rw = Mathf.Clamp((u1 - u0) * texW - 2f, 2f, texW - rx);
                    float rh = Mathf.Clamp((v1 - v0) * texH - 2f, 2f, texH - ry);

                    Sprite sp = Sprite.Create(
                        IconAtlasTexture,
                        new Rect(rx, ry, rw, rh),
                        new Vector2(0.5f, 0.5f),
                        100f,
                        0u,
                        SpriteMeshType.FullRect);
                    sp.name = key;
                    _iconSprites[key] = sp;
                }

                pos = objClose + 1;
            }
        }

        private static float ExtractJsonFloat(string segment, string fieldToken, float fallback)
        {
            int idx = segment.IndexOf(fieldToken, StringComparison.Ordinal);
            if (idx < 0) return fallback;
            int colon = segment.IndexOf(':', idx + fieldToken.Length);
            if (colon < 0) return fallback;

            int s = colon + 1;
            while (s < segment.Length && (segment[s] == ' ' || segment[s] == '\t' || segment[s] == '\r' || segment[s] == '\n'))
            {
                s++;
            }

            int e = s;
            while (e < segment.Length)
            {
                char c = segment[e];
                if ((c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E')
                {
                    e++;
                }
                else
                {
                    break;
                }
            }

            if (e <= s) return fallback;
            if (float.TryParse(segment.Substring(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }
            return fallback;
        }

        private Sprite GetOrCreateFallbackSprite()
        {
            if (_fallbackSprite != null) return _fallbackSprite;

            if (_iconSprites != null && _iconSprites.TryGetValue("icon_stat_damage", out Sprite dmg) && dmg != null)
            {
                _fallbackSprite = dmg;
                return _fallbackSprite;
            }

            Texture2D white = Texture2D.whiteTexture;
            _fallbackSprite = Sprite.Create(
                white,
                new Rect(0f, 0f, white.width, white.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _fallbackSprite.name = "icon_fallback";
            return _fallbackSprite;
        }
    }
}
