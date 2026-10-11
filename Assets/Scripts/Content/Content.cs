using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Base towers, ported from orc-horde src/content/towers.ts.
    ///
    /// Deployment limits differ: Orc Horde unlocks everything past the Machine
    /// Gun through the skill tree, and there is no tree here yet, so each tower
    /// starts with a small allowance instead.
    /// </summary>
    public static class Towers
    {
        public static readonly TowerDef MachineGun = new TowerDef
        {
            Id = "machine_gun", Name = "Machine Gun",
            Description = "Rapid single-target fire. Every bullet stops at the first thing it hits.",
            Limit = 3, Footprint = U.M(24), Damage = 6, Range = U.M(260), FireInterval = U.Sec(120),
            TargetFirst = true, Sight = SightBlockers.RockAndWalls, Kind = AttackKind.Projectile,
            ProjectileSpeed = U.M(1300), ProjectileRadius = U.M(4), SplashRadius = 0,
            ProjectileLifetime = U.Sec(700), Color = U.Hex(0xc2a33e),
        };

        public static readonly TowerDef Cannon = new TowerDef
        {
            Id = "cannon", Name = "Cannon",
            Description = "Slow splash artillery. Lobs over barricades. Strong against packs.",
            Limit = 2, Footprint = U.M(28), Damage = 10, Range = U.M(280), FireInterval = U.Sec(900),
            TargetFirst = true, Sight = SightBlockers.Rock, Kind = AttackKind.Projectile, Lobbed = true,
            ProjectileSpeed = U.M(700), ProjectileRadius = U.M(8), SplashRadius = U.M(55),
            ProjectileLifetime = U.Sec(2500), Color = U.Hex(0x4a7ea8),
        };

        public static readonly TowerDef Laser = new TowerDef
        {
            Id = "laser", Name = "Laser",
            Description = "Piercing beam that ricochets off rock and barricades. Fires without a clear line.",
            Limit = 1, Footprint = U.M(26), Damage = 2.5f, Range = U.M(300), FireInterval = U.Sec(220),
            TargetFirst = true, Sight = SightBlockers.RockAndWalls, SightPreferred = true,
            Kind = AttackKind.Hitscan, PathBudgetMultiplier = 2f, AimDither = 0.026f,
            Color = U.Hex(0x3ec9a7),
        };

        public static readonly TowerDef ArcTower = new TowerDef
        {
            Id = "arc_tower", Name = "Arc Tower",
            Description = "Pulses a full circle, slowing everything it touches to half speed.",
            Limit = 1, Footprint = U.M(26), Damage = 1, Range = U.M(200), FireInterval = U.Sec(500),
            TargetFirst = false, Sight = SightBlockers.Rock, Kind = AttackKind.PulseAoe,
            SlowFactor = 0.5f, SlowDuration = U.Sec(900), Color = U.Hex(0x8e7cc3),
        };

        public static readonly TowerDef FlameThrower = new TowerDef
        {
            Id = "flame_thrower", Name = "Flame Thrower",
            Description = "Sprays a cone of fire. Devastating close up, useless at a distance.",
            Limit = 1, Footprint = U.M(27), Damage = 3.5f, Range = U.M(170), FireInterval = U.Sec(140),
            TargetFirst = false, Sight = SightBlockers.RockAndWalls, Kind = AttackKind.ConeAoe,
            ConeDegrees = 70, Color = U.Hex(0xd9743f),
        };

        public static readonly TowerDef[] All = { MachineGun, Cannon, Laser, ArcTower, FlameThrower };
    }

    /// <summary>Ported from orc-horde src/content/enemies.ts.</summary>
    public static class Enemies
    {
        public static readonly EnemyDef Fodder = new EnemyDef
        {
            Id = "fodder", Name = "Fodder", Description = "Cheap, numerous infantry.",
            MaxHealth = 35, Armour = 0, Speed = U.M(78), Radius = U.M(8), BreachDamage = 1,
            Pressure = 1f / 3f, Reward = 1, TurnDegreesPerSecond = 7f * Mathf.Rad2Deg,
            Wall = WallBehavior.AttackUnderPressure, WallDetection = U.M(128), WallRange = U.M(18), WallDps = 8,
            Color = U.Hex(0xc0563c),
        };

        public static readonly EnemyDef Runner = new EnemyDef
        {
            Id = "runner", Name = "Runner", Description = "Fragile but fast.",
            MaxHealth = 25, Armour = 0, Speed = U.M(130), Radius = U.M(7), BreachDamage = 3,
            Pressure = 2f / 3f, Reward = 1, TurnDegreesPerSecond = 9f * Mathf.Rad2Deg,
            Wall = WallBehavior.Avoid, Color = U.Hex(0xe0a33c),
        };

        public static readonly EnemyDef Armoured = new EnemyDef
        {
            Id = "armoured", Name = "Armoured", Description = "Slow, heavily protected line infantry.",
            MaxHealth = 130, Armour = 6, Speed = U.M(55), Radius = U.M(10), BreachDamage = 3,
            Pressure = 6f / 3f, Reward = 3, TurnDegreesPerSecond = 5f * Mathf.Rad2Deg,
            Wall = WallBehavior.Avoid, Color = U.Hex(0x7f8c9b),
        };

        public static readonly EnemyDef SiegeElite = new EnemyDef
        {
            Id = "siege_elite", Name = "Siege Elite", Description = "Breaks walls across its route.",
            MaxHealth = 500, Armour = 0, Speed = U.M(48), Radius = U.M(13), BreachDamage = 3,
            Pressure = 20f / 3f, Reward = 8, TurnDegreesPerSecond = 4f * Mathf.Rad2Deg,
            Wall = WallBehavior.AttackIfRouteBlocked, WallDetection = U.M(110), WallRange = U.M(26), WallDps = 90,
            Color = U.Hex(0x9b59b6),
        };

        public static readonly EnemyDef SiegeBreaker = new EnemyDef
        {
            Id = "mini_boss", Name = "Siege Breaker", Description = "Heavyweight wall-breaker.",
            MaxHealth = 1200, Armour = 10, Speed = U.M(42), Radius = U.M(17), BreachDamage = 10,
            ShowsHealthBar = true, Pressure = 45f / 3f, Reward = 18, TurnDegreesPerSecond = 3f * Mathf.Rad2Deg,
            Wall = WallBehavior.AttackIfRouteBlocked, WallDetection = U.M(130), WallRange = U.M(29), WallDps = 150,
            Color = U.Hex(0xb5479b),
        };

        public static readonly EnemyDef SiegeBoss = new EnemyDef
        {
            Id = "siege_boss", Name = "Siege Boss",
            Description = "Enormous armoured breaker. If it reaches the base the round is lost outright.",
            MaxHealth = 3000, Armour = 14, Speed = U.M(35), Radius = U.M(22), BreachDamage = 10,
            BreachLosesRound = true, ShowsHealthBar = true, IsBoss = true, Pressure = 100f / 3f, Reward = 40,
            TurnDegreesPerSecond = 2.5f * Mathf.Rad2Deg,
            Wall = WallBehavior.BreachWhereTooNarrow, WallDetection = U.M(150), WallRange = U.M(32), WallDps = 250,
            Color = U.Hex(0xd6423f),
        };

        /// <summary>WallDps is the charge's damage, dealt once, for this one.</summary>
        public static readonly EnemyDef Sapper = new EnemyDef
        {
            Id = "sapper", Name = "Sapper", Description = "Carries a charge to the nearest barricade.",
            MaxHealth = 60, Armour = 0, Speed = U.M(95), Radius = U.M(8), BreachDamage = 2,
            Pressure = 1, Reward = 2, TurnDegreesPerSecond = 7f * Mathf.Rad2Deg,
            Wall = WallBehavior.DemolishNearestWall, WallDetection = 999f, WallRange = U.M(16), WallDps = 1200,
            Color = U.Hex(0xf07a2a),
        };

        public static readonly EnemyDef Shaman = new EnemyDef
        {
            Id = "shaman", Name = "Shaman", Description = "Heals every orc around it.",
            MaxHealth = 90, Armour = 0, Speed = U.M(60), Radius = U.M(9), BreachDamage = 1,
            Pressure = 1, Reward = 2, TurnDegreesPerSecond = 6f * Mathf.Rad2Deg, Wall = WallBehavior.Avoid,
            HealRadius = U.M(120), HealPerPulse = 12, HealInterval = U.Sec(1000),
            Color = U.Hex(0x4fbf7a),
        };

        public static readonly EnemyDef Splitter = new EnemyDef
        {
            Id = "splitter", Name = "Splitter", Description = "Killing it lets four Fodder loose.",
            MaxHealth = 180, Armour = 0, Speed = U.M(58), Radius = U.M(12), BreachDamage = 3,
            Pressure = 2, Reward = 2, TurnDegreesPerSecond = 5f * Mathf.Rad2Deg, Wall = WallBehavior.Avoid,
            SplitInto = Fodder, SplitCount = 4, Color = U.Hex(0x9d6fd6),
        };
    }

    /// <summary>Ported from orc-horde src/content/walls.ts.</summary>
    public static class Walls
    {
        public static readonly WallDef Barricade = new WallDef
        {
            Id = "barricade", Name = "Barricade",
            Description = "Redirects the horde. Destructible during a round, restored on every attempt.",
            Limit = 8, Health = 1200, Length = U.M(3 * 32), Thickness = U.M(32), Height = 1.6f,
            Color = U.Hex(0x8d6e4f),
        };
    }

    /// <summary>The two starting abilities, from orc-horde src/content/abilities.ts.</summary>
    public static class Abilities
    {
        public static readonly AbilityDef Mortar = new AbilityDef
        {
            Id = "mortar", Name = "Mortar", Description = "Manually targeted shell. Heavy damage in a tight radius.",
            Cooldown = U.Sec(10000), Damage = 250, Radius = U.M(90),
        };

        public static readonly AbilityDef BombingRun = new AbilityDef
        {
            Id = "bombing_run", Name = "Bombing Run", Description = "Strafing corridor along your aim.",
            Cooldown = U.Sec(25000), Damage = 160, Length = U.M(450), Width = U.M(100),
        };
    }

    /// <summary>
    /// Quarry Road, from orc-horde src/content/maps/quarryRoad.ts: one wide road
    /// in from the west, north at the bend, then east to the base.
    /// </summary>
    public static class Maps
    {
        const float WorldWidth = 2400, WorldHeight = 1800;
        const float RoadWidth = 320, ShoulderDepth = 160;
        const float EntryY = 1200, BaseY = 750, PivotX = 960;

        /// <summary>Orc Horde's y runs south; here +Z is north.</summary>
        static Vector3 P(float x, float y) => new Vector3(U.M(x), 0, U.M(WorldHeight - y));

        static Rect R(float minX, float minY, float maxX, float maxY) =>
            Rect.MinMaxRect(U.M(minX), U.M(WorldHeight - maxY), U.M(maxX), U.M(WorldHeight - minY));

        static (EnemyDef, float)[] W(params (EnemyDef, float)[] rules) => rules;

        public static MapDef QuarryRoad()
        {
            const float h = RoadWidth / 2;
            return new MapDef
            {
                Name = "Quarry Road",
                Description = "One wide road cut through solid rock. Nowhere for the horde to go but past your guns.",
                SpawnName = "Quarry Gate", BaseName = "Loading Yard",
                Width = U.M(WorldWidth), Depth = U.M(WorldHeight),
                Road = new[]
                {
                    R(0, EntryY - h, PivotX + h, EntryY + h),
                    R(PivotX - h, BaseY - h, PivotX + h, EntryY + h),
                    R(PivotX - h, BaseY - h, WorldWidth, BaseY + h),
                },
                ShoulderDepth = U.M(ShoulderDepth),
                Spawn = P(80, EntryY),
                Base = P(WorldWidth - 80, BaseY),
                PlayerStart = P(WorldWidth - 260, BaseY),
                PlayerYaw = -90,
                RockColor = U.Hex(0x4f4a44), FloorColor = U.Hex(0x2e2a26), ShoulderColor = U.Hex(0x6b6152),
                Rounds = new[]
                {
                    new RoundDef
                    {
                        Name = "First Contact", StartingHealth = 20, Duration = 60, PressureBudget = 270,
                        Enemies = W((Enemies.Fodder, 150)),
                    },
                    new RoundDef
                    {
                        Name = "Quick Feet", StartingHealth = 20, Duration = 70, PressureBudget = 480,
                        Enemies = W((Enemies.Fodder, 105), (Enemies.Runner, 30)),
                    },
                    new RoundDef
                    {
                        Name = "Iron Ranks", StartingHealth = 20, Duration = 80, PressureBudget = 675,
                        Enemies = W((Enemies.Fodder, 82.5f), (Enemies.Runner, 25), (Enemies.Armoured, 20)),
                    },
                    new RoundDef
                    {
                        Name = "Breakers", StartingHealth = 20, Duration = 90, PressureBudget = 1050,
                        Enemies = W((Enemies.Fodder, 67.5f), (Enemies.Runner, 20), (Enemies.Armoured, 25),
                            (Enemies.SiegeElite, 10)),
                    },
                    new RoundDef
                    {
                        Name = "The Last Load", StartingHealth = 20, Duration = 100, PressureBudget = 1500,
                        Enemies = W((Enemies.Fodder, 60), (Enemies.Runner, 20), (Enemies.Armoured, 25),
                            (Enemies.SiegeElite, 15)),
                        Guaranteed = new[] { (Enemies.SiegeElite, 2, 60f, 2.5f) },
                        Bosses = new[] { (Enemies.SiegeBoss, 75f, 10f) },
                    },
                },
            };
        }
    }
}
