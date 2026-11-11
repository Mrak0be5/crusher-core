using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Crusher.Config;
using Crusher.Sim;

namespace Crusher.Core
{
    public struct OfflineProgressSummary
    {
        public double ElapsedSeconds;
        public int LabsCompleted;
    }

    /// <summary>
    /// Reflection-free, IL2CPP-safe atomic JSON persistence (save.json.tmp -> save.json)
    /// and UTC offline timer progression for Labs.
    /// </summary>
    public static class SaveSystem
    {
        public const int SAVE_VERSION = 1;
        public const double MAX_OFFLINE_SECONDS = 7.0 * 86400.0;

        public static void SaveAtomic(GameSession session, string saveFilePath, DateTimeOffset? nowUtcOverride = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(saveFilePath)) throw new ArgumentException("Invalid save path", nameof(saveFilePath));

            string dir = Path.GetDirectoryName(saveFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            long utcUnix = (nowUtcOverride ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            string json = SerializeToJson(session, utcUnix);

            string tmpPath = saveFilePath + ".tmp";
            File.WriteAllText(tmpPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Atomic replace: File.Move with overwrite in .NET Standard 2.1 / .NET 6+, fallback to Copy+Delete
            try
            {
                if (File.Exists(saveFilePath))
                {
                    File.Delete(saveFilePath);
                }
                File.Move(tmpPath, saveFilePath);
            }
            catch
            {
                File.Copy(tmpPath, saveFilePath, overwrite: true);
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        public static bool TryLoadAndApplyOffline(
            GameSession session,
            string saveFilePath,
            out OfflineProgressSummary offlineSummary,
            DateTimeOffset? nowUtcOverride = null)
        {
            offlineSummary = default;
            if (!File.Exists(saveFilePath))
                return false;

            string json = File.ReadAllText(saveFilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            if (!TryDeserializeFromJson(json, session, out long savedUtcUnix))
                return false;

            long nowUnix = (nowUtcOverride ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            if (savedUtcUnix > 0 && nowUnix > savedUtcUnix)
            {
                double delta = Math.Min(MAX_OFFLINE_SECONDS, (double)(nowUnix - savedUtcUnix));
                int completed = session.TickLabsRealTime((float)delta);
                offlineSummary = new OfflineProgressSummary
                {
                    ElapsedSeconds = delta,
                    LabsCompleted = completed
                };
            }
            return true;
        }

        public static string SerializeToJson(GameSession s, long savedUtcUnix)
        {
            var sb = new StringBuilder(2048);
            var ic = CultureInfo.InvariantCulture;

            sb.Append("{\n");
            AppendKV(sb, "version", SAVE_VERSION.ToString(ic));
            AppendKV(sb, "savedUtcUnix", savedUtcUnix.ToString(ic));
            AppendKV(sb, "coins", s.Coins.ToString("R", ic));
            AppendKV(sb, "lifetimeCoins", s.LifetimeCoins.ToString("R", ic));
            AppendKV(sb, "gems", s.Gems.ToString(ic));
            AppendKV(sb, "stones", s.Stones.ToString(ic));
            AppendKV(sb, "eliteCells", s.EliteCells.ToString(ic));

            AppendKV(sb, "bestWaveT1", s.BestWaveT1.ToString(ic));
            AppendKV(sb, "bestWaveT2", s.BestWaveT2.ToString(ic));
            AppendKV(sb, "activeTier", s.ActiveTier.ToString(ic));
            AppendKV(sb, "totalRunsStarted", s.TotalRunsStarted.ToString(ic));
            AppendKV(sb, "totalRunsCompleted", s.TotalRunsCompleted.ToString(ic));
            AppendKV(sb, "totalPlayRealSeconds", s.TotalPlayRealSeconds.ToString("R", ic));

            AppendKV(sb, "speed15Unlocked", BoolStr(s.Speed15Unlocked));
            AppendKV(sb, "workshopUnlocked", BoolStr(s.WorkshopUnlocked));
            AppendKV(sb, "labsUnlocked", BoolStr(s.LabsUnlocked));
            AppendKV(sb, "cardsUnlocked", BoolStr(s.CardsUnlocked));
            AppendKV(sb, "uwSystemUnlocked", BoolStr(s.UwSystemUnlocked));
            AppendKV(sb, "goldenBotUnlocked", BoolStr(s.GoldenBotUnlocked));
            AppendKV(sb, "moltenCoreSkinUnlocked", BoolStr(s.MoltenCoreSkinUnlocked));
            AppendKV(sb, "moltenCoreSkinEquipped", BoolStr(s.MoltenCoreSkinEquipped));
            AppendKV(sb, "tier2Unlocked", BoolStr(s.Tier2Unlocked));
            AppendKV(sb, "ftueDefenseArrowCompleted", BoolStr(s.FtueDefenseArrowCompleted));
            AppendKV(sb, "selectedGameSpeed", s.SelectedGameSpeed.ToString("R", ic));

            AppendKV(sb, "unlockedCardSlots", s.UnlockedCardSlots.ToString(ic));
            AppendKV(sb, "totalCardPulls", s.TotalCardPulls.ToString(ic));
            AppendKV(sb, "unlockedLabSlots", s.UnlockedLabSlots.ToString(ic));
            AppendKV(sb, "uwUnlockedCount", s.UwUnlockedCount.ToString(ic));

            AppendIntArray(sb, "workshopLevels", s.WorkshopLevels);
            AppendBoolArray(sb, "unlockGroups", s.UnlockGroups);
            AppendIntArray(sb, "cardCopies", s.CardCopies);
            AppendIntArray(sb, "equippedCards", s.EquippedCards);
            AppendIntArray(sb, "labLevels", s.LabLevels);
            AppendBoolArray(sb, "uwUnlocked", s.UwUnlocked);
            AppendIntArray(sb, "uwLevels", s.UwLevels);
            AppendBoolArray(sb, "claimedMilestonesT1", s.ClaimedMilestonesT1);

            // Lab slots flattened as arrays for zero-reflection parsing
            int slotLen = s.LabSlots.Length;
            bool[] lsActive = new bool[slotLen];
            int[] lsLab = new int[slotLen];
            float[] lsRem = new float[slotLen];
            float[] lsTot = new float[slotLen];
            float[] lsBoost = new float[slotLen];
            float[] lsBoostRem = new float[slotLen];
            for (int i = 0; i < slotLen; i++)
            {
                lsActive[i] = s.LabSlots[i].Active;
                lsLab[i] = (int)s.LabSlots[i].Lab;
                lsRem[i] = s.LabSlots[i].RemainingSeconds;
                lsTot[i] = s.LabSlots[i].TotalSeconds;
                lsBoost[i] = s.LabSlots[i].BoostMult > 0f ? s.LabSlots[i].BoostMult : 1.0f;
                lsBoostRem[i] = s.LabSlots[i].BoostRemainingSeconds;
            }
            AppendBoolArray(sb, "labSlotActive", lsActive);
            AppendIntArray(sb, "labSlotId", lsLab);
            AppendFloatArray(sb, "labSlotRem", lsRem);
            AppendFloatArray(sb, "labSlotTot", lsTot);
            AppendFloatArray(sb, "labSlotBoost", lsBoost);
            AppendFloatArray(sb, "labSlotBoostRem", lsBoostRem, trailingComma: false);

            sb.Append("}\n");
            return sb.ToString();
        }

        public static bool TryDeserializeFromJson(string json, GameSession s, out long savedUtcUnix)
        {
            savedUtcUnix = 0;
            if (string.IsNullOrWhiteSpace(json)) return false;

            var map = ParseFlatJsonObject(json);
            if (map.Count == 0 || !map.ContainsKey("version")) return false;

            var ic = CultureInfo.InvariantCulture;
            savedUtcUnix = GetLong(map, "savedUtcUnix", 0L);

            s.Coins = GetDouble(map, "coins", 0.0);
            s.LifetimeCoins = GetDouble(map, "lifetimeCoins", 0.0);
            s.Gems = GetInt(map, "gems", 0);
            s.Stones = GetInt(map, "stones", 0);
            s.EliteCells = GetInt(map, "eliteCells", 0);

            s.BestWaveT1 = GetInt(map, "bestWaveT1", 0);
            s.BestWaveT2 = GetInt(map, "bestWaveT2", 0);
            s.ActiveTier = GetInt(map, "activeTier", 1);
            s.TotalRunsStarted = GetInt(map, "totalRunsStarted", 0);
            s.TotalRunsCompleted = GetInt(map, "totalRunsCompleted", 0);
            s.TotalPlayRealSeconds = GetDouble(map, "totalPlayRealSeconds", 0.0);

            s.Speed15Unlocked = GetBool(map, "speed15Unlocked", false);
            s.WorkshopUnlocked = GetBool(map, "workshopUnlocked", false);
            s.LabsUnlocked = GetBool(map, "labsUnlocked", false);
            s.CardsUnlocked = GetBool(map, "cardsUnlocked", false);
            s.UwSystemUnlocked = GetBool(map, "uwSystemUnlocked", false);
            s.GoldenBotUnlocked = GetBool(map, "goldenBotUnlocked", false);
            s.MoltenCoreSkinUnlocked = GetBool(map, "moltenCoreSkinUnlocked", false);
            s.MoltenCoreSkinEquipped = GetBool(map, "moltenCoreSkinEquipped", false);
            s.Tier2Unlocked = GetBool(map, "tier2Unlocked", false);
            s.FtueDefenseArrowCompleted = GetBool(map, "ftueDefenseArrowCompleted", false);
            s.SelectedGameSpeed = (float)GetDouble(map, "selectedGameSpeed", 1.0);

            s.UnlockedCardSlots = Math.Clamp(GetInt(map, "unlockedCardSlots", 1), 1, BalanceTables.MAX_CARD_SLOTS);
            s.TotalCardPulls = GetInt(map, "totalCardPulls", 0);
            s.UnlockedLabSlots = Math.Clamp(GetInt(map, "unlockedLabSlots", 1), 1, BalanceTables.MAX_LAB_SLOTS);
            s.UwUnlockedCount = Math.Clamp(GetInt(map, "uwUnlockedCount", 0), 0, GameIdsAndScale.UW_COUNT);

            ReadIntArray(map, "workshopLevels", s.WorkshopLevels);
            ReadBoolArray(map, "unlockGroups", s.UnlockGroups);
            s.UnlockGroups[(int)UnlockGroup.AlwaysOpen] = true;
            ReadIntArray(map, "cardCopies", s.CardCopies);
            ReadIntArray(map, "equippedCards", s.EquippedCards);
            ReadIntArray(map, "labLevels", s.LabLevels);
            ReadBoolArray(map, "uwUnlocked", s.UwUnlocked);
            ReadIntArray(map, "uwLevels", s.UwLevels);
            ReadBoolArray(map, "claimedMilestonesT1", s.ClaimedMilestonesT1);

            int slotLen = s.LabSlots.Length;
            bool[] lsActive = new bool[slotLen];
            int[] lsLab = new int[slotLen];
            float[] lsRem = new float[slotLen];
            float[] lsTot = new float[slotLen];
            float[] lsBoost = new float[slotLen];
            float[] lsBoostRem = new float[slotLen];

            ReadBoolArray(map, "labSlotActive", lsActive);
            ReadIntArray(map, "labSlotId", lsLab);
            ReadFloatArray(map, "labSlotRem", lsRem);
            ReadFloatArray(map, "labSlotTot", lsTot);
            ReadFloatArray(map, "labSlotBoost", lsBoost);
            ReadFloatArray(map, "labSlotBoostRem", lsBoostRem);

            for (int i = 0; i < slotLen; i++)
            {
                s.LabSlots[i] = new LabSlotState
                {
                    Active = lsActive[i],
                    Lab = (LabId)Math.Clamp(lsLab[i], 0, GameIdsAndScale.LAB_COUNT - 1),
                    RemainingSeconds = lsRem[i],
                    TotalSeconds = lsTot[i],
                    BoostMult = lsBoost[i] >= 1.0f ? lsBoost[i] : 1.0f,
                    BoostRemainingSeconds = lsBoostRem[i]
                };
            }

            return true;
        }

        // --- Minimal Flat JSON Helpers (Zero Reflection) ---

        private static string BoolStr(bool v) => v ? "true" : "false";

        private static void AppendKV(StringBuilder sb, string key, string rawVal, bool trailingComma = true)
        {
            sb.Append("  \"").Append(key).Append("\": ").Append(rawVal);
            if (trailingComma) sb.Append(',');
            sb.Append('\n');
        }

        private static void AppendIntArray(StringBuilder sb, string key, int[] arr, bool trailingComma = true)
        {
            sb.Append("  \"").Append(key).Append("\": [");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(arr[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            if (trailingComma) sb.Append(',');
            sb.Append('\n');
        }

        private static void AppendBoolArray(StringBuilder sb, string key, bool[] arr, bool trailingComma = true)
        {
            sb.Append("  \"").Append(key).Append("\": [");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(arr[i] ? "true" : "false");
            }
            sb.Append(']');
            if (trailingComma) sb.Append(',');
            sb.Append('\n');
        }

        private static void AppendFloatArray(StringBuilder sb, string key, float[] arr, bool trailingComma = true)
        {
            sb.Append("  \"").Append(key).Append("\": [");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(arr[i].ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            if (trailingComma) sb.Append(',');
            sb.Append('\n');
        }

        private static Dictionary<string, string> ParseFlatJsonObject(string json)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            int n = json.Length;

            while (i < n)
            {
                int q1 = json.IndexOf('"', i);
                if (q1 < 0) break;
                int q2 = json.IndexOf('"', q1 + 1);
                if (q2 < 0) break;

                string key = json.Substring(q1 + 1, q2 - q1 - 1);
                int colon = json.IndexOf(':', q2 + 1);
                if (colon < 0) break;

                int valStart = colon + 1;
                while (valStart < n && char.IsWhiteSpace(json[valStart])) valStart++;
                if (valStart >= n) break;

                int valEnd;
                if (json[valStart] == '[')
                {
                    int closeBracket = json.IndexOf(']', valStart + 1);
                    if (closeBracket < 0) break;
                    valEnd = closeBracket + 1;
                    dict[key] = json.Substring(valStart + 1, closeBracket - valStart - 1);
                }
                else if (json[valStart] == '"')
                {
                    int closeQuote = json.IndexOf('"', valStart + 1);
                    if (closeQuote < 0) break;
                    valEnd = closeQuote + 1;
                    dict[key] = json.Substring(valStart + 1, closeQuote - valStart - 1);
                }
                else
                {
                    valEnd = valStart;
                    while (valEnd < n && json[valEnd] != ',' && json[valEnd] != '}' && !char.IsWhiteSpace(json[valEnd]))
                        valEnd++;
                    dict[key] = json.Substring(valStart, valEnd - valStart);
                }

                i = valEnd + 1;
            }
            return dict;
        }

        private static int GetInt(Dictionary<string, string> m, string k, int def) =>
            m.TryGetValue(k, out string v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;

        private static long GetLong(Dictionary<string, string> m, string k, long def) =>
            m.TryGetValue(k, out string v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) ? r : def;

        private static double GetDouble(Dictionary<string, string> m, string k, double def) =>
            m.TryGetValue(k, out string v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : def;

        private static bool GetBool(Dictionary<string, string> m, string k, bool def) =>
            m.TryGetValue(k, out string v) ? string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) : def;

        private static void ReadIntArray(Dictionary<string, string> m, string k, int[] dst)
        {
            if (!m.TryGetValue(k, out string raw) || string.IsNullOrWhiteSpace(raw)) return;
            string[] parts = raw.Split(',');
            int lim = Math.Min(dst.Length, parts.Length);
            for (int i = 0; i < lim; i++)
            {
                if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    dst[i] = v;
            }
        }

        private static void ReadFloatArray(Dictionary<string, string> m, string k, float[] dst)
        {
            if (!m.TryGetValue(k, out string raw) || string.IsNullOrWhiteSpace(raw)) return;
            string[] parts = raw.Split(',');
            int lim = Math.Min(dst.Length, parts.Length);
            for (int i = 0; i < lim; i++)
            {
                if (float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    dst[i] = v;
            }
        }

        private static void ReadBoolArray(Dictionary<string, string> m, string k, bool[] dst)
        {
            if (!m.TryGetValue(k, out string raw) || string.IsNullOrWhiteSpace(raw)) return;
            string[] parts = raw.Split(',');
            int lim = Math.Min(dst.Length, parts.Length);
            for (int i = 0; i < lim; i++)
            {
                dst[i] = string.Equals(parts[i].Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
