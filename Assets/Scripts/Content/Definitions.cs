using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Conversions from Orc Horde's 2D content units.
    ///
    /// Orc Horde measures its world in flat units where a Fodder is 16 across.
    /// One unit is 4 cm here, which makes a Fodder about shoulder-width, Quarry
    /// Road 12.8 m wide and a Machine Gun's reach 10.4 m. Every number in the
    /// content files is kept in its original unit and converted with these, so
    /// the source of a value stays readable next to the original game.
    /// </summary>
    public static class U
    {
        public const float Scale = 0.04f;

        public static float M(float units) => units * Scale;

        public static float Sec(float ms) => ms / 1000f;

        public static Color Hex(uint rgb) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);
    }

    public enum AttackKind { Projectile, Hitscan, PulseAoe, ConeAoe }

    /// <summary>What stops a tower's shot: a lobbed shell clears a barricade, a flat one does not.</summary>
    public enum SightBlockers { Rock, RockAndWalls }

    public enum WallBehavior
    {
        Avoid,
        AttackUnderPressure,
        AttackIfRouteBlocked,
        BreachWhereTooNarrow,
        DemolishNearestWall,
    }

    public sealed class TowerDef
    {
        public string Id, Name, Description;
        public int Limit;
        public float Footprint, Damage, Range, FireInterval;
        public bool TargetFirst;
        public SightBlockers Sight;
        /// <summary>The Laser fires without a clear line, and bounces instead.</summary>
        public bool SightPreferred;
        public AttackKind Kind;
        public float ProjectileSpeed, ProjectileRadius, SplashRadius, ProjectileLifetime;
        public bool Lobbed;
        public float PathBudgetMultiplier = 1f, AimDither;
        public float SlowFactor = 1f, SlowDuration;
        public float ConeDegrees;
        public Color Color;
    }

    public sealed class EnemyDef
    {
        public string Id, Name, Description;
        public float MaxHealth, Armour, Speed, Radius;
        public int BreachDamage;
        public bool BreachLosesRound, ShowsHealthBar, IsBoss;
        public float Pressure;
        public int Reward;
        public float TurnDegreesPerSecond;
        public WallBehavior Wall;
        public float WallDetection, WallRange, WallDps;
        public float HealRadius, HealPerPulse, HealInterval;
        public EnemyDef SplitInto;
        public int SplitCount;
        public Color Color;

        /// <summary>Standing height: tall enough to read the size classes apart at a glance.</summary>
        public float Height => Mathf.Clamp(Radius * 5.5f, 1.6f, 5f);
    }

    public sealed class WallDef
    {
        public string Id, Name, Description;
        public int Limit;
        public float Health, Length, Thickness, Height;
        public Color Color;
    }

    public sealed class AbilityDef
    {
        public string Id, Name, Description;
        public float Cooldown, Damage, Radius, Length, Width;
    }

    public sealed class RoundDef
    {
        public string Name;
        public int StartingHealth;
        public float Duration, PressureBudget;
        public (EnemyDef def, float weight)[] Enemies;
        public (EnemyDef def, int count, float at, float spacing)[] Guaranteed = { };
        public (EnemyDef def, float at, float warning)[] Bosses = { };
    }

    public sealed class MapDef
    {
        public string Name, Description, SpawnName, BaseName;
        public float Width, Depth;
        /// <summary>The enemy floor, in metres on the XZ plane.</summary>
        public Rect[] Road;
        /// <summary>How far back from the road a tower may stand.</summary>
        public float ShoulderDepth;
        public Vector3 Spawn, Base, PlayerStart;
        public float PlayerYaw;
        public Color RockColor, FloorColor, ShoulderColor;
        public RoundDef[] Rounds;
    }
}
