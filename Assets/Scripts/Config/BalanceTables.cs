using System;

namespace Crusher.Config
{
    public enum StatTab : byte
    {
        Attack = 0,
        Defense = 1,
        Utility = 2
    }

    public enum UnlockGroup : byte
    {
        AlwaysOpen = 0,
        Crit,
        Range,
        Multishot,
        DefenseAbs,
        DefensePct,
        Thorns,
        Knockback,
        Orbs,
        Lifesteal,
        CashWave,
        Coins,
        FreeUp
    }

    public enum CardRarity : byte
    {
        Common = 0,
        Rare = 1,
        Epic = 2
    }

    [Flags]
    public enum MilestoneUnlockFlags : ushort
    {
        None = 0,
        Speed15x = 1 << 0,
        Workshop = 1 << 1,
        Labs = 1 << 2,
        Cards = 1 << 3,
        UltimateWeapons = 1 << 4,
        GoldenBot = 1 << 5,
        MoltenCoreSkin = 1 << 6,
        Tier2 = 1 << 7
    }

    public readonly struct StatDef
    {
        public readonly StatId Id;
        public readonly StatTab Tab;
        public readonly UnlockGroup Group;
        public readonly string TitleRu;
        public readonly string UnitSuffix;
        public readonly int MaxLevel;
        public readonly int UnlockCoins;
        public readonly double CashBase;
        public readonly double CashA;
        public readonly double CashB;
        public readonly double CashC;
        public readonly double CoinBase;
        public readonly double CoinA;
        public readonly double CoinB;
        public readonly double CoinC;

        public StatDef(
            StatId id, StatTab tab, UnlockGroup group,
            string titleRu, string unitSuffix,
            int maxLevel, int unlockCoins,
            double cashBase, double cashA, double cashB, double cashC,
            double coinBase, double coinA, double coinB, double coinC)
        {
            Id = id;
            Tab = tab;
            Group = group;
            TitleRu = titleRu;
            UnitSuffix = unitSuffix;
            MaxLevel = maxLevel;
            UnlockCoins = unlockCoins;
            CashBase = cashBase;
            CashA = cashA;
            CashB = cashB;
            CashC = cashC;
            CoinBase = coinBase;
            CoinA = coinA;
            CoinB = coinB;
            CoinC = coinC;
        }
    }

    public readonly struct EnemyTypeDef
    {
        public readonly EnemyType Type;
        public readonly string TitleRu;
        public readonly double HpMult;
        public readonly double DmgMult;
        public readonly float SpeedMult;
        public readonly float Mass;
        public readonly double CashMult;
        public readonly int BaseCoins;
        public readonly float HitboxRadiusSim;
        public readonly bool ImmuneToOrbsAndBh;
        public readonly double ThornsTakenMult;

        public EnemyTypeDef(
            EnemyType type, string titleRu,
            double hpMult, double dmgMult, float speedMult, float mass,
            double cashMult, int baseCoins, float hitboxRadiusSim,
            bool immuneToOrbsAndBh, double thornsTakenMult)
        {
            Type = type;
            TitleRu = titleRu;
            HpMult = hpMult;
            DmgMult = dmgMult;
            SpeedMult = speedMult;
            Mass = mass;
            CashMult = cashMult;
            BaseCoins = baseCoins;
            HitboxRadiusSim = hitboxRadiusSim;
            ImmuneToOrbsAndBh = immuneToOrbsAndBh;
            ThornsTakenMult = thornsTakenMult;
        }
    }

    public readonly struct CardDef
    {
        public readonly CardId Id;
        public readonly CardRarity Rarity;
        public readonly string TitleRu;
        public readonly string DescRu;
        public readonly float Val1Star;
        public readonly float Val2Star;
        public readonly float Val3Star;

        public CardDef(CardId id, CardRarity rarity, string titleRu, string descRu, float v1, float v2, float v3)
        {
            Id = id;
            Rarity = rarity;
            TitleRu = titleRu;
            DescRu = descRu;
            Val1Star = v1;
            Val2Star = v2;
            Val3Star = v3;
        }

        public float GetValueForStar(int star)
        {
            if (star <= 0) return 0f;
            if (star == 1) return Val1Star;
            if (star == 2) return Val2Star;
            return Val3Star;
        }
    }

    public readonly struct LabDef
    {
        public readonly LabId Id;
        public readonly string TitleRu;
        public readonly string DescRu;
        public readonly int MaxLevel;
        public readonly int BaseCoins;
        public readonly float CoinGrowth;
        public readonly float BaseDurationSec;
        public readonly float DurationGrowth;

        public LabDef(LabId id, string titleRu, string descRu, int maxLevel, int baseCoins, float coinGrowth, float baseDur, float durGrowth)
        {
            Id = id;
            TitleRu = titleRu;
            DescRu = descRu;
            MaxLevel = maxLevel;
            BaseCoins = baseCoins;
            CoinGrowth = coinGrowth;
            BaseDurationSec = baseDur;
            DurationGrowth = durGrowth;
        }
    }

    public readonly struct UwDef
    {
        public readonly UwId Id;
        public readonly string TitleRu;
        public readonly string DescRu;
        public readonly int MaxUpgradeLevel;

        public UwDef(UwId id, string titleRu, string descRu, int maxUpgradeLevel)
        {
            Id = id;
            TitleRu = titleRu;
            DescRu = descRu;
            MaxUpgradeLevel = maxUpgradeLevel;
        }
    }

    public readonly struct MilestoneDef
    {
        public readonly int Wave;
        public readonly int CoinsReward;
        public readonly int GemsReward;
        public readonly int StonesReward;
        public readonly MilestoneUnlockFlags UnlockFlags;
        public readonly string BannerRu;

        public MilestoneDef(int wave, int coins, int gems, int stones, MilestoneUnlockFlags flags, string bannerRu)
        {
            Wave = wave;
            CoinsReward = coins;
            GemsReward = gems;
            StonesReward = stones;
            UnlockFlags = flags;
            BannerRu = bannerRu;
        }
    }

    /// <summary>
    /// Complete zero-allocation mathematical tables and balance curves for Crusher Core V1 (0–60 min slice).
    /// Calibrated against client v29.1.4 formulas + 60-minute pacing invariants A1–A8.
    /// </summary>
    public static class BalanceTables
    {
        // --- Core Wave & Arena Constants ---
        public const float WAVE_FIGHT_SECONDS = 26.0f;
        public const float WAVE_COOLDOWN_SECONDS = 9.0f;
        public const float SPAWN_TICK_SECONDS = 0.125f;
        public const int SPAWN_TICKS_PER_WAVE = 208;

        public const float CORE_RADIUS_SIM = 0.45f;          // 0.45 * 2.5 = 1.125 m rotor teeth radius
        public const float SPAWN_RING_PADDING_SIM = 1.35f;   // spawns outside perimeter ring
        public const float BASE_ENEMY_SPEED_SIM = 0.55f;     // ~4.6s from Range0(3.0) to Core(0.45)
        public const float ENEMY_MELEE_HIT_INTERVAL = 2.0f;  // 0.5 hits/sec per touching enemy
        public const float RANGED_FIRE_INTERVAL = 2.0f;
        public const float PROJECTILE_SPEED_SIM = 11.5f;
        public const float ORB_HITBOX_RADIUS_SIM = 0.24f;

        public const int MAX_ACTIVE_ENEMIES_TOTAL = 150;
        public const int MAX_ACTIVE_NORMAL = 120;
        public const int MAX_ACTIVE_ELITE = 20;
        public const int MAX_ACTIVE_BOSS = 10;

        public const double STARTING_RUN_CASH = 35.0;
        public const double PROTOTYPE_THORNS_RUN1 = 0.08;    // 8% emergency thorns triggered on Run 1 W3

        // Gem Drone real-time cadence (+5 Gems every ~165s real play)
        public const float GEM_DRONE_INTERVAL_REAL_SEC = 165.0f;
        public const float GEM_DRONE_LIFETIME_REAL_SEC = 18.0f;
        public const int GEM_DRONE_REWARD = 5;

        // Golden Bot parameters (unlocked at T1 W50)
        public const float GOLDEN_BOT_COOLDOWN_SEC = 25.0f;
        public const float GOLDEN_BOT_DURATION_SEC = 10.0f;
        public const float GOLDEN_BOT_RADIUS_SIM = 1.25f;
        public const double GOLDEN_BOT_COIN_MULT = 2.0;

        // Skin bonus (unlocked at T1 W80)
        public const double MOLTEN_CORE_COIN_BONUS = 1.02;

        // Cards & Gacha constants
        public const int CARD_PULL_COST_GEMS = 20;
        public const int MAX_CARD_SLOTS = 4;
        public const int MAX_CARD_COPIES = 5; // 1 copy = 1*, 2 copies = 2*, 5 copies = 3*
        public static readonly int[] CardSlotUnlockGems = { 0, 40, 80, 140 };
        public static readonly CardId[] DeterministicFirstPulls =
        {
            CardId.EnemyBalance,
            CardId.Coins,
            CardId.PlasmaCannon
        };

        // Labs & Cells constants
        public const int MAX_LAB_SLOTS = 3;
        public static readonly int[] LabSlotUnlockGems = { 0, 50, 150 };
        public const int CELLS_COST_BOOST_2X = 3;
        public const int CELLS_COST_BOOST_3X = 8;
        public const float CELLS_BOOST_DURATION_REAL_SEC = 900.0f; // 15 min real time

        // Ultimate Weapons unlock & upgrade Stone costs
        public static readonly int[] UwUnlockStoneCosts = { 5, 20, 50 };
        public static readonly int[] UwUpgradeStoneCosts = { 5, 10, 15, 25, 40 };
        public const float UW_SYNCED_COOLDOWN_SEC = 75.0f;

        // Multishot Targets & Orbs explicit cash/coin cost tables
        private static readonly int[] MultishotTargetsCash = { 80, 200, 480, 1200, 3200, 8000, 18000 };
        private static readonly int[] MultishotTargetsCoins = { 100, 260, 650, 1600, 4000, 9500, 22000 };
        private static readonly int[] OrbsCountCash = { 110, 380, 1100, 3200 };
        private static readonly int[] OrbsCountCoins = { 140, 480, 1400, 3800 };

        // Game Speed levels unlocked by LabId.GameSpeed (L0..L4)
        private static readonly float[] GameSpeedLabValues = { 1.0f, 2.7f, 3.5f, 4.3f, 5.0f };
        private static readonly int[] GameSpeedLabCoins = { 100, 320, 1200, 5200 };
        private static readonly float[] GameSpeedLabDurations = { 40f, 82f, 150f, 300f };

        // --- 22 Canonical Stats (indexed by (int)StatId) ---
        public static readonly StatDef[] Stats = new StatDef[GameIdsAndScale.STAT_COUNT]
        {
            // 0..6: ATTACK
            new StatDef(StatId.Damage, StatTab.Attack, UnlockGroup.AlwaysOpen, "Калибр пушки", "", 6000, 0,
                10, 0.34, 2.18, 1.8,   30, 2.2, 2.20, 18),
            new StatDef(StatId.AttackSpeed, StatTab.Attack, UnlockGroup.AlwaysOpen, "Темп стрельбы", "/с", 99, 0,
                5, 0.50, 2.16, 1.8,    30, 2.8, 2.18, 15),
            new StatDef(StatId.CritChance, StatTab.Attack, UnlockGroup.Crit, "Шанс крита", "%", 78, 25,
                4, 0.35, 2.20, 2.2,    40, 2.4, 2.18, 14),
            new StatDef(StatId.CritFactor, StatTab.Attack, UnlockGroup.Crit, "Крит-множитель", "×", 150, 25,
                10, 0.32, 2.16, 2.4,   40, 2.2, 2.16, 14),
            new StatDef(StatId.Range, StatTab.Attack, UnlockGroup.Range, "Магнитный периметр", "м", 40, 40,
                10, 0.55, 2.20, 4.5,   40, 3.0, 2.20, 18),
            new StatDef(StatId.MultishotChance, StatTab.Attack, UnlockGroup.Multishot, "Шанс дроби", "%", 99, 80,
                10, 0.75, 2.20, 5.0,   50, 3.2, 2.20, 20),
            new StatDef(StatId.MultishotTargets, StatTab.Attack, UnlockGroup.Multishot, "Цели дроби", "", 7, 80,
                125, 1, 1, 0,          300, 1, 1, 0),

            // 7..16: DEFENSE
            new StatDef(StatId.Health, StatTab.Defense, UnlockGroup.AlwaysOpen, "Прочность ядра", "", 6000, 0,
                10, 0.34, 2.16, 2.4,   30, 2.1, 2.18, 16),
            new StatDef(StatId.HealthRegen, StatTab.Defense, UnlockGroup.AlwaysOpen, "Автосварка", "/с", 6000, 0,
                5, 0.32, 2.15, 2.0,    25, 2.0, 2.16, 12),
            new StatDef(StatId.DefenseAbsolute, StatTab.Defense, UnlockGroup.DefenseAbs, "Бронеплита", "", 5000, 20,
                5, 0.85, 2.34, 4.2,    40, 2.6, 2.22, 18),
            new StatDef(StatId.DefensePercent, StatTab.Defense, UnlockGroup.DefensePct, "Демпфер", "%", 99, 20,
                6, 0.65, 2.22, 5.0,    40, 2.8, 2.20, 18),
            new StatDef(StatId.Thorns, StatTab.Defense, UnlockGroup.Thorns, "Карбидные зубья", "%", 74, 30,
                8, 0.75, 2.22, 5.0,    40, 2.8, 2.20, 18),
            new StatDef(StatId.KnockbackChance, StatTab.Defense, UnlockGroup.Knockback, "Шанс отбойника", "%", 80, 120,
                10, 0.85, 2.20, 5.5,   60, 3.4, 2.20, 22),
            new StatDef(StatId.KnockbackForce, StatTab.Defense, UnlockGroup.Knockback, "Импульс отбоя", "", 40, 120,
                10, 1.10, 2.25, 7.0,   60, 4.2, 2.24, 26),
            new StatDef(StatId.OrbsCount, StatTab.Defense, UnlockGroup.Orbs, "Плазмопилы", " шт.", 4, 150,
                250, 1, 1, 0,          400, 1, 1, 0),
            new StatDef(StatId.OrbSpeed, StatTab.Defense, UnlockGroup.Orbs, "Привод пил", "", 26, 150,
                15, 1.60, 2.30, 12.0,  100, 6.0, 2.28, 40),
            new StatDef(StatId.Lifesteal, StatTab.Defense, UnlockGroup.Lifesteal, "Энергоотсос", "%", 60, 150,
                10, 0.95, 2.22, 6.5,   50, 3.5, 2.22, 24),

            // 17..21: UTILITY
            new StatDef(StatId.CashBonus, StatTab.Utility, UnlockGroup.AlwaysOpen, "Выход лома $", "×", 75, 0,
                10, 0.65, 2.20, 3.5,   30, 2.4, 2.18, 16),
            new StatDef(StatId.CashPerWave, StatTab.Utility, UnlockGroup.CashWave, "Конвейер $/волна", "$", 100, 20,
                14, 0.80, 2.20, 4.5,   30, 2.5, 2.18, 16),
            new StatDef(StatId.CoinsPerKill, StatTab.Utility, UnlockGroup.Coins, "Золото / фраг", "×", 75, 30,
                10, 0.70, 2.20, 4.0,   40, 2.6, 2.20, 18),
            new StatDef(StatId.CoinsPerWave, StatTab.Utility, UnlockGroup.Coins, "Золото / волна", "", 75, 30,
                12, 0.90, 2.20, 5.0,   40, 2.8, 2.20, 18),
            new StatDef(StatId.FreeUpgrades, StatTab.Utility, UnlockGroup.FreeUp, "Автосборщик", "%", 80, 250,
                14, 1.15, 2.22, 7.5,   75, 4.0, 2.22, 28)
        };

        // --- 6 Enemy Definitions (indexed by (int)EnemyType) ---
        public static readonly EnemyTypeDef[] Enemies = new EnemyTypeDef[GameIdsAndScale.ENEMY_TYPE_COUNT]
        {
            new EnemyTypeDef(EnemyType.Basic,   "Гаджет-куб",       1.0,  1.0,  1.00f,  1.00f, 1.0,  1, 0.14f, false, 1.0),
            new EnemyTypeDef(EnemyType.Fast,    "Турбо-батарейка",  1.0,  1.0,  2.10f,  1.00f, 1.5,  2, 0.13f, false, 1.0),
            new EnemyTypeDef(EnemyType.Tank,    "Чугунный сейф",    5.0,  1.0,  0.60f,  4.86f, 3.0,  4, 0.22f, false, 1.0),
            new EnemyTypeDef(EnemyType.Ranged,  "Конденсатор",      1.0,  0.65, 1.20f,  1.00f, 1.5,  2, 0.15f, false, 1.0),
            new EnemyTypeDef(EnemyType.Boss,    "Титановый ATM",   20.0,  1.45, 0.40f, 12.38f, 5.0, 12, 0.36f, true,  0.5),
            new EnemyTypeDef(EnemyType.Vampire, "Энергоклещ",       2.8,  1.0,  0.60f,  2.50f, 3.0,  8, 0.20f, true,  1.0)
        };

        // --- 8 Signature Cards (indexed by (int)CardId) ---
        public static readonly CardDef[] Cards = new CardDef[GameIdsAndScale.CARD_COUNT]
        {
            new CardDef(CardId.EnemyBalance,    CardRarity.Common, "Баланс роя",       "Врагов ×1.8, лом Cash ×1.4/1.6/1.8",      1.40f, 1.60f, 1.80f),
            new CardDef(CardId.Coins,           CardRarity.Common, "Золотой чип",      "Все монеты ×1.25/1.35/1.45",              1.25f, 1.35f, 1.45f),
            new CardDef(CardId.Damage,          CardRarity.Common, "Вольфрам",         "Урон пушки ×1.4/1.7/2.0",                 1.40f, 1.70f, 2.00f),
            new CardDef(CardId.Health,          CardRarity.Common, "Армирование",      "Прочность ядра ×1.4/1.7/2.0",             1.40f, 1.70f, 2.00f),
            new CardDef(CardId.ExtraOrb,        CardRarity.Rare,   "Внешняя пила",     "+1 Плазмопила в противоход",               1.00f, 1.20f, 1.40f),
            new CardDef(CardId.PlasmaCannon,    CardRarity.Rare,   "Плазма-рельс",     "Снимает 22%/28%/34% HP Босса на входе",    0.22f, 0.28f, 0.34f),
            new CardDef(CardId.DeathRay,        CardRarity.Epic,   "Луч смерти",       "Плазмарез раз в 25с на 3.5/4.5/5.5с",      3.50f, 4.50f, 5.50f),
            new CardDef(CardId.WaveAccelerator, CardRarity.Epic,   "Ускоритель волн",  "Пауза между волнами -25%/-32%/-40%",       0.25f, 0.32f, 0.40f)
        };

        // --- 6 Labs (indexed by (int)LabId) ---
        public static readonly LabDef[] Labs = new LabDef[GameIdsAndScale.LAB_COUNT]
        {
            new LabDef(LabId.GameSpeed,          "Форсаж времени",     "Скорость игры до ×5.0",                    4, 100, 3.2f, 45f, 2.0f),
            new LabDef(LabId.MagneticGarlic,     "Магнитный чеснок",   "Жжёт Энергоклещей долей зубьев и тянет",   5, 180, 1.9f, 55f, 1.55f),
            new LabDef(LabId.CoinsPerKill,       "Аффинаж золота",     "Монеты за фраг ×1.06 за уровень",         10, 120, 1.65f, 45f, 1.35f),
            new LabDef(LabId.CarbideOverdrive,   "Карбидный форсаж",   "Урон ×1.08 и +2% зубьев по Боссам",       10, 120, 1.65f, 45f, 1.35f),
            new LabDef(LabId.OrbAndRotorSpeed,   "Обороты пил",        "+10% к скорости вращения Плазмопил",       5, 220, 1.9f, 60f, 1.50f),
            new LabDef(LabId.BlackHoleCoinBonus, "Грави-плавка BH",    "Монеты внутри Чёрной Дыры +0.5×/ур.",      5, 350, 2.0f, 75f, 1.50f)
        };

        // --- 3 Ultimate Weapons (indexed by (int)UwId) ---
        public static readonly UwDef[] Uws = new UwDef[GameIdsAndScale.UW_COUNT]
        {
            new UwDef(UwId.GoldenTower,    "Золотая плавильня",  "×5.0+ Cash и Coins на 15с+ (КД 90с)", 5),
            new UwDef(UwId.BlackHole,      "Электромагнит BH",   "Стягивает рой на пилы, ×1.5+ Coins (КД 90с)", 5),
            new UwDef(UwId.ChainLightning, "Цепная дуга",        "Разряд по 3–5 целям на 500%+ урона", 5)
        };

        // --- 12 Tier 1 Milestones ---
        public static readonly MilestoneDef[] Tier1Milestones = new MilestoneDef[]
        {
            new MilestoneDef(5,    80,   0,  0, MilestoneUnlockFlags.Speed15x,        "Форсаж ×1.5 открыт!"),
            new MilestoneDef(10,  210,  20,  0, MilestoneUnlockFlags.Workshop,        "Ангар модификаций открыт!"),
            new MilestoneDef(15,  520,   0,  0, MilestoneUnlockFlags.Labs,            "Лаборатория (Слот 1) открыта!"),
            new MilestoneDef(20,  750,  60,  0, MilestoneUnlockFlags.Cards,           "Модульные Карты открыты!"),
            new MilestoneDef(30, 1400,  70,  5, MilestoneUnlockFlags.UltimateWeapons, "Абсолютное Оружие #1 (5 Stones)!"),
            new MilestoneDef(40, 2500,  80,  0, MilestoneUnlockFlags.None,            "Резерв: +2500 Coins, +80 Gems"),
            new MilestoneDef(50, 4500, 120, 25, MilestoneUnlockFlags.GoldenBot,       "Ульта #2 + Золотой Дрон открыты!"),
            new MilestoneDef(60, 7000, 150,  0, MilestoneUnlockFlags.None,            "Резерв: +7000 Coins, +150 Gems"),
            new MilestoneDef(70, 10500,  0, 15, MilestoneUnlockFlags.None,            "Реакторные камни: +15 Stones"),
            new MilestoneDef(80, 14000, 200, 0, MilestoneUnlockFlags.MoltenCoreSkin,  "Скин «Molten Core» (+2% Coins)!"),
            new MilestoneDef(90, 20000,  0, 20, MilestoneUnlockFlags.None,            "Финальный заряд: +20 Stones"),
            new MilestoneDef(100, 30000, 250, 30, MilestoneUnlockFlags.Tier2,         "TIER 2 ОТКРЫТ (×20 HP / ×1.8 Coins)!")
        };

        // --- Stat Value & Cost Calculations ---

        public static double GetStatValue(StatId id, int level)
        {
            if (level < 0) level = 0;
            switch (id)
            {
                case StatId.Damage:
                    if (level == 0) return 3.6;
                    double baseDmg = 3.6 + 3.0 * level + 0.15 * Math.Pow(level, 2.08);
                    if (level > 18) baseDmg += 0.62 * Math.Pow(level - 18, 2.37);
                    return baseDmg;
                case StatId.AttackSpeed:
                    return 1.15 + 0.058 * level;
                case StatId.CritChance:
                    return Math.Min(0.85, 0.02 + 0.012 * level);
                case StatId.CritFactor:
                    return 1.20 + 0.14 * level;
                case StatId.Range:
                    return GameIdsAndScale.RANGE_0_SIM + 0.05 * level;
                case StatId.MultishotChance:
                    return Math.Min(0.55, 0.006 * level);
                case StatId.MultishotTargets:
                    return 2 + level;
                case StatId.Health:
                    if (level == 0) return 22.0;
                    double baseHp = 22.0 + 10.5 * level + 0.21 * Math.Pow(level, 2.27);
                    if (level > 32) baseHp += 2.60 * Math.Pow(level - 32, 2.35);
                    return baseHp;
                case StatId.HealthRegen:
                    return 0.18 + 0.40 * level + 0.015 * Math.Pow(level, 2.15);
                case StatId.DefenseAbsolute:
                    if (level == 0) return 0.0;
                    double baseDefAbs = 3.20 + 2.50 * (level - 1) + 0.026 * Math.Pow(level, 2.20);
                    if (level > 32) baseDefAbs += 0.45 * Math.Pow(level - 32, 2.25);
                    return baseDefAbs;
                case StatId.DefensePercent:
                    return Math.Min(0.495, 0.0050 * level);
                case StatId.Thorns:
                    if (level <= 0) return 0.0;
                    if (level <= 15) return 0.09 + 0.03 * level; // L1=12% .. L15=54%
                    return Math.Min(0.99, 0.54 + 0.015 * (level - 15)); // up to 99% at L45
                case StatId.KnockbackChance:
                    return Math.Min(0.80, 0.016 * level);
                case StatId.KnockbackForce:
                    return 0.50 + 0.16 * level;
                case StatId.OrbsCount:
                    return level; // 0..4
                case StatId.OrbSpeed:
                    return 0.10 + 0.024 * level;
                case StatId.Lifesteal:
                    return Math.Min(0.35, 0.0055 * level); // up to 35% for bullet/lifesteal survivability
                case StatId.CashBonus:
                    return 1.00 + 0.03 * level;
                case StatId.CashPerWave:
                    return 10.0 * level;
                case StatId.CoinsPerKill:
                    return 1.00 + 0.035 * level;
                case StatId.CoinsPerWave:
                    return 4.0 + 4.0 * level;
                case StatId.FreeUpgrades:
                    return Math.Min(0.50, 0.007 * level);
                default:
                    return 0.0;
            }
        }

        public static double GetCashCost(StatId id, int currentLevel)
        {
            ref readonly StatDef def = ref Stats[(int)id];
            if (currentLevel >= def.MaxLevel) return double.PositiveInfinity;
            if (id == StatId.MultishotTargets)
                return MultishotTargetsCash[Math.Clamp(currentLevel, 0, MultishotTargetsCash.Length - 1)];
            if (id == StatId.OrbsCount)
                return OrbsCountCash[Math.Clamp(currentLevel, 0, OrbsCountCash.Length - 1)];

            int L = currentLevel + 1;
            double raw = def.CashBase + def.CashA * Math.Pow(L, def.CashB) + def.CashC * L;
            double step = L <= 18 ? 1.0 : Math.Pow(L / 18.0, 0.66);
            if (L > 50) step *= Math.Pow(L / 50.0, 1.15);
            return Math.Round(raw * step);
        }

        public static double GetCoinCost(StatId id, int currentLevel)
        {
            ref readonly StatDef def = ref Stats[(int)id];
            if (currentLevel >= def.MaxLevel) return double.PositiveInfinity;
            if (id == StatId.MultishotTargets)
                return MultishotTargetsCoins[Math.Clamp(currentLevel, 0, MultishotTargetsCoins.Length - 1)];
            if (id == StatId.OrbsCount)
                return OrbsCountCoins[Math.Clamp(currentLevel, 0, OrbsCountCoins.Length - 1)];

            int L = currentLevel + 1;
            double raw = def.CoinBase * 0.55 + def.CoinA * 0.40 * Math.Pow(L, 1.92) + def.CoinC * 0.36 * L;
            double step = L < 50 ? 1.0 : (L < 85 ? 1.15 : (L < 120 ? 1.38 : 1.75));
            return Math.Max(10.0, Math.Round(raw * step));
        }

        // --- Wave Spawns, Composition & Scaling (v29.1.4 + 60m V1 Pressure) ---

        public static int GetWaveSpawnCount(int wave, bool hasEnemyBalance)
        {
            float spawnPct, doublePct;
            if (wave <= 2) { spawnPct = 10.0f; doublePct = 0.0f; }
            else if (wave <= 5) { spawnPct = 11.0f; doublePct = 0.0f; }
            else if (wave <= 19) { spawnPct = 12.5f; doublePct = 0.3f; }
            else if (wave <= 39) { spawnPct = 14.5f; doublePct = 0.5f; }
            else if (wave <= 59) { spawnPct = 17.0f; doublePct = 0.8f; }
            else if (wave <= 79) { spawnPct = 18.5f; doublePct = 1.3f; }
            else if (wave <= 99) { spawnPct = 20.0f; doublePct = 1.5f; }
            else { spawnPct = 22.0f; doublePct = 2.0f; }

            float eb = hasEnemyBalance ? 1.8f : 1.0f;
            return (int)MathF.Round((1.0f + doublePct * 0.01f) * (spawnPct * 0.01f) * SPAWN_TICKS_PER_WAVE * eb);
        }

        public static void GetWaveComposition(int wave, out float fastProb, out float tankProb, out float rangedProb)
        {
            if (wave <= 5) { fastProb = 0.05f; tankProb = 0.02f; rangedProb = 0.00f; }
            else if (wave <= 19) { fastProb = 0.06f; tankProb = 0.04f; rangedProb = 0.01f; }
            else if (wave <= 39) { fastProb = 0.07f; tankProb = 0.06f; rangedProb = 0.02f; }
            else if (wave <= 59) { fastProb = 0.08f; tankProb = 0.07f; rangedProb = 0.03f; }
            else if (wave <= 79) { fastProb = 0.09f; tankProb = 0.08f; rangedProb = 0.04f; }
            else if (wave <= 99) { fastProb = 0.10f; tankProb = 0.08f; rangedProb = 0.05f; }
            else { fastProb = 0.10f; tankProb = 0.09f; rangedProb = 0.06f; }
        }

        public static float GetVampireSpawnChance(int wave)
        {
            if (wave < 25) return 0f;
            if (wave < 40) return 0.28f;
            if (wave < 70) return 0.48f;
            return 0.72f;
        }

        public static double GetTierDifficultyMult(int tier) => tier <= 1 ? 1.0 : 20.0;
        public static double GetTierCoinMult(int tier) => tier <= 1 ? 1.0 : 1.8;

        public static double GetWaveBaseHp(int w, int tier)
        {
            if (w < 1) w = 1;
            double ph = 1.0
                + 0.04 * (w / 5) + 0.05 * (w / 10) + 0.06 * (w / 25)
                + 0.08 * (w / 50) + 0.10 * (w / 60) + 0.18 * (w / 72)
                + 0.20 * (w / 83) + 0.21 * (w / 94) + 0.12 * (w / 100) + 0.10 * (w / 107);

            double ch = Math.Pow(1.035, w / 30)
                * Math.Pow(1.02, w / 60)
                * Math.Pow(1.025, w / 72)
                * Math.Pow(1.03, (w / 83) + (w / 94))
                * Math.Pow(1.02, (w / 100) + (w / 107));

            // Piecewise V1 pacing curve:
            // Stage 1 (w=1..28): smooth early ramp so Run 2 reaches W22+ and Run 3 clears W30 at ~15m
            // Stage 2 (w=29..51): mid-game step so W30->W50 takes ~3 runs (hitting W50 at ~34m)
            // Stage 3 (w=52..99): GT+BH farming climb to W100 at ~52-58m
            // Stage 4 (w>99): Titanium Overdrive wall capping 60m around W100-W103
            double v1Ramp = 1.0;
            if (w >= 14 && w <= 30)
            {
                v1Ramp *= 0.75;
            }
            else if (w > 30)
            {
                int w2 = Math.Min(w, 50) - 30;
                v1Ramp *= 0.75 * Math.Pow(1.056, w2);
                if (w > 50)
                {
                    int w3 = Math.Min(w, 100) - 50;
                    v1Ramp *= Math.Pow(1.028, w3);
                }
                if (w > 100)
                {
                    v1Ramp *= Math.Pow(1.900, w - 100);
                }
            }
            return ph * ch * (0.05 * Math.Pow(w, 2.13) + 0.8 * w + 1.5) * v1Ramp * GetTierDifficultyMult(tier);
        }

        public static double GetBossArmorHpMult(int w)
        {
            if (w <= 65) return 1.0;
            int wBoss = Math.Min(w, 100) - 65;
            return Math.Pow(1.062, wBoss);
        }

        public static double GetWaveBaseDamage(int w, int tier)
        {
            if (w < 1) w = 1;
            double pd = 1.0
                + 0.02 * (w / 5) + 0.025 * (w / 10) + 0.012 * (w / 25)
                + 0.017 * (w / 50) + 0.02 * (w / 100);

            double cd = Math.Pow(1.005, w / 30)
                * Math.Pow(1.01, (w / 72) + (w / 83) + (w / 94) + (w / 107));

            double v1DmgRamp = 1.0;
            if (w >= 14 && w <= 20)
            {
                v1DmgRamp *= 0.62;
            }
            else if (w >= 21 && w <= 29)
            {
                v1DmgRamp *= 0.65;
            }
            else if (w == 30)
            {
                v1DmgRamp *= 0.71;
            }
            else if (w > 30)
            {
                int w2 = Math.Min(w, 50) - 30;
                v1DmgRamp *= 0.65 * Math.Pow(1.161, w2);
                if (w > 50)
                {
                    int w3 = Math.Min(w, 75) - 50;
                    v1DmgRamp *= Math.Pow(1.025, w3);
                }
                if (w > 75)
                {
                    int wLate = Math.Min(w, 100) - 75;
                    v1DmgRamp *= Math.Pow(0.968, wLate);
                }
                if (w > 100)
                {
                    v1DmgRamp *= Math.Pow(1.900, w - 100);
                }
            }
            return 0.94 * pd * cd * (0.021 * Math.Pow(w, 2.007) + 0.16 * w + 1.07) * v1DmgRamp * GetTierDifficultyMult(tier);
        }

        public static double GetWaveBaseKillCash(int w)
        {
            if (w <= 11) return 0.95 + 0.03 * w;
            if (w <= 50) return 1.35 + 0.16 * (w - 11);
            return 7.59 + 0.30 * (w - 50);
        }

        public static double GetWaveCoinScale(int w)
        {
            if (w <= 50) return 1.05 + 0.090 * Math.Max(0, w - 1);
            if (w <= 65) return 5.46 + 0.24 * (w - 50);
            return 9.06 + 0.52 * (w - 65);
        }

        // --- Cards, Labs & UW Helpers ---

        public static int GetCardStarFromCopies(int copies)
        {
            if (copies <= 0) return 0;
            if (copies == 1) return 1;
            if (copies < 5) return 2;
            return 3;
        }

        public static float GetMaxUnlockedGameSpeed(int gameSpeedLabLevel, bool speed15MilestoneUnlocked, bool workshopMilestoneUnlocked = false)
        {
            if (gameSpeedLabLevel >= 1)
            {
                int idx = Math.Clamp(gameSpeedLabLevel, 0, GameSpeedLabValues.Length - 1);
                return GameSpeedLabValues[idx];
            }
            if (workshopMilestoneUnlocked) return 2.0f;
            return speed15MilestoneUnlocked ? 1.6f : 1.0f;
        }

        /// <summary>
        /// Fix #7 (#20): Run 1 before W5 (unlocked == 1.0x) runs at 1.25f effective timeScale; 1.5x+ uses exact speed.
        /// </summary>
        public static float GetEffectiveTimeScale(float selectedSpeed, float maxUnlockedSpeed)
        {
            float clamped = MathF.Min(selectedSpeed, maxUnlockedSpeed);
            if (maxUnlockedSpeed <= 1.01f)
                return 1.25f;
            return MathF.Max(1.0f, clamped);
        }

        public static int GetLabCoinCost(LabId id, int currentLevel)
        {
            ref readonly LabDef def = ref Labs[(int)id];
            if (currentLevel >= def.MaxLevel) return int.MaxValue;
            if (id == LabId.GameSpeed)
                return GameSpeedLabCoins[Math.Clamp(currentLevel, 0, GameSpeedLabCoins.Length - 1)];
            return (int)MathF.Round(def.BaseCoins * MathF.Pow(def.CoinGrowth, currentLevel));
        }

        public static float GetLabDurationSeconds(LabId id, int currentLevel)
        {
            ref readonly LabDef def = ref Labs[(int)id];
            if (currentLevel >= def.MaxLevel) return 0f;
            if (id == LabId.GameSpeed)
                return GameSpeedLabDurations[Math.Clamp(currentLevel, 0, GameSpeedLabDurations.Length - 1)];
            return MathF.Round(def.BaseDurationSec * MathF.Pow(def.DurationGrowth, currentLevel));
        }

        public static int GetLabRushGemCost(float remainingSeconds)
        {
            if (remainingSeconds <= 0f) return 0;
            return Math.Max(1, (int)MathF.Ceiling(remainingSeconds / 30.0f));
        }

        // UW Stat Values by upgrade level (0..5)
        public static double GetGoldenTowerMult(int level) => 5.5 + 0.9 * level;
        public static float GetGoldenTowerDuration(int level) => 15.0f + 2.0f * level;

        public static float GetBlackHoleRadiusSim(int level) => 1.15f + 0.14f * level; // ~32m+ equivalent
        public static float GetBlackHoleDuration(int level) => 15.0f + 2.0f * level;
        public static double GetBlackHoleCoinMult(int bhLabLevel) => 1.5 + 0.5 * bhLabLevel;

        public static float GetChainLightningChance(int level) => 0.24f + 0.04f * level;
        public static int GetChainLightningTargets(int level) => 4 + (level / 2);
        public static double GetChainLightningDamageMult(int level) => 8.5 + 2.5 * level;
    }
}
