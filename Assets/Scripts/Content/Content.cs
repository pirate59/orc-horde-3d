using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Base towers, ported from orc-horde src/content/towers.ts. These are the
    /// unupgraded numbers: the skill tree's unlocks, capacity and multipliers are
    /// applied on top by <see cref="Progression.Tower"/>, never written back here.
    /// </summary>
    public static class Towers
    {
        public static readonly TowerDef MachineGun = new TowerDef
        {
            Id = "machine_gun", Name = "Machine Gun",
            Description = "Rapid single-target fire. Every bullet stops at the first thing it hits.",
            UnlockedByDefault = true, Limit = 3, Footprint = U.M(24), Damage = 6, Range = U.M(260), FireInterval = U.Sec(120),
            TargetFirst = true, Sight = SightBlockers.RockAndWalls, Kind = AttackKind.Projectile,
            ProjectileSpeed = U.M(1300), ProjectileRadius = U.M(4), SplashRadius = 0,
            ProjectileLifetime = U.Sec(700), Color = U.Hex(0xc2a33e),
        };

        public static readonly TowerDef Cannon = new TowerDef
        {
            Id = "cannon", Name = "Cannon",
            Description = "Slow splash artillery. Lobs over barricades. Strong against packs.",
            Limit = 0, Footprint = U.M(28), Damage = 10, Range = U.M(280), FireInterval = U.Sec(900),
            TargetFirst = true, Sight = SightBlockers.Rock, Kind = AttackKind.Projectile, Lobbed = true,
            ProjectileSpeed = U.M(700), ProjectileRadius = U.M(8), SplashRadius = U.M(55),
            ProjectileLifetime = U.Sec(2500), Color = U.Hex(0x4a7ea8),
        };

        public static readonly TowerDef Laser = new TowerDef
        {
            Id = "laser", Name = "Laser",
            Description = "Piercing beam that ricochets off rock and barricades. Fires without a clear line.",
            Limit = 0, Footprint = U.M(26), Damage = 2.5f, Range = U.M(300), FireInterval = U.Sec(220),
            TargetFirst = true, Sight = SightBlockers.RockAndWalls, SightPreferred = true,
            Kind = AttackKind.Hitscan, PathBudgetMultiplier = 2f, AimDither = 0.026f,
            Color = U.Hex(0x3ec9a7),
        };

        public static readonly TowerDef ArcTower = new TowerDef
        {
            Id = "arc_tower", Name = "Arc Tower",
            Description = "Pulses a full circle, slowing everything it touches to half speed.",
            Limit = 0, Footprint = U.M(26), Damage = 1, Range = U.M(200), FireInterval = U.Sec(500),
            TargetFirst = false, Sight = SightBlockers.Rock, Kind = AttackKind.PulseAoe,
            SlowFactor = 0.5f, SlowDuration = U.Sec(900), Color = U.Hex(0x8e7cc3),
        };

        public static readonly TowerDef FlameThrower = new TowerDef
        {
            Id = "flame_thrower", Name = "Flame Thrower",
            Description = "Sprays a cone of fire. Devastating close up, useless at a distance.",
            Limit = 0, Footprint = U.M(27), Damage = 3.5f, Range = U.M(170), FireInterval = U.Sec(140),
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

        // The set-piece bosses. All four share the Siege Boss's frame and wall-breaking.

        static EnemyDef Boss(string id, string name, string description, float health, float armour, float speed,
            float radius, uint color) => new EnemyDef
        {
            Id = id, Name = name, Description = description,
            MaxHealth = health, Armour = armour, Speed = U.M(speed), Radius = U.M(radius), BreachDamage = 10,
            BreachLosesRound = true, ShowsHealthBar = true, IsBoss = true, Pressure = 100f / 3f, Reward = 40,
            TurnDegreesPerSecond = 2.5f * Mathf.Rad2Deg,
            Wall = WallBehavior.BreachWhereTooNarrow, WallDetection = U.M(150), WallRange = U.M(32), WallDps = 250,
            Color = U.Hex(color),
        };

        public static readonly EnemyDef RamChief = Charge(Boss("ram_chief", "Ram Chief",
            "Winds up, then charges down the road at three times its pace, smashing the first barricade it hits. Enrages when nearly dead.",
            2600, 10, 36, 21, 0xe0683a));

        static EnemyDef Charge(EnemyDef d)
        {
            d.ChargeInterval = U.Sec(9000);
            d.ChargeWindup = U.Sec(1200);
            d.ChargeDuration = U.Sec(2000);
            d.ChargeSpeedMultiplier = 3;
            d.ChargeWallDamage = 900;
            d.EnrageThreshold = 0.25f;
            d.EnrageSpeedMultiplier = 1.4f;
            return d;
        }

        public static readonly EnemyDef Shieldwall = Shield(Boss("shieldwall", "Shieldwall",
            "Carries a tower shield that stops most of any hit from the front. Flank it, bounce a beam into it, or drop shells on it from above.",
            2800, 12, 32, 22, 0x4f9fd6));

        static EnemyDef Shield(EnemyDef d)
        {
            d.ShieldArcDegrees = 140;
            d.ShieldReduction = 0.8f;
            return d;
        }

        public static readonly EnemyDef Warlord = Summoner(Boss("warlord", "Warlord",
            "Calls a war band to its side at two-thirds and one-third health, standing still while it gathers, and rages at the end.",
            3200, 10, 34, 22, 0xc9a227));

        static EnemyDef Summoner(EnemyDef d)
        {
            d.SummonThresholds = new[] { 0.66f, 0.33f };
            d.SummonEscort = new[] { (Fodder, 8), (Armoured, 2) };
            d.SummonPause = U.Sec(1500);
            d.EnrageThreshold = 0.25f;
            d.EnrageSpeedMultiplier = 1.5f;
            return d;
        }

        public static readonly EnemyDef BloodTotem = Regenerator(Boss("blood_totem", "Blood Totem",
            "Heals everything around it, and heals itself whenever it is left alone for three seconds. Keep it under fire.",
            2400, 8, 30, 21, 0xc23b5b));

        static EnemyDef Regenerator(EnemyDef d)
        {
            d.HealRadius = U.M(160);
            d.HealPerPulse = 20;
            d.HealInterval = U.Sec(1000);
            d.RegenIdle = U.Sec(3000);
            d.RegenPerSecond = 80;
            return d;
        }

        public static readonly EnemyDef[] All =
        {
            Fodder, Runner, Armoured, SiegeElite, SiegeBreaker, SiegeBoss, Sapper, Shaman, Splitter,
            RamChief, Shieldwall, Warlord, BloodTotem,
        };

        static System.Collections.Generic.Dictionary<string, EnemyDef> byId;

        /// <summary>Looks an enemy up by its Orc Horde id, as the exported rounds name them.</summary>
        public static EnemyDef ById(string id)
        {
            if (byId == null)
            {
                byId = new System.Collections.Generic.Dictionary<string, EnemyDef>();
                foreach (var e in All) byId[e.Id] = e;
            }
            return byId.TryGetValue(id, out var def) ? def : null;
        }
    }

    /// <summary>Ported from orc-horde src/content/walls.ts.</summary>
    public static class Walls
    {
        public static readonly WallDef Barricade = new WallDef
        {
            Id = "barricade", Name = "Barricade",
            Description = "Redirects the horde. Destructible during a round, restored on every attempt.",
            // Three hex cells end to end, as Orc Horde's barricades are since its maps went hexagonal.
            Limit = 8, Health = 1200, Cells = 3, Length = U.M(3 * 60), Thickness = U.M(32), Height = 1.6f,
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
}
