namespace Crusher.Config
{
    /// <summary>
    /// Shared simulation-to-world scale and canonical identifier contract for Crusher Core V1.
    /// </summary>
    public static class GameIdsAndScale
    {
        /// <summary>1 simulation unit = 2.5 Unity meters (Range0 = 3.0 sim = 7.5m in 3D).</summary>
        public const float WORLD_SCALE = 2.5f;

        public const float RANGE_0_SIM = 3.0f;
        public const float RANGE_0_WORLD = RANGE_0_SIM * WORLD_SCALE;

        public const int STAT_COUNT = 22;
        public const int ENEMY_TYPE_COUNT = 6;
        public const int CARD_COUNT = 8;
        public const int LAB_COUNT = 6;
        public const int UW_COUNT = 3;
        public const int CORE_GRADE_COUNT = 4;
        public const int SFX_COUNT = 14;

        public static float SimToWorld(float simUnits) => simUnits * WORLD_SCALE;
        public static float WorldToSim(float worldMeters) => worldMeters / WORLD_SCALE;
    }

    public enum StatId
    {
        Damage,
        AttackSpeed,
        CritChance,
        CritFactor,
        Range,
        MultishotChance,
        MultishotTargets,
        Health,
        HealthRegen,
        DefenseAbsolute,
        DefensePercent,
        Thorns,
        KnockbackChance,
        KnockbackForce,
        OrbsCount,
        OrbSpeed,
        Lifesteal,
        CashBonus,
        CashPerWave,
        CoinsPerKill,
        CoinsPerWave,
        FreeUpgrades
    }

    public enum EnemyType
    {
        Basic,
        Fast,
        Tank,
        Ranged,
        Boss,
        Vampire
    }

    public enum CardId
    {
        EnemyBalance,
        Coins,
        Damage,
        Health,
        ExtraOrb,
        PlasmaCannon,
        DeathRay,
        WaveAccelerator
    }

    public enum LabId
    {
        GameSpeed,
        MagneticGarlic,
        CoinsPerKill,
        CarbideOverdrive,
        OrbAndRotorSpeed,
        BlackHoleCoinBonus
    }

    public enum UwId
    {
        GoldenTower,
        BlackHole,
        ChainLightning
    }

    public enum CoreVisualGrade
    {
        G0_Rusty,
        G1_SteelCarbide,
        G2_TitaniumTwin,
        G3_DiamondReactor
    }

    public enum SfxId
    {
        Crunch,
        BossShatter,
        RotorHumLoop,
        OrbSaw,
        PneumoShot,
        BlackHoleLoop,
        GoldenStart,
        IngotDing,
        ChainZap,
        PlasmaHit,
        UiTap,
        UiUpgrade,
        UiUnlock,
        OverloadDeath
    }
}
