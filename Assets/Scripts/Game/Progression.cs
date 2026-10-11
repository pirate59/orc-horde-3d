using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    [Serializable]
    public sealed class SkillTree
    {
        public SkillNode[] nodes;
    }

    [Serializable]
    public sealed class SkillNode
    {
        public string id, name, description, currency;
        public bool mastery;
        public float x, y;
        public SkillPrerequisite[] prerequisites, alternatives;
        public SkillLevel[] levels;
    }

    [Serializable]
    public sealed class SkillPrerequisite
    {
        public string nodeId;
        public int minimumLevel;
    }

    [Serializable]
    public sealed class SkillLevel
    {
        public int cost;
        public string label, description;
        public SkillEffect[] effects;
    }

    [Serializable]
    public sealed class SkillEffect
    {
        public string kind, target, stat;
        public float amount;
    }

    /// <summary>A tower's stats with every bought upgrade applied, as Orc Horde's statResolver does it.</summary>
    public sealed class TowerStats
    {
        public bool Unlocked;
        public int Limit;
        public float Damage, Range, FireInterval, Splash, SlowDuration, ConeDegrees;
    }

    /// <summary>
    /// The campaign's currency and skill tree, after Orc Horde's progression.
    ///
    /// Kills pay currency win or lose; a win adds a completion bonus that grows
    /// through the campaign and doubles on a first clear. Currency buys skill
    /// levels: tower unlocks, capacity and stat multipliers, walls, base health
    /// and ability upgrades, then the 40-level masteries that soak up a deep
    /// campaign's currency. Only nodes whose every effect this game honours are
    /// offered; the rest (shields, traits, the later abilities) wait their turn.
    ///
    /// Saved in PlayerPrefs, so it survives across sessions and map changes.
    /// </summary>
    public static class Progression
    {
        static readonly string SaveKey = SaveSlot.Key("progression");

        /// <summary>Orc Horde's completion bonus: +5% per campaign round before this one.</summary>
        const float RoundMultiplierStep = 0.05f;
        const int FirstClearMultiplier = 2;


        static readonly HashSet<string> Supported = new HashSet<string>
        {
            "unlock_tower", "tower_capacity_delta", "tower_stat_multiplier_delta",
            "wall_capacity_delta", "wall_health_multiplier_delta", "base_health_delta",
            "ability_stat_multiplier_delta", RifleEffect,
        };

        /// <summary>
        /// FPS-only: the player's rifle, which Orc Horde has no use for. Without it
        /// the one gun that never improves would fade to nothing against hordes the
        /// towers are being upgraded to meet. Priced alongside the Machine Gun line.
        /// </summary>
        const string RifleEffect = "rifle_stat_multiplier_delta";

        static SkillNode RifleNode(string id, string name, string description, string stat, float amount, int[] costs)
        {
            var levels = new SkillLevel[costs.Length];
            for (int i = 0; i < costs.Length; i++)
                levels[i] = new SkillLevel
                {
                    cost = costs[i],
                    label = $"Level {i + 1}",
                    description = $"+{amount:P0} rifle {(stat == "damage" ? "damage" : "fire rate")}",
                    effects = new[] { new SkillEffect { kind = RifleEffect, target = "rifle", stat = stat, amount = amount } },
                };
            return new SkillNode
            {
                id = id, name = name, description = description, currency = "currency",
                x = -1, y = -1, prerequisites = new SkillPrerequisite[0], alternatives = new SkillPrerequisite[0], levels = levels,
            };
        }

        static readonly SkillNode[] RifleNodes =
        {
            RifleNode("rifle_damage", "Rifle Rounds", "Heavier rounds for your own rifle.", "damage", 0.15f,
                new[] { 500, 900, 1500, 2400, 3600, 5200, 7200, 9600 }),
            RifleNode("rifle_fire_rate", "Rifle Action", "A faster action for your own rifle.", "fireRate", 0.1f,
                new[] { 700, 1300, 2100, 3400, 5400, 8000 }),
        };

        static readonly HashSet<string> SupportedTowerStats = new HashSet<string>
            { "damage", "fireRate", "range", "splashRadius", "slowDuration", "coneWidth" };

        static readonly HashSet<string> SupportedAbilities = new HashSet<string> { "mortar", "bombing_run" };
        static readonly HashSet<string> SupportedAbilityStats = new HashSet<string> { "cooldown", "damage", "radius" };

        [Serializable]
        sealed class Save
        {
            public int currency;
            public List<string> nodeIds = new List<string>();
            public List<int> nodeLevels = new List<int>();
            public List<string> clearedRounds = new List<string>();
        }

        static Save save;
        static SkillTree tree;
        static List<SkillNode> offered;
        static readonly Dictionary<TowerDef, TowerStats> towerCache = new Dictionary<TowerDef, TowerStats>();

        /// <summary>Raised after any purchase or reset, so placed things can pick up new stats.</summary>
        public static event Action Changed;

        static Save Data
        {
            get
            {
                if (save != null) return save;
                var json = PlayerPrefs.GetString(SaveKey, "");
                save = string.IsNullOrEmpty(json) ? new Save() : JsonUtility.FromJson<Save>(json);
                return save;
            }
        }

        static void Persist()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
            towerCache.Clear();
            Changed?.Invoke();
        }

        public static int Currency => Data.currency;

        /// <summary>Orc Horde's tree with the rifle line added in front.</summary>
        public static SkillTree Tree
        {
            get
            {
                if (tree != null) return tree;
                tree = JsonUtility.FromJson<SkillTree>(Resources.Load<TextAsset>("Skills").text);
                var all = new List<SkillNode>(RifleNodes);
                all.AddRange(tree.nodes);
                tree.nodes = all.ToArray();
                return tree;
            }
        }

        public static int Level(string nodeId)
        {
            int i = Data.nodeIds.IndexOf(nodeId);
            return i < 0 ? 0 : Data.nodeLevels[i];
        }

        static SkillNode Node(string id) => Array.Find(Tree.nodes, n => n.id == id);

        /// <summary>The line a node belongs to, for grouping: what its first effect works on.</summary>
        public static string Group(SkillNode n)
        {
            var e = n.levels[0].effects[0];
            if (e.kind.StartsWith("wall_")) return "Barricades";
            if (e.kind.StartsWith("base_")) return "Base";
            if (e.target == "rifle") return "Rifle";
            foreach (var t in Towers.All) if (t.Id == e.target) return t.Name;
            if (e.target == Abilities.Mortar.Id) return Abilities.Mortar.Name;
            if (e.target == Abilities.BombingRun.Id) return Abilities.BombingRun.Name;
            return "Other";
        }

        static readonly string[] GroupOrder =
            { "Rifle", "Machine Gun", "Cannon", "Laser", "Arc Tower", "Flame Thrower", "Barricades", "Base", "Mortar", "Bombing Run", "Other" };

        /// <summary>The nodes this game can honour, grouped by line, cheapest first within each.</summary>
        public static IReadOnlyList<SkillNode> Offered
        {
            get
            {
                if (offered != null) return offered;
                var ok = new HashSet<string>();
                foreach (var n in Tree.nodes)
                    if (n.currency == "currency" && Array.TrueForAll(n.levels, l => Array.TrueForAll(l.effects, Honoured)))
                        ok.Add(n.id);
                // A node is only reachable if a whole set of its prerequisites is.
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var n in Tree.nodes)
                    {
                        if (!ok.Contains(n.id)) continue;
                        bool main = Array.TrueForAll(n.prerequisites, p => ok.Contains(p.nodeId));
                        bool alt = n.alternatives.Length > 0 && Array.TrueForAll(n.alternatives, p => ok.Contains(p.nodeId));
                        if (!main && !alt) { ok.Remove(n.id); changed = true; }
                    }
                }
                offered = new List<SkillNode>(Array.FindAll(Tree.nodes, n => ok.Contains(n.id)));
                offered.Sort((a, b) =>
                {
                    int g = System.Array.IndexOf(GroupOrder, Group(a)).CompareTo(System.Array.IndexOf(GroupOrder, Group(b)));
                    return g != 0 ? g : a.levels[0].cost.CompareTo(b.levels[0].cost);
                });
                return offered;
            }
        }

        static bool Honoured(SkillEffect e) =>
            Supported.Contains(e.kind) &&
            (e.kind != "tower_stat_multiplier_delta" || SupportedTowerStats.Contains(e.stat)) &&
            (e.kind != "ability_stat_multiplier_delta" || (SupportedAbilities.Contains(e.target) && SupportedAbilityStats.Contains(e.stat)));

        public static bool PrerequisitesMet(SkillNode n)
        {
            bool Met(SkillPrerequisite[] set) => Array.TrueForAll(set, p => Level(p.nodeId) >= p.minimumLevel);
            return Met(n.prerequisites) || (n.alternatives.Length > 0 && Met(n.alternatives));
        }

        public static SkillLevel NextLevel(SkillNode n)
        {
            int level = Level(n.id);
            return level < n.levels.Length ? n.levels[level] : null;
        }

        public static bool CanBuy(SkillNode n)
        {
            var next = NextLevel(n);
            return next != null && PrerequisitesMet(n) && Data.currency >= next.cost;
        }

        /// <summary>Buys up to <paramref name="levels"/> levels in a row, as far as currency reaches. Returns how many.</summary>
        public static int Buy(SkillNode n, int levels)
        {
            int bought = 0;
            while (bought < levels && Buy(n)) bought++;
            return bought;
        }

        public static bool Buy(SkillNode n)
        {
            if (!CanBuy(n)) return false;
            Data.currency -= NextLevel(n).cost;
            int i = Data.nodeIds.IndexOf(n.id);
            if (i < 0) { Data.nodeIds.Add(n.id); Data.nodeLevels.Add(1); }
            else Data.nodeLevels[i]++;
            Persist();
            return true;
        }

        /// <summary>Every effect of every bought level.</summary>
        static IEnumerable<SkillEffect> Bought()
        {
            for (int i = 0; i < Data.nodeIds.Count; i++)
            {
                var n = Node(Data.nodeIds[i]);
                if (n == null) continue;
                for (int l = 0; l < Data.nodeLevels[i] && l < n.levels.Length; l++)
                    foreach (var e in n.levels[l].effects)
                        if (Honoured(e)) yield return e;
            }
        }

        static float Sum(string kind, string target = null, string stat = null)
        {
            float total = 0;
            foreach (var e in Bought())
                if (e.kind == kind && (target == null || e.target == target) && (stat == null || e.stat == stat))
                    total += e.amount;
            return total;
        }

        static bool Has(string kind, string target)
        {
            foreach (var e in Bought())
                if (e.kind == kind && e.target == target) return true;
            return false;
        }

        public static TowerStats Tower(TowerDef def)
        {
            if (towerCache.TryGetValue(def, out var s)) return s;
            float M(string stat) => Mathf.Max(0.1f, 1f + Sum("tower_stat_multiplier_delta", def.Id, stat));
            s = new TowerStats
            {
                Unlocked = def.UnlockedByDefault || Has("unlock_tower", def.Id),
                Limit = def.Limit + Mathf.RoundToInt(Sum("tower_capacity_delta", def.Id)),
                Damage = def.Damage * M("damage"),
                Range = def.Range * M("range"),
                FireInterval = def.FireInterval / M("fireRate"),
                Splash = def.SplashRadius * M("splashRadius"),
                SlowDuration = def.SlowDuration * M("slowDuration"),
                ConeDegrees = Mathf.Min(180f, def.ConeDegrees * M("coneWidth")),
            };
            if (!s.Unlocked) s.Limit = 0;
            towerCache[def] = s;
            return s;
        }

        public static float RifleDamage => 1f + Sum(RifleEffect, "rifle", "damage");
        public static float RifleFireRate => 1f + Sum(RifleEffect, "rifle", "fireRate");

        public static int WallLimit => Walls.Barricade.Limit + Mathf.RoundToInt(Sum("wall_capacity_delta"));
        public static float WallHealth => Walls.Barricade.Health * (1f + Sum("wall_health_multiplier_delta"));
        public static int BaseHealthBonus => Mathf.RoundToInt(Sum("base_health_delta"));

        public static float AbilityCooldown(AbilityDef a) =>
            a.Cooldown / Mathf.Max(0.1f, 1f + Sum("ability_stat_multiplier_delta", a.Id, "cooldown"));
        public static float AbilityDamage(AbilityDef a) =>
            a.Damage * (1f + Sum("ability_stat_multiplier_delta", a.Id, "damage"));
        public static float AbilityRadius(AbilityDef a) =>
            a.Radius * (1f + Sum("ability_stat_multiplier_delta", a.Id, "radius"));

        /// <summary>Position of a round across the whole campaign, for the completion bonus. Off-campaign maps count as the start.</summary>
        static int CampaignRoundIndex(MapData map, int round)
        {
            var catalog = Session.Catalog;
            int index = 0;
            foreach (var id in catalog.campaign)
            {
                if (id == map.id) return index + round;
                index += catalog.Get(id).rounds.Length;
            }
            return round;
        }

        public struct RoundReward
        {
            public int Kills, Completion;
            public bool FirstClear;
            public int Total => Kills + Completion;
        }

        /// <summary>Pays out a finished round, as Orc Horde's settleRoundAttempt does.</summary>
        public static RoundReward Settle(MapData map, int round, float killCurrency, bool victory)
        {
            var reward = new RoundReward { Kills = Mathf.Max(0, Mathf.RoundToInt(killCurrency)) };
            if (victory)
            {
                string key = $"{map.id}:{round}";
                reward.FirstClear = !Data.clearedRounds.Contains(key);
                if (reward.FirstClear) Data.clearedRounds.Add(key);
                float multiplier = Mathf.Round((1f + CampaignRoundIndex(map, round) * RoundMultiplierStep) * 100f) / 100f;
                reward.Completion = Mathf.RoundToInt(reward.Kills * multiplier) * (reward.FirstClear ? FirstClearMultiplier : 1);
            }
            Data.currency += reward.Total;
            Persist();
            return reward;
        }

        /// <summary>Back to a fresh campaign: no currency, no skills, no clears.</summary>
        public static void Reset()
        {
            save = new Save();
            Persist();
        }
    }
}
