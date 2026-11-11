using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;

namespace Crusher.HeadlessSim
{
    public enum BotProfile
    {
        ShredderOptimal = 0,
        CasualMixed = 1,
        NaiveDamage = 2
    }

    public sealed class SeedTelemetry
    {
        public BotProfile Profile;
        public int Seed;
        public int Run1Wave;
        public double Run1DurationSec;
        public int FinalBestWaveT1;
        public bool Tier2Unlocked;
        public int TotalRuns;
        public double TimeW30Min = double.PositiveInfinity;
        public double TimeW50Min = double.PositiveInfinity;
        public double TimeW100Min = double.PositiveInfinity;
        public bool HadStagnationSwamp;
        public double AvgUnlockGapMin;
        public double MaxUnlockGapMin45m;
        public double Phase4GtBhCoinShare;
        public int PeakActiveEnemies;
        public double LifetimeCoins;
        public int FinalWorkshopTotalLevels;
        public int FinalLabsTotalLevels;
    }

    public static class Program
    {
        private const int SEEDS_PER_PROFILE = 100;
        private const float TOTAL_SESSION_SECONDS = 3600.0f;
        private const float SIM_STEP_REAL_SEC = 0.10f; // 10 Hz real-time driver (CombatSim sub-steps at <= 0.0625s game time!)
        private const float HANGAR_PAUSE_REAL_SEC = 14.0f;

        public static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Stopwatch sw = Stopwatch.StartNew();

            Console.WriteLine("=== CRUSHER CORE V1: HEADLESS C# VERIFIER (STOP-GATE 1A) ===");

            // 1. Unit tests on SaveSystem, Offline Lab Timers, Deterministic Gacha, UW Pool & Core Grades
            RunCoreUnitTests();
            Console.WriteLine("[PASS] Unit Tests: SaveSystem atomic .tmp->save.json, UTC offline labs, FTUE 3-card seed, UW pool, Core G0-G3.");

            // 2. Run 3 profiles x 100 seeds x 3600s in parallel across CPU cores
            var allResults = new SeedTelemetry[3 * SEEDS_PER_PROFILE];
            Parallel.For(0, 3 * SEEDS_PER_PROFILE, idx =>
            {
                BotProfile profile = (BotProfile)(idx / SEEDS_PER_PROFILE);
                int seed = (idx % SEEDS_PER_PROFILE) + 1;
                allResults[idx] = SimulateSingleSeed(profile, seed);
            });

            sw.Stop();
            Console.WriteLine($"Simulated {allResults.Length} full 60-min sessions in {sw.Elapsed.TotalSeconds:F2}s.");
            Console.WriteLine();

            var opt = allResults.Where(r => r.Profile == BotProfile.ShredderOptimal).ToArray();
            var cas = allResults.Where(r => r.Profile == BotProfile.CasualMixed).ToArray();
            var nai = allResults.Where(r => r.Profile == BotProfile.NaiveDamage).ToArray();

            PrintProfileSummary("Shredder-Optimal", opt);
            PrintProfileSummary("Casual-Mixed", cas);
            PrintProfileSummary("Naive-Damage", nai);

            // 3. Evaluate all 8 Invariants A1..A8
            bool allPass = EvaluateAndPrintInvariants(opt, cas, nai, allResults, out string jsonReport);

            string outDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".";
            string reportPath = Path.GetFullPath(Path.Combine(outDir, "..", "sim_report.json"));
            File.WriteAllText(reportPath, jsonReport, new UTF8Encoding(false));
            Console.WriteLine($"\nSaved telemetry report -> {reportPath}");

            return allPass ? 0 : 2;
        }

        private static void RunCoreUnitTests()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CrusherCoreSimTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string savePath = Path.Combine(tempDir, "save.json");
                var s1 = new GameSession(999UL);

                // Verify initial grade G0 & 1.25x Run 1 speed
                Assert(s1.GetVisualCoreGrade() == CoreVisualGrade.G0_Rusty, "Initial grade must be G0_Rusty");
                Assert(Math.Abs(s1.EffectiveTimeScale - 1.25f) < 0.001f, "Run 1 initial effective speed must be 1.25x");

                // Unlock Thorns -> G1; Orbs -> G2; UW -> G3
                s1.Coins = 5000;
                Assert(s1.TryUnlockWorkshopBranch(StatId.Thorns), "Unlock Thorns failed");
                Assert(s1.GetVisualCoreGrade() == CoreVisualGrade.G1_SteelCarbide, "Grade after Thorns must be G1");
                Assert(s1.TryUnlockWorkshopBranch(StatId.OrbsCount), "Unlock Orbs failed");
                Assert(s1.GetVisualCoreGrade() == CoreVisualGrade.G2_TitaniumTwin, "Grade after Orbs must be G2");

                // Deterministic first 3 card pulls
                s1.CardsUnlocked = true;
                s1.Gems = 500;
                Assert(s1.TryPullCard(out CardId c0, out _) && c0 == CardId.EnemyBalance, "Pull 0 must be EnemyBalance");
                Assert(s1.TryPullCard(out CardId c1, out _) && c1 == CardId.Coins, "Pull 1 must be Coins");
                Assert(s1.TryPullCard(out CardId c2, out _) && c2 == CardId.PlasmaCannon, "Pull 2 must be PlasmaCannon");

                // UW choices guarantee GT & BH
                s1.UwSystemUnlocked = true;
                s1.Stones = 30;
                Span<UwId> choices = stackalloc UwId[3];
                int nChoices = s1.GetAvailableUwChoices(choices);
                Assert(nChoices == 3, "Must offer 3 UWs initially");
                Assert(s1.TryUnlockUw(UwId.ChainLightning), "Unlock CL");
                Assert(s1.GetVisualCoreGrade() == CoreVisualGrade.G3_DiamondReactor, "Grade after UW1 must be G3");
                nChoices = s1.GetAvailableUwChoices(choices);
                Assert(nChoices == 2, "Must offer remaining 2 UWs (GT + BH)");
                Assert(s1.TryUnlockUw(UwId.GoldenTower), "Unlock GT as UW2");

                // Lab slots + Cell Boost + Atomic Save + Offline UTC progress
                s1.LabsUnlocked = true;
                Assert(s1.TryUnlockLabSlot(), "Unlock Lab Slot 2");
                s1.EliteCells = 10;
                Assert(s1.TryStartLab(LabId.GameSpeed, 0), "Start GameSpeed L1 (45s)");
                Assert(s1.TryBoostLabSlotWithCells(0, boost3x: false), "Boost Slot 0 2x");
                Assert(s1.TryStartLab(LabId.CoinsPerKill, 1), "Start CPK L1 (45s)");

                DateTimeOffset t0 = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
                SaveSystem.SaveAtomic(s1, savePath, t0);
                Assert(File.Exists(savePath), "save.json must exist");
                Assert(!File.Exists(savePath + ".tmp"), "save.json.tmp must be cleaned up");

                var s2 = new GameSession(1UL);
                bool loaded = SaveSystem.TryLoadAndApplyOffline(s2, savePath, out OfflineProgressSummary off, t0.AddSeconds(30));
                Assert(loaded, "Save load failed");
                Assert(Math.Abs(off.ElapsedSeconds - 30.0) < 0.01, "Offline elapsed mismatch");
                Assert(off.LabsCompleted == 1, "Slot 0 (42s at 2x over 30s = 60s) must have completed");
                Assert(s2.LabLevels[(int)LabId.GameSpeed] == 1, "GameSpeed must be L1");
                Assert(Math.Abs(s2.SelectedGameSpeed - 2.7f) < 0.01f, "SelectedGameSpeed must auto-step to 2.7x");
                Assert(s2.LabSlots[1].Active && Math.Abs(s2.LabSlots[1].RemainingSeconds - 15.0f) < 0.1f, "Slot 1 must have 15s left");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        private static SeedTelemetry SimulateSingleSeed(BotProfile profile, int seed)
        {
            var tel = new SeedTelemetry { Profile = profile, Seed = seed };
            var session = new GameSession((ulong)(seed * 7919 + (int)profile * 104729 + 17));
            var botRng = new FastRng((ulong)(seed * 31337 + 99));

            var unlockTimestampsMin = new List<double> { 0.0 };
            int prevUnlockedBranches = CountUnlockedBranches(session);
            int prevCardPulls = 0;
            int prevCardSlots = 1;
            int prevLabSlots = 1;
            int prevLabsDone = 0;
            int prevUwCount = 0;
            int prevMilestoneSystems = 0;

            var runBestHistory = new List<int>();
            var runCpmHistory = new List<double>();
            double phase4CoinsTotal = 0.0;
            double phase4CoinsGtBh = 0.0;

            float elapsedReal = 0f;
            float buyDecisionTimer = 0f;
            float runStartReal = 0f;

            // Start Run 1 immediately at 00:00 (0 menus!)
            session.StartNewRun(1);

            while (elapsedReal < TOTAL_SESSION_SECONDS)
            {
                if (session.InRun)
                {
                    int prevBest = session.BestWaveT1;
                    double runCoinsBeforePossibleEnd = session.Combat.RunCoinsEarned;
                    double runGtBhBeforePossibleEnd = session.Combat.RunCoinsEarnedDuringGtBh;
                    int runWaveBeforeTick = session.Combat.CurrentWave;
                    int runPeakBeforeTick = session.Combat.PeakActiveEnemies;

                    session.Tick(SIM_STEP_REAL_SEC);
                    elapsedReal += SIM_STEP_REAL_SEC;
                    buyDecisionTimer += SIM_STEP_REAL_SEC;

                    if (session.Combat.PeakActiveEnemies > tel.PeakActiveEnemies)
                        tel.PeakActiveEnemies = session.Combat.PeakActiveEnemies;
                    if (runPeakBeforeTick > tel.PeakActiveEnemies)
                        tel.PeakActiveEnemies = runPeakBeforeTick;

                    double nowMin = elapsedReal / 60.0;
                    if (session.BestWaveT1 >= 30 && double.IsPositiveInfinity(tel.TimeW30Min)) tel.TimeW30Min = nowMin;
                    if (session.BestWaveT1 >= 50 && double.IsPositiveInfinity(tel.TimeW50Min)) tel.TimeW50Min = nowMin;
                    if (session.BestWaveT1 >= 100 && double.IsPositiveInfinity(tel.TimeW100Min)) tel.TimeW100Min = nowMin;

                    // Gem drone tap check
                    if (session.Combat.GemDrone.Active)
                    {
                        float tapProb = profile == BotProfile.ShredderOptimal ? 0.90f : (profile == BotProfile.CasualMixed ? 0.70f : 0.55f);
                        if (botRng.NextFloat() < tapProb * SIM_STEP_REAL_SEC * 0.5f)
                        {
                            session.ClaimActiveGemDrone();
                        }
                    }

                    // Periodic in-run Cash upgrades & background Lab re-queueing (every 1.0s real time)
                    if (buyDecisionTimer >= 1.0f && session.InRun)
                    {
                        buyDecisionTimer = 0f;
                        BotSpendInRunCash(session, profile, ref botRng);
                        BotManageLabsDuringSession(session, profile, ref botRng);
                        RecordAnyStructuralUnlocks(session, nowMin, unlockTimestampsMin,
                            ref prevUnlockedBranches, ref prevCardPulls, ref prevCardSlots, ref prevLabSlots, ref prevLabsDone, ref prevUwCount, ref prevMilestoneSystems);
                    }

                    // Did the run just end?
                    if (!session.InRun)
                    {
                        double runDurSec = Math.Max(1.0, elapsedReal - runStartReal);
                        int endedWave = Math.Max(runWaveBeforeTick, session.Combat.CurrentWave);

                        if (session.TotalRunsCompleted == 1)
                        {
                            tel.Run1Wave = endedWave;
                            tel.Run1DurationSec = runDurSec;
                        }

                        if (elapsedReal >= 35.0f * 60.0f && session.UwUnlocked[(int)UwId.GoldenTower] && session.UwUnlocked[(int)UwId.BlackHole])
                        {
                            phase4CoinsTotal += runCoinsBeforePossibleEnd;
                            phase4CoinsGtBh += runGtBhBeforePossibleEnd;
                        }

                        double cpm = runCoinsBeforePossibleEnd / (runDurSec / 60.0);
                        runBestHistory.Add(session.BestWaveT1);
                        runCpmHistory.Add(cpm);

                        // Hangar intermission (spend Coins, Gems, Stones, start Labs)
                        float baseHangar = profile == BotProfile.ShredderOptimal ? 12.0f
                            : (profile == BotProfile.CasualMixed ? (session.BestWaveT1 >= 30 ? 32.0f : 26.0f) : 18.0f);
                        float hangarStep = MathF.Min(baseHangar, TOTAL_SESSION_SECONDS - elapsedReal);
                        if (hangarStep > 0f)
                        {
                            session.Tick(hangarStep);
                            elapsedReal += hangarStep;
                        }

                        BotHangarDecisions(session, profile, ref botRng);
                        RecordAnyStructuralUnlocks(session, elapsedReal / 60.0, unlockTimestampsMin,
                            ref prevUnlockedBranches, ref prevCardPulls, ref prevCardSlots, ref prevLabSlots, ref prevLabsDone, ref prevUwCount, ref prevMilestoneSystems);

                        if (elapsedReal < TOTAL_SESSION_SECONDS)
                        {
                            runStartReal = elapsedReal;
                            // Keep farming Tier 1 up to W105 so T1 W100 milestone is cleanly secured; only try T1 in 60m test
                            session.StartNewRun(1);
                        }
                    }
                    else if (prevBest != session.BestWaveT1)
                    {
                        RecordAnyStructuralUnlocks(session, nowMin, unlockTimestampsMin,
                            ref prevUnlockedBranches, ref prevCardPulls, ref prevCardSlots, ref prevLabSlots, ref prevLabsDone, ref prevUwCount, ref prevMilestoneSystems);
                    }
                }
            }

            // If final run is still active at 60:00 and in Phase 4 with GT+BH, include its coin telemetry
            if (session.InRun && session.UwUnlocked[(int)UwId.GoldenTower] && session.UwUnlocked[(int)UwId.BlackHole])
            {
                phase4CoinsTotal += session.Combat.RunCoinsEarned;
                phase4CoinsGtBh += session.Combat.RunCoinsEarnedDuringGtBh;
            }

            tel.FinalBestWaveT1 = session.BestWaveT1;
            tel.Tier2Unlocked = session.Tier2Unlocked;
            tel.TotalRuns = session.TotalRunsStarted;
            tel.LifetimeCoins = session.LifetimeCoins;
            tel.FinalWorkshopTotalLevels = session.WorkshopLevels.Sum();
            tel.FinalLabsTotalLevels = session.LabLevels.Sum();
            tel.Phase4GtBhCoinShare = phase4CoinsTotal > 1.0 ? (phase4CoinsGtBh / phase4CoinsTotal) : 0.0;

            // Check A2 stagnation swamp (3 consecutive runs while bestWave < 85 where wave gain < 6 AND cpm gain < 1.35x)
            for (int r = 3; r < runBestHistory.Count; r++)
            {
                if (runBestHistory[r - 3] >= 85) break;
                int waveGain = runBestHistory[r] - runBestHistory[r - 3];
                double baseCpm = Math.Max(1.0, runCpmHistory[r - 3]);
                double maxRecentCpm = Math.Max(runCpmHistory[r], Math.Max(runCpmHistory[r - 1], runCpmHistory[r - 2]));
                if (waveGain < 6 && (maxRecentCpm / baseCpm) < 1.35)
                {
                    tel.HadStagnationSwamp = true;
                    break;
                }
            }

            // Unlock gap telemetry
            unlockTimestampsMin.Sort();
            var dedup = new List<double> { 0.0 };
            foreach (double t in unlockTimestampsMin)
            {
                if (t - dedup[dedup.Count - 1] >= 0.25) dedup.Add(t);
            }
            double maxGap45 = 0.0;
            double sumGaps = 0.0;
            int gapCnt = 0;
            for (int i = 1; i < dedup.Count; i++)
            {
                double g = dedup[i] - dedup[i - 1];
                sumGaps += g;
                gapCnt++;
                if (dedup[i] <= 45.0 && g > maxGap45) maxGap45 = g;
            }
            tel.AvgUnlockGapMin = gapCnt > 0 ? sumGaps / gapCnt : 60.0;
            tel.MaxUnlockGapMin45m = maxGap45;

            return tel;
        }

        private static int CountUnlockedBranches(GameSession s)
        {
            int c = 0;
            for (int i = 0; i < s.UnlockGroups.Length; i++) if (s.UnlockGroups[i]) c++;
            return c;
        }

        private static int CountMilestoneSystemUnlocks(GameSession s)
        {
            int c = 0;
            for (int i = 0; i < BalanceTables.Tier1Milestones.Length; i++)
            {
                if (s.ClaimedMilestonesT1[i] && BalanceTables.Tier1Milestones[i].UnlockFlags != MilestoneUnlockFlags.None)
                    c++;
            }
            return c;
        }

        private static void RecordAnyStructuralUnlocks(
            GameSession s, double nowMin, List<double> stamps,
            ref int prevBranches, ref int prevPulls, ref int prevCSlots, ref int prevLSlots, ref int prevLabs, ref int prevUws, ref int prevMsSys)
        {
            int b = CountUnlockedBranches(s);
            int pulls = s.TotalCardPulls;
            int cs = s.UnlockedCardSlots;
            int ls = s.UnlockedLabSlots;
            int labs = s.LabLevels.Sum();
            int uws = s.UwUnlockedCount;
            int msSys = CountMilestoneSystemUnlocks(s);

            if (b > prevBranches || pulls > prevPulls || cs > prevCSlots || ls > prevLSlots || labs > prevLabs || uws > prevUws || msSys > prevMsSys)
            {
                stamps.Add(nowMin);
                prevBranches = b;
                prevPulls = pulls;
                prevCSlots = cs;
                prevLSlots = ls;
                prevLabs = labs;
                prevUws = uws;
                prevMsSys = msSys;
            }
        }

        private static double GetStatWeight(BotProfile profile, StatId id, GameSession s, bool inRun)
        {
            int totalLv = s.GetTotalStatLevel(id);
            if (profile == BotProfile.ShredderOptimal)
            {
                switch (id)
                {
                    case StatId.Thorns: return totalLv < 25 ? 4.0 : (totalLv < 60 ? 2.2 : 1.2);
                    case StatId.DefenseAbsolute: return 3.2;
                    case StatId.DefensePercent: return 2.6;
                    case StatId.Health: return 2.3;
                    case StatId.OrbsCount: return 5.0;
                    case StatId.OrbSpeed: return 2.4;
                    case StatId.KnockbackChance: return 2.2;
                    case StatId.KnockbackForce: return 1.8;
                    case StatId.CoinsPerKill: return inRun ? 2.2 : 3.4;
                    case StatId.CoinsPerWave: return inRun ? 1.2 : 1.8;
                    case StatId.CashBonus: return inRun ? 2.4 : 1.8;
                    case StatId.CashPerWave: return inRun && s.Combat.CurrentWave < 25 ? 1.6 : 0.6;
                    case StatId.FreeUpgrades: return 2.2;
                    case StatId.AttackSpeed: return 2.0;
                    case StatId.MultishotChance: return 1.9;
                    case StatId.MultishotTargets: return 2.5;
                    case StatId.Damage: return 1.5;
                    case StatId.Lifesteal: return 1.3;
                    case StatId.CritChance: return 1.0;
                    case StatId.CritFactor: return 1.0;
                    case StatId.HealthRegen: return 0.8;
                    case StatId.Range: return totalLv < 8 ? 0.9 : 0.1;
                    default: return 1.0;
                }
            }
            else if (profile == BotProfile.CasualMixed)
            {
                switch (id)
                {
                    case StatId.OrbsCount: return s.GetTotalStatLevel(id) < 2 ? 0.95 : 0.45;
                    case StatId.Thorns: return totalLv < 22 ? 0.95 : 0.60;
                    case StatId.DefenseAbsolute: return 0.58;
                    case StatId.DefensePercent: return 0.68;
                    case StatId.Health: return 1.35;
                    case StatId.Damage: return 1.70;
                    case StatId.AttackSpeed: return 1.60;
                    case StatId.CoinsPerKill: return 1.05;
                    case StatId.CashBonus: return 1.05;
                    case StatId.FreeUpgrades: return 0.50;
                    case StatId.Range: return totalLv < 22 ? 1.40 : 0.65;
                    default: return 1.00;
                }
            }
            else // NaiveDamage: ignores DefAbs/DefPct, pumps Damage + AttackSpeed + Crit + HP + Lifesteal
            {
                switch (id)
                {
                    case StatId.Damage: return 3.0;
                    case StatId.AttackSpeed: return 2.6;
                    case StatId.MultishotChance: return 2.0;
                    case StatId.MultishotTargets: return 2.4;
                    case StatId.CritChance: return 1.8;
                    case StatId.CritFactor: return 1.8;
                    case StatId.Health: return 2.1;
                    case StatId.Lifesteal: return 1.8;
                    case StatId.CashBonus: return 1.6;
                    case StatId.CoinsPerKill: return 1.6;
                    case StatId.FreeUpgrades: return 1.3;
                    case StatId.Thorns: return totalLv < 10 ? 0.9 : 0.25; // touched once via FTUE arrow, then neglected
                    case StatId.DefenseAbsolute: return 0.0;              // completely ignored by Naive-Damage!
                    case StatId.DefensePercent: return 0.15;
                    case StatId.OrbsCount: return 0.8;
                    case StatId.OrbSpeed: return 0.5;
                    case StatId.KnockbackChance: return 0.8;
                    case StatId.KnockbackForce: return 0.6;
                    case StatId.Range: return totalLv < 12 ? 1.1 : 0.3;
                    default: return 0.6;
                }
            }
        }

        private static void BotSpendInRunCash(GameSession s, BotProfile profile, ref FastRng rng)
        {
            for (int iter = 0; iter < 12; iter++)
            {
                int bestStat = -1;
                double bestScore = double.MaxValue;

                for (int i = 0; i < GameIdsAndScale.STAT_COUNT; i++)
                {
                    StatId id = (StatId)i;
                    if (!s.IsStatUnlocked(id)) continue;
                    int lv = s.GetTotalStatLevel(id);
                    if (lv >= BalanceTables.Stats[i].MaxLevel) continue;

                    double wt = GetStatWeight(profile, id, s, inRun: true);
                    if (wt <= 0.001) continue;
                    if (profile == BotProfile.CasualMixed)
                    {
                        wt *= (0.70 + 0.60 * rng.NextFloat());
                    }

                    double cost = BalanceTables.GetCashCost(id, lv);
                    if (cost > s.RunCash) continue;

                    double score = cost / wt;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestStat = i;
                    }
                }

                if (bestStat < 0) break;
                if (!s.TryBuyRunUpgrade((StatId)bestStat, 1)) break;
            }
        }

        private static void BotHangarDecisions(GameSession s, BotProfile profile, ref FastRng rng)
        {
            // 1. Ultimate Weapons (W30 / W50)
            if (s.UwSystemUnlocked)
            {
                Span<UwId> choices = stackalloc UwId[3];
                while (s.Stones >= s.GetNextUwUnlockStoneCost())
                {
                    int n = s.GetAvailableUwChoices(choices);
                    if (n == 0) break;

                    UwId pick = choices[0];
                    if (profile == BotProfile.ShredderOptimal)
                    {
                        // Phase 3 (W30): pick BlackHole first to combo with Plasma Orbs; Phase 4 (W50): pick GoldenTower for GT+BH overdrive!
                        bool foundBh = false;
                        for (int i = 0; i < n; i++)
                        {
                            if (choices[i] == UwId.BlackHole) { pick = UwId.BlackHole; foundBh = true; break; }
                        }
                        if (!foundBh)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                if (choices[i] == UwId.GoldenTower) { pick = UwId.GoldenTower; break; }
                            }
                        }
                    }
                    else if (profile == BotProfile.CasualMixed)
                    {
                        // W30 (UW #1) is pure random; on W50 (UW #2) casual notices GoldenTower 80% of the time if offered
                        pick = choices[rng.NextRange(0, n)];
                        if (s.UwUnlockedCount >= 1 && rng.NextFloat() < 0.80f)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                if (choices[i] == UwId.GoldenTower) { pick = UwId.GoldenTower; break; }
                            }
                        }
                    }
                    else // NaiveDamage prefers ChainLightning first!
                    {
                        for (int i = 0; i < n; i++)
                        {
                            if (choices[i] == UwId.ChainLightning) { pick = UwId.ChainLightning; break; }
                            if (choices[i] == UwId.GoldenTower) { pick = UwId.GoldenTower; }
                        }
                    }

                    if (!s.TryUnlockUw(pick)) break;
                }

                // Spend leftover Stones on UW upgrades (only after UW #2 is secured, or if >20 Stones spare)
                if (s.UwUnlockedCount >= 2)
                {
                    UwId[] prio = { UwId.GoldenTower, UwId.BlackHole, UwId.ChainLightning };
                    bool upgraded = true;
                    while (upgraded)
                    {
                        upgraded = false;
                        foreach (UwId u in prio)
                        {
                            if (s.UwUnlocked[(int)u] && s.TryUpgradeUw(u)) { upgraded = true; break; }
                        }
                    }
                }
            }

            // 2. Gems: Lab Slots, Card Slots, Card Gacha
            BotSpendGems(s, profile, ref rng);
            BotEquipBestCards(s, profile);

            // 3. Labs & Elite Cells
            BotManageLabsDuringSession(s, profile, ref rng);

            // 4. Workshop Branch Unlocks
            StatId[] unlockOrderOpt =
            {
                StatId.DefenseAbsolute, StatId.Thorns, StatId.DefensePercent, StatId.CoinsPerKill,
                StatId.OrbsCount, StatId.CashPerWave, StatId.MultishotChance, StatId.KnockbackChance,
                StatId.Lifesteal, StatId.FreeUpgrades, StatId.CritChance, StatId.Range
            };
            StatId[] unlockOrderCas =
            {
                StatId.Thorns, StatId.DefenseAbsolute, StatId.CritChance, StatId.Range,
                StatId.CoinsPerKill, StatId.DefensePercent, StatId.MultishotChance, StatId.CashPerWave,
                StatId.KnockbackChance, StatId.OrbsCount, StatId.Lifesteal, StatId.FreeUpgrades
            };
            StatId[] unlockOrderNaive =
            {
                StatId.Thorns, StatId.CritChance, StatId.Range, StatId.CoinsPerKill,
                StatId.MultishotChance, StatId.KnockbackChance, StatId.Lifesteal, StatId.CashPerWave, StatId.DefensePercent
            };

            StatId[] order = profile == BotProfile.ShredderOptimal ? unlockOrderOpt
                : (profile == BotProfile.CasualMixed ? unlockOrderCas : unlockOrderNaive);
            foreach (StatId branch in order)
            {
                s.TryUnlockWorkshopBranch(branch);
            }

            // 5. Workshop Permanent Coin Upgrades (keep small coin reserve for upcoming Labs if LabsUnlocked)
            double labReserve = s.LabsUnlocked ? Math.Min(400.0, s.Coins * 0.20) : 0.0;
            for (int iter = 0; iter < 600; iter++)
            {
                int bestStat = -1;
                double bestScore = double.MaxValue;

                for (int i = 0; i < GameIdsAndScale.STAT_COUNT; i++)
                {
                    StatId id = (StatId)i;
                    if (!s.IsStatUnlocked(id)) continue;
                    int lv = s.WorkshopLevels[i];
                    if (lv >= BalanceTables.Stats[i].MaxLevel) continue;

                    double wt = GetStatWeight(profile, id, s, inRun: false);
                    if (wt <= 0.001) continue;
                    if (profile == BotProfile.CasualMixed)
                    {
                        wt *= (0.70 + 0.60 * rng.NextFloat());
                    }

                    double cost = BalanceTables.GetCoinCost(id, lv);
                    if (cost > s.Coins - labReserve) continue;

                    double score = cost / wt;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestStat = i;
                    }
                }

                if (bestStat < 0) break;
                if (!s.TryBuyWorkshopUpgrade((StatId)bestStat, 1)) break;
            }
        }

        private static void BotSpendGems(GameSession s, BotProfile profile, ref FastRng rng)
        {
            if (profile == BotProfile.CasualMixed)
            {
                for (int guard = 0; guard < 20; guard++)
                {
                    if (s.CardsUnlocked && s.TotalCardPulls < 4 && s.Gems >= BalanceTables.CARD_PULL_COST_GEMS)
                    {
                        s.TryPullCard(out _, out _);
                        continue;
                    }
                    if (s.CardsUnlocked && s.UnlockedCardSlots == 1 && s.Gems >= 40)
                    {
                        s.TryUnlockCardSlot();
                        continue;
                    }
                    if (s.LabsUnlocked && s.UnlockedLabSlots == 1 && s.Gems >= 50)
                    {
                        s.TryUnlockLabSlot();
                        continue;
                    }
                    if (s.CardsUnlocked && s.UnlockedCardSlots == 2 && s.BestWaveT1 >= 50 && s.TotalCardPulls >= 6 && s.Gems >= 80)
                    {
                        s.TryUnlockCardSlot();
                        continue;
                    }
                    if (s.CardsUnlocked && !s.AllCardsMaxed() && s.Gems >= BalanceTables.CARD_PULL_COST_GEMS && rng.NextFloat() < 0.75f)
                    {
                        s.TryPullCard(out _, out _);
                        continue;
                    }
                    break;
                }
                return;
            }

            for (int guard = 0; guard < 30; guard++)
            {
                // First 3 deterministic pulls on W20 take top priority!
                if (s.CardsUnlocked && s.TotalCardPulls < 3 && s.Gems >= BalanceTables.CARD_PULL_COST_GEMS)
                {
                    s.TryPullCard(out _, out _);
                    continue;
                }

                // Lab Slot 2 (50 Gems) right around W30
                if (s.LabsUnlocked && s.UnlockedLabSlots == 1 && s.Gems >= 50)
                {
                    s.TryUnlockLabSlot();
                    continue;
                }

                // Card Slot 2 (40 Gems) & Slot 3 (80 Gems)
                if (s.CardsUnlocked && s.UnlockedCardSlots == 1 && s.Gems >= 40)
                {
                    s.TryUnlockCardSlot();
                    continue;
                }
                if (s.CardsUnlocked && s.UnlockedCardSlots == 2 && s.TotalCardPulls >= 5 && s.Gems >= 80)
                {
                    s.TryUnlockCardSlot();
                    continue;
                }

                // Lab Slot 3 (150 Gems) after W55+
                if (s.LabsUnlocked && s.UnlockedLabSlots == 2 && s.BestWaveT1 >= 50 && s.Gems >= 150)
                {
                    s.TryUnlockLabSlot();
                    continue;
                }

                // Card Slot 4 (140 Gems)
                if (s.CardsUnlocked && s.UnlockedCardSlots == 3 && s.TotalCardPulls >= 8 && s.Gems >= 140)
                {
                    s.TryUnlockCardSlot();
                    continue;
                }

                // Pull cards if we aren't saving specifically for an imminent slot
                bool savingForSlot =
                    (s.LabsUnlocked && s.UnlockedLabSlots == 2 && s.BestWaveT1 >= 50 && s.Gems >= 90 && s.Gems < 150) ||
                    (s.CardsUnlocked && s.UnlockedCardSlots == 2 && s.TotalCardPulls >= 5 && s.Gems >= 40 && s.Gems < 80) ||
                    (s.CardsUnlocked && s.UnlockedCardSlots == 3 && s.TotalCardPulls >= 8 && s.Gems >= 80 && s.Gems < 140);

                if (savingForSlot && profile == BotProfile.ShredderOptimal) break;

                if (s.CardsUnlocked && !s.AllCardsMaxed() && s.Gems >= BalanceTables.CARD_PULL_COST_GEMS)
                {
                    s.TryPullCard(out _, out _);
                    continue;
                }

                break;
            }
        }

        private static void BotEquipBestCards(GameSession s, BotProfile profile)
        {
            if (!s.CardsUnlocked) return;

            CardId[] prio;
            if (profile == BotProfile.ShredderOptimal)
            {
                // Phase 2 (before W30): Coins + PlasmaCannon + Health so 1.8x swarm doesn't delay W30;
                // Phase 3-4 (W30+): engage EnemyBalance for 2.52x Cash + Coins alongside Orbs & BlackHole!
                if (s.BestWaveT1 < 30)
                {
                    prio = new[] { CardId.Coins, CardId.PlasmaCannon, CardId.Health, CardId.ExtraOrb, CardId.WaveAccelerator, CardId.DeathRay, CardId.Damage, CardId.EnemyBalance };
                }
                else if (s.BestWaveT1 < 65)
                {
                    prio = new[] { CardId.EnemyBalance, CardId.Coins, CardId.Health, CardId.PlasmaCannon, CardId.ExtraOrb, CardId.WaveAccelerator, CardId.DeathRay, CardId.Damage };
                }
                else
                {
                    prio = new[] { CardId.EnemyBalance, CardId.Health, CardId.PlasmaCannon, CardId.ExtraOrb, CardId.Coins, CardId.WaveAccelerator, CardId.DeathRay, CardId.Damage };
                }
            }
            else if (profile == BotProfile.CasualMixed)
            {
                // Casual player equips Damage, Coins, Health, DeathRay before EnemyBalance/PlasmaCannon
                prio = new[] { CardId.Damage, CardId.Coins, CardId.Health, CardId.DeathRay, CardId.EnemyBalance, CardId.WaveAccelerator, CardId.ExtraOrb, CardId.PlasmaCannon };
            }
            else
            {
                prio = new[] { CardId.Damage, CardId.Health, CardId.PlasmaCannon, CardId.Coins, CardId.DeathRay, CardId.WaveAccelerator, CardId.EnemyBalance, CardId.ExtraOrb };
            }

            int slot = 0;
            foreach (CardId cid in prio)
            {
                if (slot >= s.UnlockedCardSlots) break;
                if (s.CardCopies[(int)cid] > 0)
                {
                    s.TryEquipCard(cid, slot++);
                }
            }
        }

        private static void BotManageLabsDuringSession(GameSession s, BotProfile profile, ref FastRng rng)
        {
            if (!s.LabsUnlocked) return;

            LabId[] prio = profile == BotProfile.ShredderOptimal
                ? new[] { LabId.GameSpeed, LabId.MagneticGarlic, LabId.BlackHoleCoinBonus, LabId.CoinsPerKill, LabId.CarbideOverdrive, LabId.OrbAndRotorSpeed }
                : (profile == BotProfile.CasualMixed
                    ? new[] { LabId.CarbideOverdrive, LabId.CoinsPerKill, LabId.GameSpeed, LabId.OrbAndRotorSpeed, LabId.MagneticGarlic, LabId.BlackHoleCoinBonus }
                    : new[] { LabId.GameSpeed, LabId.CarbideOverdrive, LabId.CoinsPerKill, LabId.MagneticGarlic, LabId.OrbAndRotorSpeed, LabId.BlackHoleCoinBonus });

            for (int slot = 0; slot < s.UnlockedLabSlots; slot++)
            {
                if (!s.LabSlots[slot].Active)
                {
                    foreach (LabId lid in prio)
                    {
                        if (s.TryStartLab(lid, slot)) break;
                    }
                }

                // Cell boost
                if (s.LabSlots[slot].Active)
                {
                    if (profile == BotProfile.ShredderOptimal)
                    {
                        if (s.EliteCells >= BalanceTables.CELLS_COST_BOOST_3X)
                            s.TryBoostLabSlotWithCells(slot, boost3x: true);
                        else if (s.EliteCells >= BalanceTables.CELLS_COST_BOOST_2X)
                            s.TryBoostLabSlotWithCells(slot, boost3x: false);
                    }
                    else if (profile == BotProfile.CasualMixed && slot == 0 && rng.NextFloat() < 0.05f && s.EliteCells >= BalanceTables.CELLS_COST_BOOST_2X)
                    {
                        s.TryBoostLabSlotWithCells(slot, boost3x: false);
                    }
                }
            }
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0.0;
            double idx = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(idx);
            int hi = Math.Min(sorted.Length - 1, lo + 1);
            double frac = idx - lo;
            return sorted[lo] * (1.0 - frac) + sorted[hi] * frac;
        }

        private static void PrintProfileSummary(string label, SeedTelemetry[] rows)
        {
            double[] r1w = rows.Select(r => (double)r.Run1Wave).OrderBy(x => x).ToArray();
            double[] r1t = rows.Select(r => r.Run1DurationSec).OrderBy(x => x).ToArray();
            double[] w30 = rows.Select(r => r.TimeW30Min).OrderBy(x => x).ToArray();
            double[] w50 = rows.Select(r => r.TimeW50Min).OrderBy(x => x).ToArray();
            double[] w100 = rows.Select(r => r.TimeW100Min).OrderBy(x => x).ToArray();
            double[] best = rows.Select(r => (double)r.FinalBestWaveT1).OrderBy(x => x).ToArray();
            double[] runs = rows.Select(r => (double)r.TotalRuns).OrderBy(x => x).ToArray();
            double t2Pct = 100.0 * rows.Count(r => r.Tier2Unlocked) / rows.Length;

            Console.WriteLine($"--- {label} ({rows.Length} seeds) ---");
            Console.WriteLine($"  Run 1 Wave:   p10={Percentile(r1w, 0.10):F0}  med={Percentile(r1w, 0.50):F1}  p90={Percentile(r1w, 0.90):F0}  (time med={Percentile(r1t, 0.50):F0}s)");
            Console.WriteLine($"  W30 (UW #1):  med={FormatMin(Percentile(w30, 0.50))}  (p25={FormatMin(Percentile(w30, 0.25))}, p75={FormatMin(Percentile(w30, 0.75))})");
            Console.WriteLine($"  W50 (UW #2):  med={FormatMin(Percentile(w50, 0.50))}  (p25={FormatMin(Percentile(w50, 0.25))}, p75={FormatMin(Percentile(w50, 0.75))})");
            Console.WriteLine($"  W100 Fastest: min={FormatMin(w100[0])}  med={FormatMin(Percentile(w100, 0.50))}");
            Console.WriteLine($"  60m BestWave: p10={Percentile(best, 0.10):F0}  med={Percentile(best, 0.50):F1}  p90={Percentile(best, 0.90):F0}  | Tier2={t2Pct:F0}% | Runs med={Percentile(runs, 0.50):F0}");
        }

        private static string FormatMin(double m) => double.IsInfinity(m) ? "—" : $"{m:F1}m";

        private static bool EvaluateAndPrintInvariants(
            SeedTelemetry[] opt, SeedTelemetry[] cas, SeedTelemetry[] nai, SeedTelemetry[] all,
            out string jsonReport)
        {
            double optR1Med = Percentile(opt.Select(x => (double)x.Run1Wave).OrderBy(x => x).ToArray(), 0.50);
            double casR1Med = Percentile(cas.Select(x => (double)x.Run1Wave).OrderBy(x => x).ToArray(), 0.50);
            double naiR1Med = Percentile(nai.Select(x => (double)x.Run1Wave).OrderBy(x => x).ToArray(), 0.50);
            int earlyDeathsW4 = all.Count(x => x.Run1Wave < 5);
            int r1Overshoots = all.Count(x => x.Run1Wave > 15);
            bool a1 = optR1Med >= 7 && optR1Med <= 11 && casR1Med >= 7 && casR1Med <= 11 && naiR1Med >= 7 && naiR1Med <= 11
                && earlyDeathsW4 == 0 && r1Overshoots == 0;

            double optNoSwampPct = 100.0 * opt.Count(x => !x.HadStagnationSwamp) / opt.Length;
            double casNoSwampPct = 100.0 * cas.Count(x => !x.HadStagnationSwamp) / cas.Length;
            bool a2 = optNoSwampPct >= 92.0 && casNoSwampPct >= 85.0;

            double optW30Med = Percentile(opt.Select(x => x.TimeW30Min).OrderBy(x => x).ToArray(), 0.50);
            double casW30Med = Percentile(cas.Select(x => x.TimeW30Min).OrderBy(x => x).ToArray(), 0.50);
            bool a3 = optW30Med >= 13.0 && optW30Med <= 18.0 && casW30Med >= 16.0 && casW30Med <= 24.0;

            double optW50Med = Percentile(opt.Select(x => x.TimeW50Min).OrderBy(x => x).ToArray(), 0.50);
            double casW50Med = Percentile(cas.Select(x => x.TimeW50Min).OrderBy(x => x).ToArray(), 0.50);
            bool a4 = optW50Med >= 30.0 && optW50Med <= 38.0 && casW50Med >= 35.0 && casW50Med <= 46.0;

            double fastestW100Min = all.Min(x => x.TimeW100Min);
            int overshootBefore42 = all.Count(x => x.TimeW100Min < 42.0);
            bool a5 = overshootBefore42 == 0;

            double optBestMed = Percentile(opt.Select(x => (double)x.FinalBestWaveT1).OrderBy(x => x).ToArray(), 0.50);
            double optT2Pct = 100.0 * opt.Count(x => x.Tier2Unlocked) / opt.Length;
            double casBestMed = Percentile(cas.Select(x => (double)x.FinalBestWaveT1).OrderBy(x => x).ToArray(), 0.50);
            double naiBestMed = Percentile(nai.Select(x => (double)x.FinalBestWaveT1).OrderBy(x => x).ToArray(), 0.50);
            int naiBelow40 = nai.Count(x => x.FinalBestWaveT1 < 40);
            bool a6 = optBestMed >= 95.0 && optBestMed <= 105.0 && optT2Pct >= 70.0
                && casBestMed >= 75.0 && casBestMed <= 95.0
                && naiBestMed >= 55.0 && naiBestMed <= 75.0 && naiBelow40 <= 5;

            double optAvgGap = Percentile(opt.Select(x => x.AvgUnlockGapMin).OrderBy(x => x).ToArray(), 0.50);
            double optMaxGap45 = Percentile(opt.Select(x => x.MaxUnlockGapMin45m).OrderBy(x => x).ToArray(), 0.50);
            var validShares = opt.Where(x => x.Phase4GtBhCoinShare > 0.0).Select(x => x.Phase4GtBhCoinShare).OrderBy(x => x).ToArray();
            double gtBhShareMed = validShares.Length > 0 ? Percentile(validShares, 0.50) * 100.0 : 0.0;
            bool a7 = optAvgGap <= 5.0 && optMaxGap45 <= 8.0 && gtBhShareMed >= 45.0;

            int maxEntities = all.Max(x => x.PeakActiveEnemies);
            bool a8 = maxEntities <= BalanceTables.MAX_ACTIVE_ENEMIES_TOTAL;

            Console.WriteLine("\n=== INVARIANTS CHECK (STOP-GATE 1A) ===");
            Console.WriteLine($"[{(a1 ? "PASS" : "FAIL")}] A1 Run1 Death W7-W11: Opt={optR1Med:F1}, Cas={casR1Med:F1}, Nai={naiR1Med:F1}, <W5={earlyDeathsW4}, >W15={r1Overshoots}");
            Console.WriteLine($"[{(a2 ? "PASS" : "FAIL")}] A2 No Plateau Swamp:  OptClean={optNoSwampPct:F0}%, CasClean={casNoSwampPct:F0}%");
            Console.WriteLine($"[{(a3 ? "PASS" : "FAIL")}] A3 UW#1 (W30) Time:   Opt={optW30Med:F1}m [13..18], Cas={casW30Med:F1}m [16..24]");
            Console.WriteLine($"[{(a4 ? "PASS" : "FAIL")}] A4 GT+BH (W50) Time:  Opt={optW50Med:F1}m [30..38], Cas={casW50Med:F1}m [35..46]");
            Console.WriteLine($"[{(a5 ? "PASS" : "FAIL")}] A5 Overshoot >=42m:   FastestW100={FormatMin(fastestW100Min)}, Seeds<42m={overshootBefore42}");
            Console.WriteLine($"[{(a6 ? "PASS" : "FAIL")}] A6 60m BestWave:      Opt={optBestMed:F1} (T2={optT2Pct:F0}%), Cas={casBestMed:F1}, Nai={naiBestMed:F1} (<W40={naiBelow40})");
            Console.WriteLine($"[{(a7 ? "PASS" : "FAIL")}] A7 Dopamine & GT+BH:  AvgGap={optAvgGap:F2}m, MaxGap45m={optMaxGap45:F2}m, GT+BH CoinShare={gtBhShareMed:F1}%");
            Console.WriteLine($"[{(a8 ? "PASS" : "FAIL")}] A8 Entity Cap <=150:  MaxConcurrent={maxEntities}");

            bool allOk = a1 && a2 && a3 && a4 && a5 && a6 && a7 && a8;
            Console.WriteLine($"\nOVERALL STOP-GATE 1A: {(allOk ? "ALL 8/8 PASS" : "NEEDS CALIBRATION")}");

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"allPass\": {(allOk ? "true" : "false")},\n");
            sb.Append($"  \"seedsPerProfile\": {SEEDS_PER_PROFILE},\n");
            sb.Append($"  \"A1_pass\": {(a1 ? "true" : "false")}, \"optRun1WaveMed\": {optR1Med:F2}, \"casRun1WaveMed\": {casR1Med:F2}, \"naiRun1WaveMed\": {naiR1Med:F2},\n");
            sb.Append($"  \"A2_pass\": {(a2 ? "true" : "false")}, \"optCleanSwampPct\": {optNoSwampPct:F1}, \"casCleanSwampPct\": {casNoSwampPct:F1},\n");
            sb.Append($"  \"A3_pass\": {(a3 ? "true" : "false")}, \"optW30MinMed\": {optW30Med:F2}, \"casW30MinMed\": {casW30Med:F2},\n");
            sb.Append($"  \"A4_pass\": {(a4 ? "true" : "false")}, \"optW50MinMed\": {optW50Med:F2}, \"casW50MinMed\": {casW50Med:F2},\n");
            sb.Append($"  \"A5_pass\": {(a5 ? "true" : "false")}, \"fastestW100Min\": {(double.IsInfinity(fastestW100Min) ? -1.0 : fastestW100Min):F2}, \"seedsUnder42m\": {overshootBefore42},\n");
            sb.Append($"  \"A6_pass\": {(a6 ? "true" : "false")}, \"optBestWaveMed\": {optBestMed:F2}, \"optTier2Pct\": {optT2Pct:F1}, \"casBestWaveMed\": {casBestMed:F2}, \"naiBestWaveMed\": {naiBestMed:F2},\n");
            sb.Append($"  \"A7_pass\": {(a7 ? "true" : "false")}, \"optAvgUnlockGapMin\": {optAvgGap:F2}, \"optMaxGap45Min\": {optMaxGap45:F2}, \"gtBhCoinSharePct\": {gtBhShareMed:F1},\n");
            sb.Append($"  \"A8_pass\": {(a8 ? "true" : "false")}, \"maxActiveEnemies\": {maxEntities}\n");
            sb.Append("}\n");
            jsonReport = sb.ToString();

            return allOk;
        }

        private static void Assert(bool cond, string msg)
        {
            if (!cond) throw new InvalidOperationException("ASSERT FAILED: " + msg);
        }
    }
}
