using System;
using Crusher.Config;

namespace Crusher.Sim
{
    /// <summary>Zero-allocation deterministic Xorshift64* PRNG.</summary>
    public struct FastRng
    {
        public ulong State;

        public FastRng(ulong seed)
        {
            State = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
        }

        public uint NextUInt()
        {
            ulong x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            return (uint)((x * 0x2545F4914F6CDD1DUL) >> 32);
        }

        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public int NextRange(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }
    }

    public enum ProjectileKind : byte
    {
        TowerNeedle = 0,
        RangedEnemyBolt = 1,
        PlasmaCannonSlug = 2
    }

    public struct EnemyState
    {
        public int Id;
        public EnemyType Type;
        public float PosX;
        public float PosY;
        public float RadiusDist;
        public float Angle;
        public double Hp;
        public double MaxHp;
        public double BaseDamage;
        public float SpeedSim;
        public float Mass;
        public float HitboxRadius;
        public int ConsecutiveHits;
        public float AttackCooldown;
        public float RangedFireTimer;
        public float HitFlashTimer;
        public float SquashTimer;
        public bool IsInBlackHole;
        public bool IsVampireBeaming;
    }

    public struct ProjectileState
    {
        public bool Active;
        public ProjectileKind Kind;
        public int TargetEnemyId;
        public float PosX;
        public float PosY;
        public float TargetX;
        public float TargetY;
        public float SpeedSim;
        public double DamageOrPct;
        public bool IsCrit;
    }

    public struct OrbState
    {
        public bool Active;
        public bool IsExtraOuter;
        public float RadiusSim;
        public float Angle;
        public float PosX;
        public float PosY;
    }

    public struct ChainArcState
    {
        public float FromX;
        public float FromY;
        public float ToX;
        public float ToY;
        public float TimeRemaining;
    }

    public struct BlackHoleFieldState
    {
        public bool Unlocked;
        public bool Active;
        public float PosX;
        public float PosY;
        public float OrbitAngle;
        public float RadiusSim;
        public float TimeRemaining;
        public float CycleTimer;
    }

    public struct GoldenTowerState
    {
        public bool Unlocked;
        public bool Active;
        public double Multiplier;
        public float TimeRemaining;
        public float CycleTimer;
    }

    public struct DeathRayState
    {
        public bool Equipped;
        public bool Active;
        public float Angle;
        public float TimeRemaining;
        public float CycleTimer;
    }

    public struct GoldenBotState
    {
        public bool Unlocked;
        public bool PulseActive;
        public float PosX;
        public float PosY;
        public float TargetX;
        public float TargetY;
        public float RadiusSim;
        public float CycleTimer;
    }

    public struct GemDroneState
    {
        public bool Active;
        public float PosX;
        public float PosY;
        public float Angle;
        public float RealTimeRemaining;
        public float NextSpawnRealTimer;
    }

    public struct KillEvent
    {
        public EnemyType Type;
        public float PosX;
        public float PosY;
        public bool KilledByOrbOrRay;
        public bool KilledInGolden;
        public double CashEarned;
        public double CoinsEarned;
        public int CellsEarned;
    }

    public struct DamagePopupBucket
    {
        public bool Active;
        public float PosX;
        public float PosY;
        public double AccumulatedDamage;
        public bool HasCrit;
    }

    /// <summary>
    /// Cached combat stats recomputed whenever in-run upgrades, cards, or labs change.
    /// </summary>
    public struct ComputedTowerStats
    {
        public double Damage;
        public double AttackSpeed;
        public float CritChance;
        public double CritFactor;
        public float RangeSim;
        public float MultishotChance;
        public int MultishotTargets;
        public double MaxHp;
        public double HealthRegen;
        public double DefAbs;
        public double DefPercent;
        public double Thorns;
        public float KnockbackChance;
        public float KnockbackForce;
        public int OrbsCount;
        public float OrbAngularSpeedRad;
        public double Lifesteal;
        public double CashBonus;
        public double CashPerWave;
        public double CoinsPerKillMult;
        public double CoinsPerWave;
        public float FreeUpgradeChance;

        // Card / Lab / UW modifiers
        public bool HasEnemyBalance;
        public double EnemyBalanceCashMult;
        public double CardCoinMult;
        public bool HasExtraOrb;
        public float ExtraOrbSpeedMult;
        public float PlasmaCannonBossPct;
        public float DeathRayDuration;
        public float WaveCooldownSeconds;
        public int MagneticGarlicLevel;
        public double BossThornsBonus;
        public double BhCoinMult;
        public double GlobalAccountCoinMult;
    }

    /// <summary>
    /// Zero-allocation tick-by-tick combat arena simulation for Crusher Core V1.
    /// Shared identically by Unity 6 runtime and Headless Balance Verifier.
    /// </summary>
    public sealed class CombatSim
    {
        public const float MAX_SIM_SUBSTEP = 0.0625f; // 16 Hz minimum precision even at 5x speed
        public const int ENEMY_CAPACITY = 160;
        public const int PROJECTILE_CAPACITY = 256;
        public const int ORB_CAPACITY = 8;
        public const int CHAIN_ARC_CAPACITY = 32;
        public const int KILL_EVENT_CAPACITY = 128;
        public const int SFX_QUEUE_CAPACITY = 64;
        public const int DAMAGE_BUCKET_COUNT = 6;

        public readonly EnemyState[] Enemies = new EnemyState[ENEMY_CAPACITY];
        public int ActiveEnemyCount;
        public int ActiveNormalCount;
        public int ActiveEliteCount;
        public int ActiveBossCount;

        public readonly ProjectileState[] Projectiles = new ProjectileState[PROJECTILE_CAPACITY];
        public readonly OrbState[] Orbs = new OrbState[ORB_CAPACITY];
        public int ActiveOrbCount;

        public readonly ChainArcState[] ChainArcs = new ChainArcState[CHAIN_ARC_CAPACITY];
        public int ActiveChainArcCount;

        public BlackHoleFieldState BlackHole;
        public GoldenTowerState GoldenTower;
        public DeathRayState DeathRay;
        public GoldenBotState GoldenBot;
        public GemDroneState GemDrone;

        // Render & Audio Event Buffers (zero-alloc)
        public readonly KillEvent[] KillEvents = new KillEvent[KILL_EVENT_CAPACITY];
        public int KillEventCount;
        public readonly SfxId[] SfxQueue = new SfxId[SFX_QUEUE_CAPACITY];
        public int SfxCount;
        public readonly DamagePopupBucket[] DamageBuckets = new DamagePopupBucket[DAMAGE_BUCKET_COUNT];

        // Run Progress & Telemetry
        public FastRng Rng;
        public int Tier = 1;
        public int RunIndex; // 1-based run number
        public int CurrentWave = 1;
        public float WaveElapsed;
        public bool InWaveCooldown;
        public float CooldownRemaining;

        public double CoreHp;
        public double CoreMaxHp;
        public bool IsGameOver;
        public bool AnyVampireBeaming;
        public bool HeatingUpActive;
        public bool PrototypeThornsTriggered;
        public bool FtueThornsToastPending;

        // Economy & Telemetry for current run
        public double RunCashEarned;
        public double RunCoinsEarned;
        public double RunCoinsEarnedDuringGtBh;
        public int RunCellsEarned;
        public int RunTotalKills;
        public int PeakActiveEnemies;

        // Wave spawn state
        private int _nextEnemyId = 1;
        private int _targetNormalSpawns;
        private int _spawnedNormalThisWave;
        private float _spawnIntervalSec;
        private float _spawnAccumulator;
        private bool _bossSpawnedThisWave;
        private bool _vampireScheduledThisWave;
        private bool _vampireSpawnedThisWave;
        private float _fireCooldown;
        private float _orbBaseAngle;
        private float _extraOrbAngle;

        // Cached UW parameters
        private bool _chainUnlocked;
        private float _chainChance;
        private int _chainTargets;
        private double _chainDmgMult;
        private float _gtDuration;
        private float _bhDuration;

        // Callback flags polled by GameSession at wave transitions
        public bool PendingWaveEndPayout;
        public int PendingCompletedWave;

        // Pre-allocated scratch buffer for multishot target indices
        private readonly int[] _targetIndicesScratch = new int[16];

        public ComputedTowerStats Stats;

        public CombatSim(ulong seed = 1UL)
        {
            Rng = new FastRng(seed);
            GemDrone.NextSpawnRealTimer = 95.0f; // First gem drone around 1.5 min into game
        }

        public void ResetForNewRun(int tier, int runNumber1Based, in ComputedTowerStats initialStats, ulong runSeed)
        {
            Rng = new FastRng(runSeed);
            Tier = tier;
            RunIndex = runNumber1Based;
            Stats = initialStats;

            ActiveEnemyCount = 0;
            ActiveNormalCount = 0;
            ActiveEliteCount = 0;
            ActiveBossCount = 0;
            ActiveChainArcCount = 0;
            KillEventCount = 0;
            SfxCount = 0;

            for (int i = 0; i < PROJECTILE_CAPACITY; i++)
                Projectiles[i].Active = false;
            for (int i = 0; i < DAMAGE_BUCKET_COUNT; i++)
                DamageBuckets[i].Active = false;

            CoreMaxHp = initialStats.MaxHp;
            CoreHp = CoreMaxHp;
            IsGameOver = false;
            AnyVampireBeaming = false;
            HeatingUpActive = false;
            PrototypeThornsTriggered = false;
            FtueThornsToastPending = false;
            PendingWaveEndPayout = false;
            PendingCompletedWave = 0;

            RunCashEarned = 0.0;
            RunCoinsEarned = 0.0;
            RunCoinsEarnedDuringGtBh = 0.0;
            RunCellsEarned = 0;
            RunTotalKills = 0;
            PeakActiveEnemies = 0;

            _fireCooldown = 0.1f;
            _orbBaseAngle = 0f;
            _extraOrbAngle = MathF.PI * 0.5f;

            // Reset UWs with short initial trigger (~12s into run) so unlocked UWs feel responsive immediately
            GoldenTower.Active = false;
            GoldenTower.TimeRemaining = 0f;
            GoldenTower.CycleTimer = 12.0f;

            BlackHole.Active = false;
            BlackHole.TimeRemaining = 0f;
            BlackHole.CycleTimer = 12.0f; // 1:1 synced with Golden Tower!
            BlackHole.OrbitAngle = 0.6f;

            DeathRay.Active = false;
            DeathRay.TimeRemaining = 0f;
            DeathRay.CycleTimer = 10.0f;
            DeathRay.Angle = 0f;

            GoldenBot.PulseActive = false;
            GoldenBot.CycleTimer = 8.0f;
            GoldenBot.PosX = 1.2f;
            GoldenBot.PosY = 0.8f;
            GoldenBot.TargetX = -1.2f;
            GoldenBot.TargetY = 1.0f;
            GoldenBot.RadiusSim = BalanceTables.GOLDEN_BOT_RADIUS_SIM;

            RebuildOrbs();
            SetupWave(1);
        }

        public void UpdateStatsLive(
            in ComputedTowerStats newStats,
            bool gtUnlocked, int gtLevel,
            bool bhUnlocked, int bhLevel,
            bool clUnlocked, int clLevel,
            bool goldenBotUnlocked)
        {
            double oldMaxHp = CoreMaxHp;
            Stats = newStats;
            CoreMaxHp = newStats.MaxHp;
            if (CoreMaxHp > oldMaxHp && !IsGameOver)
            {
                CoreHp += (CoreMaxHp - oldMaxHp);
            }
            if (CoreHp > CoreMaxHp) CoreHp = CoreMaxHp;

            GoldenTower.Unlocked = gtUnlocked;
            GoldenTower.Multiplier = BalanceTables.GetGoldenTowerMult(gtLevel);
            _gtDuration = BalanceTables.GetGoldenTowerDuration(gtLevel);

            BlackHole.Unlocked = bhUnlocked;
            BlackHole.RadiusSim = BalanceTables.GetBlackHoleRadiusSim(bhLevel);
            _bhDuration = BalanceTables.GetBlackHoleDuration(bhLevel);

            _chainUnlocked = clUnlocked;
            _chainChance = BalanceTables.GetChainLightningChance(clLevel);
            _chainTargets = BalanceTables.GetChainLightningTargets(clLevel);
            _chainDmgMult = BalanceTables.GetChainLightningDamageMult(clLevel);

            DeathRay.Equipped = newStats.DeathRayDuration > 0f;
            GoldenBot.Unlocked = goldenBotUnlocked;

            RebuildOrbs();
        }

        public void ConsumeRenderAndAudioEvents()
        {
            KillEventCount = 0;
            SfxCount = 0;
            for (int i = 0; i < DAMAGE_BUCKET_COUNT; i++)
                DamageBuckets[i].Active = false;
        }

        public bool TryClaimGemDrone()
        {
            if (!GemDrone.Active) return false;
            GemDrone.Active = false;
            GemDrone.RealTimeRemaining = 0f;
            PushSfx(SfxId.IngotDing);
            return true;
        }

        public void Tick(float realUnscaledDt, float effectiveTimeScale)
        {
            if (IsGameOver || realUnscaledDt <= 0f) return;

            // Tick real-time Gem Drone timer
            TickGemDroneRealTime(realUnscaledDt);

            // Sub-step game time to prevent high-speed tunneling at 4.5x-5.0x
            float totalGameDt = realUnscaledDt * effectiveTimeScale;
            int steps = Math.Max(1, (int)MathF.Ceiling(totalGameDt / MAX_SIM_SUBSTEP));
            float subDt = totalGameDt / steps;

            for (int s = 0; s < steps; s++)
            {
                if (IsGameOver) break;
                StepSub(subDt);
            }
        }

        private void TickGemDroneRealTime(float realDt)
        {
            if (GemDrone.Active)
            {
                GemDrone.RealTimeRemaining -= realDt;
                GemDrone.Angle += 0.9f * realDt;
                float r = Stats.RangeSim * 0.72f;
                GemDrone.PosX = MathF.Cos(GemDrone.Angle) * r;
                GemDrone.PosY = MathF.Sin(GemDrone.Angle) * r;
                if (GemDrone.RealTimeRemaining <= 0f)
                {
                    GemDrone.Active = false;
                }
            }
            else
            {
                GemDrone.NextSpawnRealTimer -= realDt;
                if (GemDrone.NextSpawnRealTimer <= 0f)
                {
                    GemDrone.Active = true;
                    GemDrone.RealTimeRemaining = BalanceTables.GEM_DRONE_LIFETIME_REAL_SEC;
                    GemDrone.NextSpawnRealTimer = BalanceTables.GEM_DRONE_INTERVAL_REAL_SEC;
                    GemDrone.Angle = Rng.NextFloat() * MathF.PI * 2f;
                    float r = Stats.RangeSim * 0.72f;
                    GemDrone.PosX = MathF.Cos(GemDrone.Angle) * r;
                    GemDrone.PosY = MathF.Sin(GemDrone.Angle) * r;
                }
            }
        }

        private void SetupWave(int wave)
        {
            CurrentWave = wave;
            WaveElapsed = 0f;
            InWaveCooldown = false;
            CooldownRemaining = 0f;

            _targetNormalSpawns = BalanceTables.GetWaveSpawnCount(wave, Stats.HasEnemyBalance);
            _spawnedNormalThisWave = 0;
            _spawnIntervalSec = BalanceTables.WAVE_FIGHT_SECONDS / Math.Max(1, _targetNormalSpawns);
            _spawnAccumulator = _spawnIntervalSec * 0.5f;

            _bossSpawnedThisWave = false;
            float vampChance = BalanceTables.GetVampireSpawnChance(wave);
            _vampireScheduledThisWave = vampChance > 0f && Rng.NextFloat() < vampChance;
            _vampireSpawnedThisWave = false;
        }

        private void StepSub(float dt)
        {
            // 1. Wave progression & enemy spawning
            if (!InWaveCooldown)
            {
                WaveElapsed += dt;
                _spawnAccumulator += dt;
                while (_spawnedNormalThisWave < _targetNormalSpawns && _spawnAccumulator >= _spawnIntervalSec)
                {
                    _spawnAccumulator -= _spawnIntervalSec;
                    _spawnedNormalThisWave++;
                    TrySpawnNormalWaveEnemy();
                }

                // Boss at 25% of wave fight time on every 10th wave (and every 2nd wave during W102+ Titanium Overdrive)
                bool isBossWave = (CurrentWave % 10 == 0) || (CurrentWave >= 102 && CurrentWave % 2 == 0);
                if (!_bossSpawnedThisWave && isBossWave && WaveElapsed >= 6.5f)
                {
                    _bossSpawnedThisWave = true;
                    int bossIdx = TrySpawnEnemyOfType(EnemyType.Boss);
                    if (bossIdx >= 0 && Stats.PlasmaCannonBossPct > 0f)
                    {
                        FirePlasmaCannonAtBoss(ref Enemies[bossIdx]);
                    }
                }

                // Elite Vampire at 50% of wave fight time (W25+)
                if (_vampireScheduledThisWave && !_vampireSpawnedThisWave && WaveElapsed >= 13.0f)
                {
                    _vampireSpawnedThisWave = true;
                    TrySpawnEnemyOfType(EnemyType.Vampire);
                }

                if (WaveElapsed >= BalanceTables.WAVE_FIGHT_SECONDS)
                {
                    InWaveCooldown = true;
                    CooldownRemaining = Stats.WaveCooldownSeconds;
                }
            }
            else
            {
                CooldownRemaining -= dt;
                if (CooldownRemaining <= 0f)
                {
                    PendingWaveEndPayout = true;
                    PendingCompletedWave = CurrentWave;
                    SetupWave(CurrentWave + 1);
                }
            }

            // 2. Tick Ultimate Weapons, Death Ray & Golden Bot
            TickSpecialSystems(dt);

            // 3. Tick Orbs & check instant-kill collisions
            TickOrbs(dt);

            // 4. Tick Enemies (movement, BH pull, Vampire beam, Ranged & Melee attacks)
            TickEnemies(dt);

            // 5. Tick Turret firing & Projectiles
            TickTurretAndProjectiles(dt);

            // 6. Tick Chain Lightning visual arcs
            for (int i = ActiveChainArcCount - 1; i >= 0; i--)
            {
                ChainArcs[i].TimeRemaining -= dt;
                if (ChainArcs[i].TimeRemaining <= 0f)
                {
                    ChainArcs[i] = ChainArcs[ActiveChainArcCount - 1];
                    ActiveChainArcCount--;
                }
            }

            // 7. Core Regeneration & Peak tracking
            if (!AnyVampireBeaming && CoreHp > 0.0 && CoreHp < CoreMaxHp)
            {
                CoreHp = Math.Min(CoreMaxHp, CoreHp + Stats.HealthRegen * dt);
            }
            if (ActiveEnemyCount > PeakActiveEnemies)
            {
                PeakActiveEnemies = ActiveEnemyCount;
            }

            if (CoreHp <= 0.0)
            {
                CoreHp = 0.0;
                IsGameOver = true;
                PushSfx(SfxId.OverloadDeath);
            }
        }

        private void TickSpecialSystems(float dt)
        {
            // Synced Golden Tower (90s start-to-start cycle)
            if (GoldenTower.Unlocked)
            {
                GoldenTower.CycleTimer -= dt;
                if (GoldenTower.CycleTimer <= 0f)
                {
                    GoldenTower.CycleTimer += BalanceTables.UW_SYNCED_COOLDOWN_SEC;
                    GoldenTower.Active = true;
                    GoldenTower.TimeRemaining = _gtDuration;
                    PushSfx(SfxId.GoldenStart);
                }
                if (GoldenTower.Active)
                {
                    GoldenTower.TimeRemaining -= dt;
                    if (GoldenTower.TimeRemaining <= 0f) GoldenTower.Active = false;
                }
            }

            // Synced Black Hole (90s start-to-start cycle; aligns 1:1 with GT when both unlocked!)
            if (BlackHole.Unlocked)
            {
                // If GT is also unlocked and BH just unlocked mid-session, keep phase locked to GT
                if (GoldenTower.Unlocked && MathF.Abs(BlackHole.CycleTimer - GoldenTower.CycleTimer) > 0.5f && !BlackHole.Active && !GoldenTower.Active)
                {
                    BlackHole.CycleTimer = GoldenTower.CycleTimer;
                }

                BlackHole.CycleTimer -= dt;
                if (BlackHole.CycleTimer <= 0f)
                {
                    BlackHole.CycleTimer += BalanceTables.UW_SYNCED_COOLDOWN_SEC;
                    BlackHole.Active = true;
                    BlackHole.TimeRemaining = _bhDuration;
                }
                if (BlackHole.Active)
                {
                    BlackHole.OrbitAngle += 0.18f * dt;
                    float bhOrbitR = Stats.RangeSim * 0.95f; // Sits directly on Plasma Saw ring!
                    BlackHole.PosX = MathF.Cos(BlackHole.OrbitAngle) * bhOrbitR;
                    BlackHole.PosY = MathF.Sin(BlackHole.OrbitAngle) * bhOrbitR;

                    BlackHole.TimeRemaining -= dt;
                    if (BlackHole.TimeRemaining <= 0f) BlackHole.Active = false;
                }
            }

            // Death Ray Card (every 25s)
            if (DeathRay.Equipped)
            {
                DeathRay.CycleTimer -= dt;
                if (DeathRay.CycleTimer <= 0f)
                {
                    DeathRay.CycleTimer += 25.0f;
                    DeathRay.Active = true;
                    DeathRay.TimeRemaining = Stats.DeathRayDuration;
                }
                if (DeathRay.Active)
                {
                    DeathRay.Angle = WrapAngle(DeathRay.Angle + 3.4f * dt);
                    DeathRay.TimeRemaining -= dt;
                    if (DeathRay.TimeRemaining <= 0f) DeathRay.Active = false;
                }
            }

            // Golden Bot (Milestone W50)
            if (GoldenBot.Unlocked)
            {
                GoldenBot.CycleTimer -= dt;
                if (GoldenBot.CycleTimer <= 0f)
                {
                    GoldenBot.CycleTimer += BalanceTables.GOLDEN_BOT_COOLDOWN_SEC;
                }
                GoldenBot.PulseActive = GoldenBot.CycleTimer > (BalanceTables.GOLDEN_BOT_COOLDOWN_SEC - BalanceTables.GOLDEN_BOT_DURATION_SEC);

                float dx = GoldenBot.TargetX - GoldenBot.PosX;
                float dy = GoldenBot.TargetY - GoldenBot.PosY;
                float distSq = dx * dx + dy * dy;
                if (distSq < 0.09f)
                {
                    float ang = Rng.NextFloat() * MathF.PI * 2f;
                    float rad = Stats.RangeSim * (0.45f + 0.45f * Rng.NextFloat());
                    GoldenBot.TargetX = MathF.Cos(ang) * rad;
                    GoldenBot.TargetY = MathF.Sin(ang) * rad;
                }
                else
                {
                    float invDist = 1.0f / MathF.Sqrt(distSq);
                    GoldenBot.PosX += dx * invDist * 0.85f * dt;
                    GoldenBot.PosY += dy * invDist * 0.85f * dt;
                }
            }
        }

        private void RebuildOrbs()
        {
            ActiveOrbCount = 0;
            int inner = Stats.OrbsCount;
            float innerR = Stats.RangeSim * 0.95f;
            for (int i = 0; i < inner && ActiveOrbCount < ORB_CAPACITY; i++)
            {
                float a = _orbBaseAngle + (MathF.PI * 2f * i) / inner;
                Orbs[ActiveOrbCount++] = new OrbState
                {
                    Active = true,
                    IsExtraOuter = false,
                    RadiusSim = innerR,
                    Angle = a,
                    PosX = MathF.Cos(a) * innerR,
                    PosY = MathF.Sin(a) * innerR
                };
            }

            if (Stats.HasExtraOrb && ActiveOrbCount < ORB_CAPACITY)
            {
                float outerR = Stats.RangeSim * 1.06f;
                Orbs[ActiveOrbCount++] = new OrbState
                {
                    Active = true,
                    IsExtraOuter = true,
                    RadiusSim = outerR,
                    Angle = _extraOrbAngle,
                    PosX = MathF.Cos(_extraOrbAngle) * outerR,
                    PosY = MathF.Sin(_extraOrbAngle) * outerR
                };
            }
        }

        private void TickOrbs(float dt)
        {
            if (ActiveOrbCount == 0 && !DeathRay.Active) return;

            _orbBaseAngle = WrapAngle(_orbBaseAngle + Stats.OrbAngularSpeedRad * dt);
            _extraOrbAngle = WrapAngle(_extraOrbAngle - Stats.OrbAngularSpeedRad * Stats.ExtraOrbSpeedMult * dt);

            int innerCount = Stats.OrbsCount;
            float innerR = Stats.RangeSim * 0.95f;
            int idx = 0;
            for (int i = 0; i < innerCount && idx < ActiveOrbCount; i++, idx++)
            {
                float a = _orbBaseAngle + (MathF.PI * 2f * i) / innerCount;
                Orbs[idx].RadiusSim = innerR;
                Orbs[idx].Angle = a;
                Orbs[idx].PosX = MathF.Cos(a) * innerR;
                Orbs[idx].PosY = MathF.Sin(a) * innerR;
            }
            if (Stats.HasExtraOrb && idx < ActiveOrbCount)
            {
                float outerR = Stats.RangeSim * 1.06f;
                Orbs[idx].RadiusSim = outerR;
                Orbs[idx].Angle = _extraOrbAngle;
                Orbs[idx].PosX = MathF.Cos(_extraOrbAngle) * outerR;
                Orbs[idx].PosY = MathF.Sin(_extraOrbAngle) * outerR;
            }

            // Check Orb & Death Ray instant-kills on non-immune enemies
            for (int e = ActiveEnemyCount - 1; e >= 0; e--)
            {
                ref EnemyState en = ref Enemies[e];
                if (BalanceTables.Enemies[(int)en.Type].ImmuneToOrbsAndBh)
                    continue;

                bool killed = false;
                for (int o = 0; o < ActiveOrbCount; o++)
                {
                    float dx = en.PosX - Orbs[o].PosX;
                    float dy = en.PosY - Orbs[o].PosY;
                    float hitR = BalanceTables.ORB_HITBOX_RADIUS_SIM + en.HitboxRadius;
                    if (dx * dx + dy * dy <= hitR * hitR)
                    {
                        killed = true;
                        PushSfx(SfxId.OrbSaw);
                        break;
                    }
                }

                if (!killed && DeathRay.Active && en.RadiusDist <= Stats.RangeSim * 1.35f)
                {
                    float diff = MathF.Abs(WrapAngleSigned(en.Angle - DeathRay.Angle));
                    if (diff <= 0.14f)
                    {
                        killed = true;
                    }
                }

                if (killed)
                {
                    KillEnemyAt(e, killedByOrbOrRay: true);
                }
            }
        }

        private void TickEnemies(float dt)
        {
            AnyVampireBeaming = false;
            HeatingUpActive = false;

            for (int i = ActiveEnemyCount - 1; i >= 0; i--)
            {
                ref EnemyState en = ref Enemies[i];
                if (en.HitFlashTimer > 0f) en.HitFlashTimer = MathF.Max(0f, en.HitFlashTimer - dt);
                if (en.SquashTimer > 0f) en.SquashTimer = MathF.Max(0f, en.SquashTimer - dt);

                en.IsInBlackHole = false;
                en.IsVampireBeaming = false;

                bool immune = BalanceTables.Enemies[(int)en.Type].ImmuneToOrbsAndBh;

                // Black Hole pull & 2%/s vortex damage
                if (BlackHole.Active && !immune)
                {
                    float dx = BlackHole.PosX - en.PosX;
                    float dy = BlackHole.PosY - en.PosY;
                    float distSq = dx * dx + dy * dy;
                    float pullRange = BlackHole.RadiusSim * 1.65f;
                    if (distSq <= pullRange * pullRange)
                    {
                        float dist = MathF.Sqrt(MathF.Max(0.0001f, distSq));
                        if (dist <= BlackHole.RadiusSim)
                        {
                            en.IsInBlackHole = true;
                            en.Hp -= en.MaxHp * 0.02 * dt;
                            if (en.Hp <= 0.0)
                            {
                                KillEnemyAt(i, killedByOrbOrRay: false);
                                continue;
                            }
                        }
                        float pullStep = MathF.Min(dist, 2.8f * dt);
                        en.PosX += (dx / dist) * pullStep;
                        en.PosY += (dy / dist) * pullStep;
                        en.RadiusDist = MathF.Sqrt(en.PosX * en.PosX + en.PosY * en.PosY);
                        en.Angle = MathF.Atan2(en.PosY, en.PosX);
                    }
                }

                // Type-specific movement & combat
                float contactRadius = BalanceTables.CORE_RADIUS_SIM + en.HitboxRadius;

                if (en.Type == EnemyType.Vampire)
                {
                    float stopRadius = Stats.RangeSim * 0.68f;
                    if (en.RadiusDist > stopRadius && Stats.MagneticGarlicLevel == 0)
                    {
                        MoveRadiallyInward(ref en, en.SpeedSim * dt, contactRadius);
                    }
                    else
                    {
                        // If above contactRadius, Magnetic Garlic slowly drags Vampire onto rotor teeth
                        if (en.RadiusDist > contactRadius)
                        {
                            float pullSpeed = Stats.MagneticGarlicLevel > 0 ? (0.14f * Stats.MagneticGarlicLevel) : 0f;
                            if (en.RadiusDist > stopRadius) pullSpeed = MathF.Max(pullSpeed, en.SpeedSim);
                            if (pullSpeed > 0f)
                                MoveRadiallyInward(ref en, pullSpeed * dt, contactRadius);
                        }

                        // Vampire siphon beam active! Drains 2.0% MaxHP/sec bypassing Def% & DefAbs
                        en.IsVampireBeaming = true;
                        AnyVampireBeaming = true;
                        CoreHp -= CoreMaxHp * 0.02 * dt;

                        // Magnetic Garlic anti-vampire burn
                        if (Stats.MagneticGarlicLevel > 0)
                        {
                            double garlicRate = Math.Max(0.018 * Stats.MagneticGarlicLevel, Stats.Thorns * 0.03 * Stats.MagneticGarlicLevel);
                            en.Hp -= en.MaxHp * garlicRate * dt;
                            if (en.Hp <= 0.0)
                            {
                                KillEnemyAt(i, killedByOrbOrRay: false);
                                continue;
                            }
                        }
                    }
                }
                else if (en.Type == EnemyType.Ranged)
                {
                    float rangedStop = Stats.RangeSim * 0.90f;
                    if (en.RadiusDist > rangedStop && !en.IsInBlackHole)
                    {
                        MoveRadiallyInward(ref en, en.SpeedSim * dt, rangedStop);
                    }
                    else if (!en.IsInBlackHole)
                    {
                        en.RangedFireTimer -= dt;
                        if (en.RangedFireTimer <= 0f)
                        {
                            en.RangedFireTimer += BalanceTables.RANGED_FIRE_INTERVAL;
                            SpawnProjectile(ProjectileKind.RangedEnemyBolt, -1, en.PosX, en.PosY, 0f, 0f, 8.0f, en.BaseDamage, false);
                        }
                    }
                }
                else
                {
                    // Melee approach (Basic, Fast, Tank, Boss)
                    if (en.RadiusDist > contactRadius && !en.IsInBlackHole)
                    {
                        MoveRadiallyInward(ref en, en.SpeedSim * dt, contactRadius);
                    }
                }

                // Melee contact attack & Thorns counter-grind (applies to any enemy touching rotor, including pulled Vampire!)
                if (en.RadiusDist <= contactRadius + 0.03f)
                {
                    en.AttackCooldown -= dt;
                    if (en.ConsecutiveHits >= 5) HeatingUpActive = true;

                    if (en.AttackCooldown <= 0f)
                    {
                        en.AttackCooldown += BalanceTables.ENEMY_MELEE_HIT_INTERVAL;
                        en.SquashTimer = 0.18f;

                        // FTUE Hook: Run 1 Wave 3+ triggers 8% prototype carbide teeth on first contact!
                        if (RunIndex == 1 && CurrentWave >= 3 && Stats.Thorns <= 0.0 && !PrototypeThornsTriggered)
                        {
                            PrototypeThornsTriggered = true;
                            FtueThornsToastPending = true;
                        }

                        ApplyEnemyHitToCore(en.BaseDamage, en.ConsecutiveHits);
                        en.ConsecutiveHits++;

                        double effThorns = Stats.Thorns;
                        if (effThorns <= 0.0 && PrototypeThornsTriggered && RunIndex == 1)
                        {
                            effThorns = BalanceTables.PROTOTYPE_THORNS_RUN1;
                        }

                        if (effThorns > 0.0)
                        {
                            double typeMult = BalanceTables.Enemies[(int)en.Type].ThornsTakenMult;
                            if (en.Type == EnemyType.Boss)
                            {
                                typeMult = Math.Min(0.55, typeMult + Stats.BossThornsBonus);
                            }
                            double thornsDmg = en.MaxHp * effThorns * typeMult;
                            en.Hp -= thornsDmg;
                            en.HitFlashTimer = 0.12f;
                            RecordDamagePopup(en.PosX, en.PosY, thornsDmg, false);
                            PushSfx(SfxId.Crunch);

                            if (en.Hp <= 0.0)
                            {
                                KillEnemyAt(i, killedByOrbOrRay: false);
                                continue;
                            }
                        }
                    }
                }
            }
        }

        private void TickTurretAndProjectiles(float dt)
        {
            // 1. Turret firing
            _fireCooldown -= dt;
            int safety = 0;
            while (_fireCooldown <= 0f && safety++ < 6)
            {
                int wantTargets = 1;
                if (Stats.MultishotChance > 0f && Rng.NextFloat() < Stats.MultishotChance)
                {
                    wantTargets = Math.Min(Stats.MultishotTargets, _targetIndicesScratch.Length);
                }

                int found = SelectPriorityTargets(wantTargets);
                if (found == 0)
                {
                    _fireCooldown = 0.05f;
                    break;
                }

                float interval = 1.0f / (float)Math.Max(0.2, Stats.AttackSpeed);
                _fireCooldown += interval;

                for (int t = 0; t < found; t++)
                {
                    ref EnemyState target = ref Enemies[_targetIndicesScratch[t]];
                    bool isCrit = Rng.NextFloat() < Stats.CritChance;
                    double shotDmg = Stats.Damage * (isCrit ? Stats.CritFactor : 1.0);
                    SpawnProjectile(
                        ProjectileKind.TowerNeedle,
                        target.Id,
                        0f, 0f,
                        target.PosX, target.PosY,
                        BalanceTables.PROJECTILE_SPEED_SIM,
                        shotDmg,
                        isCrit);
                }
                PushSfx(SfxId.PneumoShot);
            }

            // 2. Projectile movement & impacts
            for (int p = 0; p < PROJECTILE_CAPACITY; p++)
            {
                if (!Projectiles[p].Active) continue;
                ref ProjectileState proj = ref Projectiles[p];

                if (proj.Kind == ProjectileKind.RangedEnemyBolt)
                {
                    float dx = -proj.PosX;
                    float dy = -proj.PosY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    float step = proj.SpeedSim * dt;
                    if (dist <= BalanceTables.CORE_RADIUS_SIM + step)
                    {
                        ApplyEnemyHitToCore(proj.DamageOrPct, 0);
                        proj.Active = false;
                    }
                    else
                    {
                        proj.PosX += (dx / dist) * step;
                        proj.PosY += (dy / dist) * step;
                    }
                    continue;
                }

                // TowerNeedle or PlasmaCannonSlug: track live target if still alive
                int enemyIdx = FindEnemyIndexById(proj.TargetEnemyId);
                if (enemyIdx >= 0)
                {
                    proj.TargetX = Enemies[enemyIdx].PosX;
                    proj.TargetY = Enemies[enemyIdx].PosY;
                }

                float tx = proj.TargetX - proj.PosX;
                float ty = proj.TargetY - proj.PosY;
                float tDist = MathF.Sqrt(tx * tx + ty * ty);
                float pStep = proj.SpeedSim * dt;

                if (tDist <= pStep + 0.14f)
                {
                    proj.Active = false;
                    if (enemyIdx >= 0)
                    {
                        ResolveTowerProjectileHit(enemyIdx, ref proj);
                    }
                }
                else
                {
                    proj.PosX += (tx / tDist) * pStep;
                    proj.PosY += (ty / tDist) * pStep;
                }
            }
        }

        private void ResolveTowerProjectileHit(int enemyIdx, ref ProjectileState proj)
        {
            ref EnemyState en = ref Enemies[enemyIdx];
            if (proj.Kind == ProjectileKind.PlasmaCannonSlug)
            {
                double pcDmg = en.MaxHp * proj.DamageOrPct;
                en.Hp -= pcDmg;
                en.HitFlashTimer = 0.20f;
                RecordDamagePopup(en.PosX, en.PosY, pcDmg, true);
                PushSfx(SfxId.PlasmaHit);
                if (en.Hp <= 0.0)
                {
                    KillEnemyAt(enemyIdx, killedByOrbOrRay: false);
                }
                return;
            }

            double dealt = proj.DamageOrPct;
            en.Hp -= dealt;
            en.HitFlashTimer = 0.10f;
            RecordDamagePopup(en.PosX, en.PosY, dealt, proj.IsCrit);

            // Lifesteal (severely damped to 35% while any Vampire siphon beam is active)
            if (Stats.Lifesteal > 0.0 && CoreHp > 0.0)
            {
                double lsMult = AnyVampireBeaming ? 0.35 : 1.0;
                CoreHp = Math.Min(CoreMaxHp, CoreHp + dealt * Stats.Lifesteal * lsMult);
            }

            // Knockback (Boss is immune; pushes radially outward toward Plasma Saw ring)
            if (en.Hp > 0.0 && en.Type != EnemyType.Boss && Stats.KnockbackChance > 0f && Rng.NextFloat() < Stats.KnockbackChance)
            {
                float impulse = (0.35f * Stats.KnockbackForce) / MathF.Max(0.5f, en.Mass);
                float maxKnockR = Stats.RangeSim * 1.05f;
                en.RadiusDist = MathF.Min(maxKnockR, en.RadiusDist + impulse);
                en.PosX = MathF.Cos(en.Angle) * en.RadiusDist;
                en.PosY = MathF.Sin(en.Angle) * en.RadiusDist;
                en.SquashTimer = 0.12f;
            }

            float hitX = en.PosX;
            float hitY = en.PosY;
            int hitId = en.Id;

            if (en.Hp <= 0.0)
            {
                KillEnemyAt(enemyIdx, killedByOrbOrRay: false);
            }

            // Chain Lightning UW proc
            if (_chainUnlocked && Rng.NextFloat() < _chainChance)
            {
                TriggerChainLightning(hitX, hitY, hitId);
            }
        }

        private void TriggerChainLightning(float startX, float startY, int initialEnemyId)
        {
            double arcDmg = Stats.Damage * _chainDmgMult;
            float prevX = startX;
            float prevY = startY;
            PushSfx(SfxId.ChainZap);

            // Up to _chainTargets bounces among nearby enemies
            int bounced = 0;
            for (int i = ActiveEnemyCount - 1; i >= 0 && bounced < _chainTargets; i--)
            {
                if (Enemies[i].Id == initialEnemyId) continue;
                float dx = Enemies[i].PosX - prevX;
                float dy = Enemies[i].PosY - prevY;
                if (dx * dx + dy * dy > 3.2f * 3.2f) continue;

                float curX = Enemies[i].PosX;
                float curY = Enemies[i].PosY;

                if (ActiveChainArcCount < CHAIN_ARC_CAPACITY)
                {
                    ChainArcs[ActiveChainArcCount++] = new ChainArcState
                    {
                        FromX = prevX,
                        FromY = prevY,
                        ToX = curX,
                        ToY = curY,
                        TimeRemaining = 0.12f
                    };
                }

                prevX = curX;
                prevY = curY;
                bounced++;

                Enemies[i].Hp -= arcDmg;
                Enemies[i].HitFlashTimer = 0.14f;
                RecordDamagePopup(curX, curY, arcDmg, true);
                if (Enemies[i].Hp <= 0.0)
                {
                    KillEnemyAt(i, killedByOrbOrRay: false);
                }
            }
        }

        private void ApplyEnemyHitToCore(double enemyBaseDmg, int consecutiveHits)
        {
            // Order of operations (v29.1.4 SDK combat.ts): Defense % first, then flat Defense Absolute!
            double heated = enemyBaseDmg * Math.Pow(1.04, consecutiveHits);
            double afterPct = heated * (1.0 - Math.Min(0.98, Stats.DefPercent));
            double taken = Math.Max(0.0, afterPct - Stats.DefAbs);
            CoreHp -= taken;
        }

        private int SelectPriorityTargets(int maxTargets)
        {
            int count = 0;
            float maxR = Stats.RangeSim + 0.25f;

            // Score: lower score = higher priority (beaming Vampires get huge priority boost so cannon focuses them!)
            for (int slot = 0; slot < maxTargets; slot++)
            {
                int bestIdx = -1;
                float bestScore = float.MaxValue;

                for (int i = 0; i < ActiveEnemyCount; i++)
                {
                    if (Enemies[i].RadiusDist > maxR) continue;

                    bool alreadyPicked = false;
                    for (int k = 0; k < count; k++)
                    {
                        if (_targetIndicesScratch[k] == i) { alreadyPicked = true; break; }
                    }
                    if (alreadyPicked) continue;

                    float score = Enemies[i].RadiusDist;
                    if (Enemies[i].IsVampireBeaming) score -= 2.5f;
                    else if (Enemies[i].Type == EnemyType.Ranged && Enemies[i].RadiusDist <= Stats.RangeSim * 0.92f) score -= 0.85f;
                    else if (Enemies[i].Type == EnemyType.Boss) score -= 0.4f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }

                if (bestIdx < 0) break;
                _targetIndicesScratch[count++] = bestIdx;
            }
            return count;
        }

        private void FirePlasmaCannonAtBoss(ref EnemyState boss)
        {
            SpawnProjectile(
                ProjectileKind.PlasmaCannonSlug,
                boss.Id,
                0f, 0f,
                boss.PosX, boss.PosY,
                15.0f,
                Stats.PlasmaCannonBossPct,
                true);
        }

        private void SpawnProjectile(
            ProjectileKind kind, int targetId,
            float sx, float sy, float tx, float ty,
            float speedSim, double dmgOrPct, bool isCrit)
        {
            for (int i = 0; i < PROJECTILE_CAPACITY; i++)
            {
                if (!Projectiles[i].Active)
                {
                    Projectiles[i] = new ProjectileState
                    {
                        Active = true,
                        Kind = kind,
                        TargetEnemyId = targetId,
                        PosX = sx,
                        PosY = sy,
                        TargetX = tx,
                        TargetY = ty,
                        SpeedSim = speedSim,
                        DamageOrPct = dmgOrPct,
                        IsCrit = isCrit
                    };
                    return;
                }
            }
        }

        private void TrySpawnNormalWaveEnemy()
        {
            if (ActiveEnemyCount >= BalanceTables.MAX_ACTIVE_ENEMIES_TOTAL ||
                ActiveNormalCount >= BalanceTables.MAX_ACTIVE_NORMAL)
                return;

            BalanceTables.GetWaveComposition(CurrentWave, out float fastP, out float tankP, out float rangedP);
            float r = Rng.NextFloat();
            EnemyType t;
            if (r < fastP) t = EnemyType.Fast;
            else if (r < fastP + tankP) t = EnemyType.Tank;
            else if (r < fastP + tankP + rangedP) t = EnemyType.Ranged;
            else t = EnemyType.Basic;

            TrySpawnEnemyOfType(t);
        }

        private int TrySpawnEnemyOfType(EnemyType type)
        {
            if (ActiveEnemyCount >= BalanceTables.MAX_ACTIVE_ENEMIES_TOTAL) return -1;
            if (type == EnemyType.Boss && ActiveBossCount >= BalanceTables.MAX_ACTIVE_BOSS) return -1;
            if (type == EnemyType.Vampire && ActiveEliteCount >= BalanceTables.MAX_ACTIVE_ELITE) return -1;
            if (type != EnemyType.Boss && type != EnemyType.Vampire && ActiveNormalCount >= BalanceTables.MAX_ACTIVE_NORMAL) return -1;

            ref readonly EnemyTypeDef def = ref BalanceTables.Enemies[(int)type];
            double baseHp = BalanceTables.GetWaveBaseHp(CurrentWave, Tier);
            double baseDmg = BalanceTables.GetWaveBaseDamage(CurrentWave, Tier);

            float angle = Rng.NextFloat() * MathF.PI * 2f;
            float spawnR = Stats.RangeSim + BalanceTables.SPAWN_RING_PADDING_SIM;
            float speedMult = def.SpeedMult;
            if (type == EnemyType.Fast && CurrentWave >= 100) speedMult *= 1.16f;

            int idx = ActiveEnemyCount++;
            if (type == EnemyType.Boss) ActiveBossCount++;
            else if (type == EnemyType.Vampire) ActiveEliteCount++;
            else ActiveNormalCount++;

            double maxHp = baseHp * def.HpMult * (type == EnemyType.Boss ? BalanceTables.GetBossArmorHpMult(CurrentWave) : 1.0);
            Enemies[idx] = new EnemyState
            {
                Id = _nextEnemyId++,
                Type = type,
                Angle = angle,
                RadiusDist = spawnR,
                PosX = MathF.Cos(angle) * spawnR,
                PosY = MathF.Sin(angle) * spawnR,
                Hp = maxHp,
                MaxHp = maxHp,
                BaseDamage = baseDmg * def.DmgMult,
                SpeedSim = BalanceTables.BASE_ENEMY_SPEED_SIM * speedMult,
                Mass = def.Mass,
                HitboxRadius = def.HitboxRadiusSim,
                ConsecutiveHits = 0,
                AttackCooldown = 0.15f,
                RangedFireTimer = 1.0f,
                HitFlashTimer = 0f,
                SquashTimer = 0f,
                IsInBlackHole = false,
                IsVampireBeaming = false
            };
            return idx;
        }

        private void KillEnemyAt(int index, bool killedByOrbOrRay)
        {
            ref EnemyState en = ref Enemies[index];
            EnemyType type = en.Type;
            ref readonly EnemyTypeDef def = ref BalanceTables.Enemies[(int)type];

            // Cash reward
            double gtMult = GoldenTower.Active ? GoldenTower.Multiplier : 1.0;
            double ebCash = Stats.HasEnemyBalance ? Stats.EnemyBalanceCashMult : 1.0;
            double cash = BalanceTables.GetWaveBaseKillCash(CurrentWave) * def.CashMult * Stats.CashBonus * ebCash * gtMult;

            // Coin reward (Basic drops 1 coin at 70% chance so early waves yield steady coins; others 100%)
            double baseCoins = def.BaseCoins;
            if (type == EnemyType.Basic)
            {
                baseCoins = Rng.NextFloat() < 0.70f ? 1.0 : 0.0;
            }

            double bhMult = en.IsInBlackHole ? Stats.BhCoinMult : 1.0;
            double botMult = 1.0;
            if (GoldenBot.Unlocked && GoldenBot.PulseActive)
            {
                float bx = en.PosX - GoldenBot.PosX;
                float by = en.PosY - GoldenBot.PosY;
                if (bx * bx + by * by <= GoldenBot.RadiusSim * GoldenBot.RadiusSim)
                {
                    botMult = BalanceTables.GOLDEN_BOT_COIN_MULT;
                }
            }

            double coins = baseCoins * BalanceTables.GetWaveCoinScale(CurrentWave)
                * Stats.CoinsPerKillMult * Stats.CardCoinMult * Stats.GlobalAccountCoinMult
                * gtMult * bhMult * botMult;

            // Elite Cells drop (Vampire drops 2-4 Cells)
            int cells = 0;
            if (type == EnemyType.Vampire)
            {
                cells = Rng.NextRange(2, 5);
            }

            RunCashEarned += cash;
            RunCoinsEarned += coins;
            if (GoldenTower.Active || en.IsInBlackHole)
            {
                RunCoinsEarnedDuringGtBh += coins;
            }
            RunCellsEarned += cells;
            RunTotalKills++;

            if (type == EnemyType.Boss) PushSfx(SfxId.BossShatter);
            else if (coins >= 8.0 || GoldenTower.Active) PushSfx(SfxId.IngotDing);

            if (KillEventCount < KILL_EVENT_CAPACITY)
            {
                KillEvents[KillEventCount++] = new KillEvent
                {
                    Type = type,
                    PosX = en.PosX,
                    PosY = en.PosY,
                    KilledByOrbOrRay = killedByOrbOrRay,
                    KilledInGolden = GoldenTower.Active || en.IsInBlackHole,
                    CashEarned = cash,
                    CoinsEarned = coins,
                    CellsEarned = cells
                };
            }

            // Decrement category counter & swap-remove
            if (type == EnemyType.Boss) ActiveBossCount--;
            else if (type == EnemyType.Vampire) ActiveEliteCount--;
            else ActiveNormalCount--;

            int last = ActiveEnemyCount - 1;
            if (index != last)
            {
                Enemies[index] = Enemies[last];
            }
            ActiveEnemyCount = last;
        }

        private int FindEnemyIndexById(int id)
        {
            if (id <= 0) return -1;
            for (int i = 0; i < ActiveEnemyCount; i++)
            {
                if (Enemies[i].Id == id) return i;
            }
            return -1;
        }

        private static void MoveRadiallyInward(ref EnemyState en, float step, float minRadius)
        {
            en.RadiusDist = MathF.Max(minRadius, en.RadiusDist - step);
            en.PosX = MathF.Cos(en.Angle) * en.RadiusDist;
            en.PosY = MathF.Sin(en.Angle) * en.RadiusDist;
        }

        private void PushSfx(SfxId sfx)
        {
            if (SfxCount < SFX_QUEUE_CAPACITY)
            {
                SfxQueue[SfxCount++] = sfx;
            }
        }

        private void RecordDamagePopup(float x, float y, double dmg, bool crit)
        {
            // Aggregate into <= 6 spatial buckets per #20 UI budget
            for (int i = 0; i < DAMAGE_BUCKET_COUNT; i++)
            {
                if (DamageBuckets[i].Active)
                {
                    float dx = DamageBuckets[i].PosX - x;
                    float dy = DamageBuckets[i].PosY - y;
                    if (dx * dx + dy * dy <= 0.65f * 0.65f)
                    {
                        DamageBuckets[i].AccumulatedDamage += dmg;
                        DamageBuckets[i].HasCrit |= crit;
                        return;
                    }
                }
            }
            for (int i = 0; i < DAMAGE_BUCKET_COUNT; i++)
            {
                if (!DamageBuckets[i].Active)
                {
                    DamageBuckets[i] = new DamagePopupBucket
                    {
                        Active = true,
                        PosX = x,
                        PosY = y,
                        AccumulatedDamage = dmg,
                        HasCrit = crit
                    };
                    return;
                }
            }
        }

        private static float WrapAngle(float a)
        {
            const float twoPi = MathF.PI * 2f;
            while (a >= twoPi) a -= twoPi;
            while (a < 0f) a += twoPi;
            return a;
        }

        private static float WrapAngleSigned(float a)
        {
            while (a > MathF.PI) a -= MathF.PI * 2f;
            while (a < -MathF.PI) a += MathF.PI * 2f;
            return a;
        }
    }
}
