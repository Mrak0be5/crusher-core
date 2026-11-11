using System;
using Crusher.Config;

namespace Crusher.Sim
{
    public struct LabSlotState
    {
        public bool Active;
        public LabId Lab;
        public float RemainingSeconds;
        public float TotalSeconds;
        public float BoostMult;
        public float BoostRemainingSeconds;
    }

    public struct MilestoneToast
    {
        public int Wave;
        public string TextRu;
        public int Coins;
        public int Gems;
        public int Stones;
    }

    /// <summary>
    /// Full persistent + in-run game state manager for Crusher Core V1 (60-minute progression loop).
    /// Zero allocations during Tick().
    /// </summary>
    public sealed class GameSession
    {
        public const int TOAST_QUEUE_CAPACITY = 16;

        // Persistent Currencies
        public double Coins;
        public double LifetimeCoins;
        public int Gems;
        public int Stones;
        public int EliteCells;

        // Progression & Records
        public int BestWaveT1;
        public int BestWaveT2;
        public int ActiveTier = 1;
        public int TotalRunsStarted;
        public int TotalRunsCompleted;
        public double TotalPlayRealSeconds;

        // System Unlocks
        public bool Speed15Unlocked;
        public bool WorkshopUnlocked;
        public bool LabsUnlocked;
        public bool CardsUnlocked;
        public bool UwSystemUnlocked;
        public bool GoldenBotUnlocked;
        public bool MoltenCoreSkinUnlocked;
        public bool MoltenCoreSkinEquipped;
        public bool Tier2Unlocked;
        public bool FtueDefenseArrowCompleted;

        // Speed Selection
        public float SelectedGameSpeed = 1.0f;

        // Workshop & In-Run Stat Levels
        public readonly int[] WorkshopLevels = new int[GameIdsAndScale.STAT_COUNT];
        public readonly bool[] UnlockGroups = new bool[16];
        public readonly int[] RunStatLevels = new int[GameIdsAndScale.STAT_COUNT];

        // Cards
        public int UnlockedCardSlots = 1;
        public readonly int[] CardCopies = new int[GameIdsAndScale.CARD_COUNT];
        public readonly int[] EquippedCards = new int[BalanceTables.MAX_CARD_SLOTS] { -1, -1, -1, -1 };
        public int TotalCardPulls;

        // Labs
        public int UnlockedLabSlots = 1;
        public readonly int[] LabLevels = new int[GameIdsAndScale.LAB_COUNT];
        public readonly LabSlotState[] LabSlots = new LabSlotState[BalanceTables.MAX_LAB_SLOTS];

        // Ultimate Weapons
        public readonly bool[] UwUnlocked = new bool[GameIdsAndScale.UW_COUNT];
        public readonly int[] UwLevels = new int[GameIdsAndScale.UW_COUNT];
        public int UwUnlockedCount;

        // Milestones (Tier 1: 12 milestones)
        public readonly bool[] ClaimedMilestonesT1 = new bool[12];
        public readonly MilestoneToast[] ToastQueue = new MilestoneToast[TOAST_QUEUE_CAPACITY];
        public int ToastCount;

        // Active Run State
        public bool InRun;
        public double RunCash;
        private double _lastSyncedSimCash;
        private double _lastSyncedSimCoins;
        private int _lastSyncedSimCells;

        public readonly CombatSim Combat;
        public FastRng MetaRng;

        // Pre-allocated scratch arrays
        private readonly StatId[] _freeUpgradeCandidates = new StatId[12];
        private readonly CardId[] _gachaPoolScratch = new CardId[GameIdsAndScale.CARD_COUNT];
        private readonly int[] _gachaWeightScratch = new int[GameIdsAndScale.CARD_COUNT];

        public GameSession(ulong seed = 12345UL)
        {
            MetaRng = new FastRng(seed ^ 0xA5A5A5A55A5A5A5AUL);
            Combat = new CombatSim(seed);
            UnlockGroups[(int)UnlockGroup.AlwaysOpen] = true;
        }

        // --- Core Grade & Effective Speed ---

        public float MaxUnlockedGameSpeed =>
            BalanceTables.GetMaxUnlockedGameSpeed(LabLevels[(int)LabId.GameSpeed], Speed15Unlocked, WorkshopUnlocked);

        public float EffectiveTimeScale =>
            BalanceTables.GetEffectiveTimeScale(SelectedGameSpeed, MaxUnlockedGameSpeed);

        /// <summary>
        /// Fix #6 (#20): G0 -> G1 (Thorns/DefAbs unlocked) -> G2 (Orbs unlocked) -> G3 (UW #1 unlocked).
        /// </summary>
        public CoreVisualGrade GetVisualCoreGrade()
        {
            if (UwUnlockedCount >= 1)
                return CoreVisualGrade.G3_DiamondReactor;
            if (UnlockGroups[(int)UnlockGroup.Orbs])
                return CoreVisualGrade.G2_TitaniumTwin;
            if (UnlockGroups[(int)UnlockGroup.Thorns] || UnlockGroups[(int)UnlockGroup.DefenseAbs])
                return CoreVisualGrade.G1_SteelCarbide;
            return CoreVisualGrade.G0_Rusty;
        }

        public void SetSelectedGameSpeed(float speed)
        {
            SelectedGameSpeed = Math.Clamp(speed, 1.0f, MaxUnlockedGameSpeed);
        }

        // --- Run Lifecycle & Main Tick ---

        public void StartNewRun(int tier = 1)
        {
            if (tier == 2 && !Tier2Unlocked) tier = 1;
            ActiveTier = tier;
            TotalRunsStarted++;

            Array.Clear(RunStatLevels, 0, RunStatLevels.Length);
            RunCash = BalanceTables.STARTING_RUN_CASH;
            _lastSyncedSimCash = 0.0;
            _lastSyncedSimCoins = 0.0;
            _lastSyncedSimCells = 0;

            ComputedTowerStats initial = ComputeCurrentStats();
            ulong runSeed = MetaRng.NextUInt() | ((ulong)MetaRng.NextUInt() << 32);
            Combat.ResetForNewRun(tier, TotalRunsStarted, in initial, runSeed);
            PushStatsToCombat();
            InRun = true;
        }

        public void EndCurrentRunToHangar()
        {
            if (!InRun) return;
            SyncCombatEarnings();
            UpdateBestWavesAndMilestones(Combat.CurrentWave);
            InRun = false;
            TotalRunsCompleted++;
        }

        public void Tick(float realUnscaledDt)
        {
            if (realUnscaledDt <= 0f) return;
            TotalPlayRealSeconds += realUnscaledDt;

            // 1. Always tick Lab Slots in real time (both in Hangar and during Combat!)
            TickLabsRealTime(realUnscaledDt);

            // 2. Tick Combat if in an active run
            if (InRun)
            {
                Combat.Tick(realUnscaledDt, EffectiveTimeScale);
                SyncCombatEarnings();

                if (Combat.PendingWaveEndPayout)
                {
                    Combat.PendingWaveEndPayout = false;
                    ProcessWaveCompletion(Combat.PendingCompletedWave);
                }

                UpdateBestWavesAndMilestones(Combat.CurrentWave);

                if (Combat.IsGameOver)
                {
                    EndCurrentRunToHangar();
                }
            }
        }

        private void SyncCombatEarnings()
        {
            double dCash = Combat.RunCashEarned - _lastSyncedSimCash;
            if (dCash > 0.0)
            {
                RunCash += dCash;
                _lastSyncedSimCash = Combat.RunCashEarned;
            }

            double dCoins = Combat.RunCoinsEarned - _lastSyncedSimCoins;
            if (dCoins > 0.0)
            {
                Coins += dCoins;
                LifetimeCoins += dCoins;
                _lastSyncedSimCoins = Combat.RunCoinsEarned;
            }

            int dCells = Combat.RunCellsEarned - _lastSyncedSimCells;
            if (dCells > 0)
            {
                EliteCells += dCells;
                _lastSyncedSimCells = Combat.RunCellsEarned;
            }
        }

        private void ProcessWaveCompletion(int completedWave)
        {
            ref readonly ComputedTowerStats st = ref Combat.Stats;

            // End-of-wave Cash & Coins
            double waveCash = st.CashPerWave * st.CashBonus;
            RunCash += waveCash;
            Combat.RunCashEarned += waveCash;
            _lastSyncedSimCash = Combat.RunCashEarned;

            if (completedWave >= 2)
            {
                double waveCoins = st.CoinsPerWave * st.CardCoinMult * st.GlobalAccountCoinMult;
                Coins += waveCoins;
                LifetimeCoins += waveCoins;
                Combat.RunCoinsEarned += waveCoins;
                _lastSyncedSimCoins = Combat.RunCoinsEarned;
            }

            // Free Upgrades roll independently per tab (Attack, Defense, Utility)
            if (st.FreeUpgradeChance > 0f)
            {
                bool anyUpgraded = false;
                anyUpgraded |= RollFreeUpgradeForTab(StatTab.Attack, st.FreeUpgradeChance);
                anyUpgraded |= RollFreeUpgradeForTab(StatTab.Defense, st.FreeUpgradeChance);
                anyUpgraded |= RollFreeUpgradeForTab(StatTab.Utility, st.FreeUpgradeChance);
                if (anyUpgraded)
                {
                    PushStatsToCombat();
                }
            }
        }

        private bool RollFreeUpgradeForTab(StatTab tab, float chance)
        {
            if (Combat.Rng.NextFloat() >= chance) return false;

            int count = 0;
            for (int i = 0; i < GameIdsAndScale.STAT_COUNT; i++)
            {
                ref readonly StatDef def = ref BalanceTables.Stats[i];
                if (def.Tab != tab) continue;
                if (!UnlockGroups[(int)def.Group]) continue;
                if (GetTotalStatLevel(def.Id) >= def.MaxLevel) continue;
                _freeUpgradeCandidates[count++] = def.Id;
            }

            if (count == 0) return false;
            int pick = Combat.Rng.NextRange(0, count);
            RunStatLevels[(int)_freeUpgradeCandidates[pick]]++;
            return true;
        }

        private void UpdateBestWavesAndMilestones(int waveReached)
        {
            if (ActiveTier == 1 && waveReached > BestWaveT1)
            {
                BestWaveT1 = waveReached;
            }
            else if (ActiveTier == 2 && waveReached > BestWaveT2)
            {
                BestWaveT2 = waveReached;
            }

            if (ActiveTier == 1)
            {
                for (int i = 0; i < BalanceTables.Tier1Milestones.Length; i++)
                {
                    if (ClaimedMilestonesT1[i]) continue;
                    ref readonly MilestoneDef ms = ref BalanceTables.Tier1Milestones[i];
                    if (BestWaveT1 >= ms.Wave)
                    {
                        ClaimedMilestonesT1[i] = true;
                        Coins += ms.CoinsReward;
                        LifetimeCoins += ms.CoinsReward;
                        Gems += ms.GemsReward;
                        Stones += ms.StonesReward;
                        ApplyMilestoneFlags(ms.UnlockFlags);

                        if (ToastCount < TOAST_QUEUE_CAPACITY)
                        {
                            ToastQueue[ToastCount++] = new MilestoneToast
                            {
                                Wave = ms.Wave,
                                TextRu = ms.BannerRu,
                                Coins = ms.CoinsReward,
                                Gems = ms.GemsReward,
                                Stones = ms.StonesReward
                            };
                        }
                    }
                }
            }
        }

        private void ApplyMilestoneFlags(MilestoneUnlockFlags flags)
        {
            if ((flags & MilestoneUnlockFlags.Speed15x) != 0 && !Speed15Unlocked)
            {
                Speed15Unlocked = true;
                if (SelectedGameSpeed < 1.6f) SelectedGameSpeed = 1.6f;
            }
            if ((flags & MilestoneUnlockFlags.Workshop) != 0)
            {
                WorkshopUnlocked = true;
                if (SelectedGameSpeed < 2.0f) SelectedGameSpeed = 2.0f;
            }
            if ((flags & MilestoneUnlockFlags.Labs) != 0) LabsUnlocked = true;
            if ((flags & MilestoneUnlockFlags.Cards) != 0) CardsUnlocked = true;
            if ((flags & MilestoneUnlockFlags.UltimateWeapons) != 0) UwSystemUnlocked = true;
            if ((flags & MilestoneUnlockFlags.GoldenBot) != 0)
            {
                GoldenBotUnlocked = true;
                if (InRun) PushStatsToCombat();
            }
            if ((flags & MilestoneUnlockFlags.MoltenCoreSkin) != 0)
            {
                MoltenCoreSkinUnlocked = true;
                MoltenCoreSkinEquipped = true;
                if (InRun) PushStatsToCombat();
            }
            if ((flags & MilestoneUnlockFlags.Tier2) != 0) Tier2Unlocked = true;
        }

        public bool ClaimActiveGemDrone()
        {
            if (!InRun || !Combat.TryClaimGemDrone()) return false;
            Gems += BalanceTables.GEM_DRONE_REWARD;
            return true;
        }

        // --- Workshop & In-Run Stat Purchases ---

        public bool IsStatUnlocked(StatId id) => UnlockGroups[(int)BalanceTables.Stats[(int)id].Group];

        public int GetTotalStatLevel(StatId id)
        {
            int idx = (int)id;
            int max = BalanceTables.Stats[idx].MaxLevel;
            int sum = WorkshopLevels[idx] + (InRun ? RunStatLevels[idx] : 0);
            return sum > max ? max : sum;
        }

        public bool TryUnlockWorkshopBranch(StatId id)
        {
            ref readonly StatDef def = ref BalanceTables.Stats[(int)id];
            if (UnlockGroups[(int)def.Group]) return false;
            if (Coins < def.UnlockCoins) return false;

            Coins -= def.UnlockCoins;
            UnlockGroups[(int)def.Group] = true;
            if (def.Group == UnlockGroup.Thorns || def.Group == UnlockGroup.DefenseAbs)
            {
                FtueDefenseArrowCompleted = true;
            }
            if (InRun) PushStatsToCombat();
            return true;
        }

        public bool TryBuyWorkshopUpgrade(StatId id, int count = 1)
        {
            if (!IsStatUnlocked(id)) return false;
            int idx = (int)id;
            int max = BalanceTables.Stats[idx].MaxLevel;
            bool boughtAny = false;

            for (int k = 0; k < count; k++)
            {
                if (WorkshopLevels[idx] >= max) break;
                double cost = BalanceTables.GetCoinCost(id, WorkshopLevels[idx]);
                if (Coins < cost) break;
                Coins -= cost;
                WorkshopLevels[idx]++;
                boughtAny = true;
            }

            if (boughtAny && InRun) PushStatsToCombat();
            return boughtAny;
        }

        public bool TryBuyRunUpgrade(StatId id, int count = 1)
        {
            if (!InRun || !IsStatUnlocked(id)) return false;
            int idx = (int)id;
            int max = BalanceTables.Stats[idx].MaxLevel;
            bool boughtAny = false;

            for (int k = 0; k < count; k++)
            {
                int totalLv = WorkshopLevels[idx] + RunStatLevels[idx];
                if (totalLv >= max) break;
                double cost = BalanceTables.GetCashCost(id, totalLv);
                if (RunCash < cost) break;
                RunCash -= cost;
                RunStatLevels[idx]++;
                boughtAny = true;
            }

            if (boughtAny) PushStatsToCombat();
            return boughtAny;
        }

        // --- Cards & Gacha ---

        public int GetCardStar(CardId id) => BalanceTables.GetCardStarFromCopies(CardCopies[(int)id]);

        public bool IsCardEquipped(CardId id)
        {
            int target = (int)id;
            for (int i = 0; i < UnlockedCardSlots; i++)
            {
                if (EquippedCards[i] == target) return true;
            }
            return false;
        }

        public bool AllCardsMaxed()
        {
            for (int i = 0; i < GameIdsAndScale.CARD_COUNT; i++)
            {
                if (CardCopies[i] < BalanceTables.MAX_CARD_COPIES) return false;
            }
            return true;
        }

        public bool TryPullCard(out CardId pulled, out int newStar)
        {
            pulled = CardId.EnemyBalance;
            newStar = 0;
            if (!CardsUnlocked || Gems < BalanceTables.CARD_PULL_COST_GEMS || AllCardsMaxed())
                return false;

            CardId chosen;
            if (TotalCardPulls < BalanceTables.DeterministicFirstPulls.Length)
            {
                chosen = BalanceTables.DeterministicFirstPulls[TotalCardPulls];
                if (CardCopies[(int)chosen] >= BalanceTables.MAX_CARD_COPIES)
                {
                    chosen = PickWeightedCard();
                }
            }
            else
            {
                chosen = PickWeightedCard();
            }

            Gems -= BalanceTables.CARD_PULL_COST_GEMS;
            TotalCardPulls++;
            CardCopies[(int)chosen]++;
            pulled = chosen;
            newStar = GetCardStar(chosen);

            // Auto-equip newly acquired card if an unlocked slot is empty
            if (!IsCardEquipped(chosen))
            {
                for (int s = 0; s < UnlockedCardSlots; s++)
                {
                    if (EquippedCards[s] < 0)
                    {
                        EquippedCards[s] = (int)chosen;
                        break;
                    }
                }
            }

            if (InRun) PushStatsToCombat();
            return true;
        }

        private CardId PickWeightedCard()
        {
            int poolCount = 0;
            int totalWeight = 0;
            for (int i = 0; i < GameIdsAndScale.CARD_COUNT; i++)
            {
                if (CardCopies[i] >= BalanceTables.MAX_CARD_COPIES) continue;
                CardRarity rar = BalanceTables.Cards[i].Rarity;
                int w = rar == CardRarity.Common ? 55 : (rar == CardRarity.Rare ? 30 : 15);
                _gachaPoolScratch[poolCount] = (CardId)i;
                _gachaWeightScratch[poolCount] = w;
                totalWeight += w;
                poolCount++;
            }

            int roll = MetaRng.NextRange(0, totalWeight);
            int acc = 0;
            for (int i = 0; i < poolCount; i++)
            {
                acc += _gachaWeightScratch[i];
                if (roll < acc) return _gachaPoolScratch[i];
            }
            return _gachaPoolScratch[0];
        }

        public bool TryUnlockCardSlot()
        {
            if (!CardsUnlocked || UnlockedCardSlots >= BalanceTables.MAX_CARD_SLOTS) return false;
            int cost = BalanceTables.CardSlotUnlockGems[UnlockedCardSlots];
            if (Gems < cost) return false;
            Gems -= cost;
            UnlockedCardSlots++;
            return true;
        }

        public bool TryEquipCard(CardId id, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= UnlockedCardSlots) return false;
            if (CardCopies[(int)id] <= 0) return false;

            // Remove if already in another slot
            for (int i = 0; i < UnlockedCardSlots; i++)
            {
                if (EquippedCards[i] == (int)id) EquippedCards[i] = -1;
            }
            EquippedCards[slotIndex] = (int)id;
            if (InRun) PushStatsToCombat();
            return true;
        }

        // --- Labs & Elite Cells ---

        public bool IsLabCurrentlyActive(LabId id)
        {
            for (int i = 0; i < UnlockedLabSlots; i++)
            {
                if (LabSlots[i].Active && LabSlots[i].Lab == id) return true;
            }
            return false;
        }

        public bool TryUnlockLabSlot()
        {
            if (!LabsUnlocked || UnlockedLabSlots >= BalanceTables.MAX_LAB_SLOTS) return false;
            int cost = BalanceTables.LabSlotUnlockGems[UnlockedLabSlots];
            if (Gems < cost) return false;
            Gems -= cost;
            UnlockedLabSlots++;
            return true;
        }

        public bool TryStartLab(LabId id, int slotIndex)
        {
            if (!LabsUnlocked || slotIndex < 0 || slotIndex >= UnlockedLabSlots) return false;
            if (LabSlots[slotIndex].Active || IsLabCurrentlyActive(id)) return false;
            if (id == LabId.BlackHoleCoinBonus && !UwUnlocked[(int)UwId.BlackHole]) return false;

            int curLv = LabLevels[(int)id];
            if (curLv >= BalanceTables.Labs[(int)id].MaxLevel) return false;

            int cost = BalanceTables.GetLabCoinCost(id, curLv);
            if (Coins < cost) return false;

            float dur = BalanceTables.GetLabDurationSeconds(id, curLv);
            Coins -= cost;

            float existingBoost = LabSlots[slotIndex].BoostRemainingSeconds > 0f ? LabSlots[slotIndex].BoostMult : 1.0f;
            float existingBoostRem = LabSlots[slotIndex].BoostRemainingSeconds;

            LabSlots[slotIndex] = new LabSlotState
            {
                Active = true,
                Lab = id,
                RemainingSeconds = dur,
                TotalSeconds = dur,
                BoostMult = existingBoost > 1f ? existingBoost : 1.0f,
                BoostRemainingSeconds = existingBoostRem
            };
            return true;
        }

        public bool TryBoostLabSlotWithCells(int slotIndex, bool boost3x)
        {
            if (!LabsUnlocked || slotIndex < 0 || slotIndex >= UnlockedLabSlots) return false;
            int cost = boost3x ? BalanceTables.CELLS_COST_BOOST_3X : BalanceTables.CELLS_COST_BOOST_2X;
            float mult = boost3x ? 3.0f : 2.0f;
            if (EliteCells < cost) return false;
            if (LabSlots[slotIndex].BoostRemainingSeconds > 60f && LabSlots[slotIndex].BoostMult >= mult) return false;

            EliteCells -= cost;
            LabSlots[slotIndex].BoostMult = mult;
            LabSlots[slotIndex].BoostRemainingSeconds = BalanceTables.CELLS_BOOST_DURATION_REAL_SEC;
            return true;
        }

        public bool TryRushLabSlotWithGems(int slotIndex)
        {
            if (!LabsUnlocked || slotIndex < 0 || slotIndex >= UnlockedLabSlots) return false;
            if (!LabSlots[slotIndex].Active) return false;
            int gemCost = BalanceTables.GetLabRushGemCost(LabSlots[slotIndex].RemainingSeconds);
            if (Gems < gemCost) return false;

            Gems -= gemCost;
            CompleteLabInSlot(slotIndex);
            return true;
        }

        public int TickLabsRealTime(float realSeconds)
        {
            if (!LabsUnlocked || realSeconds <= 0f) return 0;
            int completedCount = 0;

            for (int i = 0; i < UnlockedLabSlots; i++)
            {
                ref LabSlotState slot = ref LabSlots[i];
                float remDt = realSeconds;

                if (slot.BoostRemainingSeconds > 0f)
                {
                    float boostedSlice = MathF.Min(remDt, slot.BoostRemainingSeconds);
                    if (slot.Active)
                    {
                        slot.RemainingSeconds -= boostedSlice * slot.BoostMult;
                    }
                    slot.BoostRemainingSeconds -= boostedSlice;
                    if (slot.BoostRemainingSeconds <= 0f)
                    {
                        slot.BoostRemainingSeconds = 0f;
                        slot.BoostMult = 1.0f;
                    }
                    remDt -= boostedSlice;
                }

                if (remDt > 0f && slot.Active)
                {
                    slot.RemainingSeconds -= remDt;
                }

                if (slot.Active && slot.RemainingSeconds <= 0f)
                {
                    CompleteLabInSlot(i);
                    completedCount++;
                }
            }
            return completedCount;
        }

        private void CompleteLabInSlot(int slotIndex)
        {
            LabId finished = LabSlots[slotIndex].Lab;
            LabSlots[slotIndex].Active = false;
            LabSlots[slotIndex].RemainingSeconds = 0f;

            int idx = (int)finished;
            if (LabLevels[idx] < BalanceTables.Labs[idx].MaxLevel)
            {
                LabLevels[idx]++;
            }

            if (finished == LabId.GameSpeed)
            {
                // Auto-engage newly researched game speed!
                SelectedGameSpeed = MaxUnlockedGameSpeed;
            }

            if (InRun) PushStatsToCombat();
        }

        // --- Ultimate Weapons ---

        public int GetNextUwUnlockStoneCost()
        {
            if (UwUnlockedCount >= GameIdsAndScale.UW_COUNT) return int.MaxValue;
            return BalanceTables.UwUnlockStoneCosts[UwUnlockedCount];
        }

        public int GetAvailableUwChoices(Span<UwId> outChoices)
        {
            int n = 0;
            for (int i = 0; i < GameIdsAndScale.UW_COUNT && n < outChoices.Length; i++)
            {
                if (!UwUnlocked[i]) outChoices[n++] = (UwId)i;
            }
            return n;
        }

        public bool TryUnlockUw(UwId choice)
        {
            if (!UwSystemUnlocked || UwUnlocked[(int)choice]) return false;
            int cost = GetNextUwUnlockStoneCost();
            if (Stones < cost) return false;

            Stones -= cost;
            UwUnlocked[(int)choice] = true;
            UwUnlockedCount++;
            if (InRun) PushStatsToCombat();
            return true;
        }

        public int GetUwUpgradeStoneCost(UwId id)
        {
            int lv = UwLevels[(int)id];
            if (lv >= BalanceTables.Uws[(int)id].MaxUpgradeLevel) return int.MaxValue;
            return BalanceTables.UwUpgradeStoneCosts[lv];
        }

        public bool TryUpgradeUw(UwId id)
        {
            if (!UwUnlocked[(int)id]) return false;
            int cost = GetUwUpgradeStoneCost(id);
            if (Stones < cost) return false;

            Stones -= cost;
            UwLevels[(int)id]++;
            if (InRun) PushStatsToCombat();
            return true;
        }

        // --- Stat Aggregation ---

        public ComputedTowerStats ComputeCurrentStats()
        {
            ComputedTowerStats s = default;

            double carbideDmgMult = Math.Pow(1.08, LabLevels[(int)LabId.CarbideOverdrive]);
            double cpkLabMult = Math.Pow(1.06, LabLevels[(int)LabId.CoinsPerKill]);
            float orbLabMult = 1.0f + 0.10f * LabLevels[(int)LabId.OrbAndRotorSpeed];

            float dmgCardMult = GetEquippedCardValue(CardId.Damage, 1.0f);
            float hpCardMult = GetEquippedCardValue(CardId.Health, 1.0f);
            float coinCardMult = GetEquippedCardValue(CardId.Coins, 1.0f);
            float ebCash = GetEquippedCardValue(CardId.EnemyBalance, 0.0f);
            float extraOrbSpeed = GetEquippedCardValue(CardId.ExtraOrb, 0.0f);
            float plasmaPct = GetEquippedCardValue(CardId.PlasmaCannon, 0.0f);
            float deathRayDur = GetEquippedCardValue(CardId.DeathRay, 0.0f);
            float waveAccel = GetEquippedCardValue(CardId.WaveAccelerator, 0.0f);

            s.Damage = BalanceTables.GetStatValue(StatId.Damage, GetTotalStatLevel(StatId.Damage)) * carbideDmgMult * dmgCardMult;
            s.AttackSpeed = BalanceTables.GetStatValue(StatId.AttackSpeed, GetTotalStatLevel(StatId.AttackSpeed));
            s.CritChance = IsStatUnlocked(StatId.CritChance)
                ? (float)BalanceTables.GetStatValue(StatId.CritChance, GetTotalStatLevel(StatId.CritChance))
                : 0.02f;
            s.CritFactor = IsStatUnlocked(StatId.CritFactor)
                ? BalanceTables.GetStatValue(StatId.CritFactor, GetTotalStatLevel(StatId.CritFactor))
                : 1.20;
            s.RangeSim = IsStatUnlocked(StatId.Range)
                ? (float)BalanceTables.GetStatValue(StatId.Range, GetTotalStatLevel(StatId.Range))
                : GameIdsAndScale.RANGE_0_SIM;

            s.MultishotChance = IsStatUnlocked(StatId.MultishotChance)
                ? (float)BalanceTables.GetStatValue(StatId.MultishotChance, GetTotalStatLevel(StatId.MultishotChance))
                : 0f;
            s.MultishotTargets = IsStatUnlocked(StatId.MultishotTargets)
                ? (int)BalanceTables.GetStatValue(StatId.MultishotTargets, GetTotalStatLevel(StatId.MultishotTargets))
                : 2;

            s.MaxHp = BalanceTables.GetStatValue(StatId.Health, GetTotalStatLevel(StatId.Health)) * hpCardMult;
            s.HealthRegen = BalanceTables.GetStatValue(StatId.HealthRegen, GetTotalStatLevel(StatId.HealthRegen));
            s.DefAbs = IsStatUnlocked(StatId.DefenseAbsolute)
                ? BalanceTables.GetStatValue(StatId.DefenseAbsolute, GetTotalStatLevel(StatId.DefenseAbsolute))
                : 0.0;
            s.DefPercent = IsStatUnlocked(StatId.DefensePercent)
                ? BalanceTables.GetStatValue(StatId.DefensePercent, GetTotalStatLevel(StatId.DefensePercent))
                : 0.0;
            s.Thorns = IsStatUnlocked(StatId.Thorns)
                ? BalanceTables.GetStatValue(StatId.Thorns, GetTotalStatLevel(StatId.Thorns))
                : 0.0;

            s.KnockbackChance = IsStatUnlocked(StatId.KnockbackChance)
                ? (float)BalanceTables.GetStatValue(StatId.KnockbackChance, GetTotalStatLevel(StatId.KnockbackChance))
                : 0f;
            s.KnockbackForce = IsStatUnlocked(StatId.KnockbackForce)
                ? (float)BalanceTables.GetStatValue(StatId.KnockbackForce, GetTotalStatLevel(StatId.KnockbackForce))
                : 0.40f;

            s.OrbsCount = IsStatUnlocked(StatId.OrbsCount)
                ? (int)BalanceTables.GetStatValue(StatId.OrbsCount, GetTotalStatLevel(StatId.OrbsCount))
                : 0;
            float rawOrbSpeed = IsStatUnlocked(StatId.OrbSpeed)
                ? (float)BalanceTables.GetStatValue(StatId.OrbSpeed, GetTotalStatLevel(StatId.OrbSpeed))
                : 0.08f;
            // Convert 0.08..0.60 -> ~0.95..4.4 rad/sec angular sweep
            s.OrbAngularSpeedRad = (0.55f + rawOrbSpeed * 6.0f) * orbLabMult;

            s.Lifesteal = IsStatUnlocked(StatId.Lifesteal)
                ? BalanceTables.GetStatValue(StatId.Lifesteal, GetTotalStatLevel(StatId.Lifesteal))
                : 0.0;

            s.CashBonus = BalanceTables.GetStatValue(StatId.CashBonus, GetTotalStatLevel(StatId.CashBonus));
            s.CashPerWave = IsStatUnlocked(StatId.CashPerWave)
                ? BalanceTables.GetStatValue(StatId.CashPerWave, GetTotalStatLevel(StatId.CashPerWave))
                : 0.0;
            s.CoinsPerKillMult = (IsStatUnlocked(StatId.CoinsPerKill)
                ? BalanceTables.GetStatValue(StatId.CoinsPerKill, GetTotalStatLevel(StatId.CoinsPerKill))
                : 1.0) * cpkLabMult;
            s.CoinsPerWave = IsStatUnlocked(StatId.CoinsPerWave)
                ? BalanceTables.GetStatValue(StatId.CoinsPerWave, GetTotalStatLevel(StatId.CoinsPerWave))
                : 1.0;
            s.FreeUpgradeChance = IsStatUnlocked(StatId.FreeUpgrades)
                ? (float)BalanceTables.GetStatValue(StatId.FreeUpgrades, GetTotalStatLevel(StatId.FreeUpgrades))
                : 0f;

            s.HasEnemyBalance = ebCash > 0f;
            s.EnemyBalanceCashMult = ebCash > 0f ? ebCash : 1.0;
            s.CardCoinMult = coinCardMult;
            s.HasExtraOrb = extraOrbSpeed > 0f;
            s.ExtraOrbSpeedMult = extraOrbSpeed > 0f ? extraOrbSpeed : 1.0f;
            s.PlasmaCannonBossPct = plasmaPct;
            s.DeathRayDuration = deathRayDur;
            s.WaveCooldownSeconds = BalanceTables.WAVE_COOLDOWN_SECONDS * (1.0f - waveAccel);

            s.MagneticGarlicLevel = LabLevels[(int)LabId.MagneticGarlic];
            s.BossThornsBonus = 0.02 * LabLevels[(int)LabId.CarbideOverdrive];
            s.BhCoinMult = BalanceTables.GetBlackHoleCoinMult(LabLevels[(int)LabId.BlackHoleCoinBonus]);

            double skinMult = MoltenCoreSkinUnlocked ? BalanceTables.MOLTEN_CORE_COIN_BONUS : 1.0;
            s.GlobalAccountCoinMult = BalanceTables.GetTierCoinMult(ActiveTier) * skinMult;

            return s;
        }

        public void PushStatsToCombat()
        {
            ComputedTowerStats st = ComputeCurrentStats();
            Combat.UpdateStatsLive(
                in st,
                UwUnlocked[(int)UwId.GoldenTower], UwLevels[(int)UwId.GoldenTower],
                UwUnlocked[(int)UwId.BlackHole], UwLevels[(int)UwId.BlackHole],
                UwUnlocked[(int)UwId.ChainLightning], UwLevels[(int)UwId.ChainLightning],
                GoldenBotUnlocked);
        }

        private float GetEquippedCardValue(CardId id, float defaultValue)
        {
            if (!IsCardEquipped(id)) return defaultValue;
            int star = GetCardStar(id);
            if (star <= 0) return defaultValue;
            return BalanceTables.Cards[(int)id].GetValueForStar(star);
        }
    }
}
