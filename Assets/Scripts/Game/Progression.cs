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
            "unlock_ability", "ability_stat_multiplier_delta", "ability_trait_delta",
            "base_shield_layer_delta", "base_shield_recharge_multiplier_delta", RifleEffect,
        };

        /// <summary>
        /// FPS-only: the player's rifle, which Orc Horde has no use for. Without it
        /// the one gun that never improves would fade to nothing against hordes the
        /// towers are being upgraded to meet. Priced alongside the Machine Gun line.
        /// </summary>
        const string RifleEffect = "rifle_stat_multiplier_delta";

        static SkillNode RifleNode(string id, string name, string description, string stat, float amount, int[] costs,
            float x, float y, SkillPrerequisite[] prerequisites)
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
                x = x, y = y, prerequisites = prerequisites, alternatives = new SkillPrerequisite[0], levels = levels,
            };
        }

        /// <summary>
        /// Placed in the web's one gap beside the root, between the walls and the
        /// base. Rifle Rounds hangs off Gun Nests at level 0, so it is open from
        /// the start as it always was, but drawn joined to the web.
        /// </summary>
        static readonly SkillNode[] RifleNodes =
        {
            RifleNode("rifle_damage", "Rifle Rounds", "Heavier rounds for your own rifle.", "damage", 0.15f,
                new[] { 500, 900, 1500, 2400, 3600, 5200, 7200, 9600 }, -80, 80,
                new[] { new SkillPrerequisite { nodeId = "machine_gun_capacity", minimumLevel = 0 } }),
            RifleNode("rifle_fire_rate", "Rifle Action", "A faster action for your own rifle.", "fireRate", 0.1f,
                new[] { 700, 1300, 2100, 3400, 5400, 8000 }, -105, 185,
                new[] { new SkillPrerequisite { nodeId = "rifle_damage", minimumLevel = 1 } }),
        };

        static readonly HashSet<string> SupportedTowerStats = new HashSet<string>
            { "damage", "fireRate", "range", "splashRadius", "slowDuration", "coneWidth" };

        static readonly HashSet<string> SupportedAbilities = new HashSet<string>(Array.ConvertAll(Abilities.All, a => a.Id));
        static readonly HashSet<string> SupportedAbilityStats = new HashSet<string>
            { "cooldown", "damage", "radius", "length", "width", "duration", "repair", "slipChance", "shellRate" };

        [Serializable]
        sealed class Save
        {
            public int currency, valor, glory;
            /// <summary>Premium payouts already made, by key, so each is paid once.</summary>
            public List<string> awards = new List<string>();
            /// <summary>Rounds won with the base untouched, as map:round: a map of them pays Glory.</summary>
            public List<string> flawlessRounds = new List<string>();
            /// <summary>The ability in each hand, Q then E, by id.</summary>
            public List<string> loadout = new List<string>();
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
            if (n.levels.Length == 0 || n.levels[0].effects.Length == 0) return "Other";
            var e = n.levels[0].effects[0];
            if (e.kind.StartsWith("wall_")) return "Barricades";
            if (e.kind.StartsWith("base_")) return "Base";
            if (e.target == "rifle") return "Rifle";
            foreach (var t in Towers.All) if (t.Id == e.target) return t.Name;
            return Abilities.ById(e.target)?.Name ?? "Other";
        }

        static readonly string[] GroupOrder =
        {
            "Rifle", "Machine Gun", "Cannon", "Laser", "Arc Tower", "Flame Thrower", "Barricades", "Base",
            "Mortar", "Bombing Run", "Emergency Repairs", "Oil Slick", "Rallying Cry", "Sentry Drop",
            "Proximity Mines", "Artillery Barrage", "Decoy Beacon", "Other",
        };

        /// <summary>The nodes this game can honour, grouped by line, cheapest first within each.</summary>
        public static IReadOnlyList<SkillNode> Offered
        {
            get
            {
                if (offered != null) return offered;
                var ok = new HashSet<string>();
                foreach (var n in Tree.nodes)
                    if (IsSupported(n))
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
            (e.kind != "ability_stat_multiplier_delta" || (SupportedAbilities.Contains(e.target) && SupportedAbilityStats.Contains(e.stat))) &&
            (e.kind != "ability_trait_delta" && e.kind != "unlock_ability" || SupportedAbilities.Contains(e.target));

        /// <summary>Whether this game does everything the node's levels do. The rest show in the web but can't be bought yet.</summary>
        public static bool IsSupported(SkillNode n) => Array.TrueForAll(n.levels, l => Array.TrueForAll(l.effects, Honoured));

        /// <summary>What a currency stands at: currency, Valor or Glory.</summary>
        public static int Balance(string currencyId) => currencyId switch
        {
            "valor" => Data.valor,
            "glory" => Data.glory,
            _ => Data.currency,
        };

        static void Spend(string currencyId, int amount)
        {
            switch (currencyId)
            {
                case "valor": Data.valor -= amount; break;
                case "glory": Data.glory -= amount; break;
                default: Data.currency -= amount; break;
            }
        }

        public static int Valor => Data.valor;
        public static int Glory => Data.glory;

        /// <summary>Orc Horde's skill-web visibility: owned, open to buy, a locked outline one step out, or not drawn.</summary>
        public enum Visibility { Hidden, Hinted, Open, Owned }

        public static Visibility VisibilityOf(SkillNode n)
        {
            if (Level(n.id) > 0) return Visibility.Owned;
            if (PrerequisitesMet(n)) return Visibility.Open;
            foreach (var set in new[] { n.prerequisites, n.alternatives })
                foreach (var p in set)
                {
                    var parent = Node(p.nodeId);
                    if (parent != null && (Level(parent.id) > 0 || PrerequisitesMet(parent))) return Visibility.Hinted;
                }
            return Visibility.Hidden;
        }

        public static SkillNode NodeById(string id) => Node(id);

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
            return next != null && IsSupported(n) && PrerequisitesMet(n) && Balance(n.currency) >= next.cost;
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
            Spend(n.currency, NextLevel(n).cost);
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

        public static bool AbilityUnlocked(AbilityDef a) => a.UnlockedByDefault || Has("unlock_ability", a.Id);

        /// <summary>An ability stat's bought bonus: 0.2 for +20%.</summary>
        public static float AbilityBonus(AbilityDef a, string stat) => Sum("ability_stat_multiplier_delta", a.Id, stat);

        /// <summary>A trait's bought total: a count, a fraction, or 1 for a switch, as Orc Horde's statResolver has it.</summary>
        public static float AbilityTrait(AbilityDef a, string trait) => Sum("ability_trait_delta", a.Id, trait);

        public static float AbilityCooldown(AbilityDef a) => a.Cooldown / Mathf.Max(0.1f, 1f + AbilityBonus(a, "cooldown"));
        public static float AbilityDamage(AbilityDef a) => a.Damage * (1f + AbilityBonus(a, "damage"));
        public static float AbilityRadius(AbilityDef a) => a.Radius * (1f + AbilityBonus(a, "radius"));
        public static float AbilityDuration(AbilityDef a) => a.Duration * (1f + AbilityBonus(a, "duration"));
        public static float AbilityRepair(AbilityDef a) => Mathf.Min(1f, a.RepairFraction * (1f + AbilityBonus(a, "repair")));
        public static float AbilitySlipChance(AbilityDef a) => a.SlipChance * (1f + AbilityBonus(a, "slipChance"));
        public static float AbilityShellInterval(AbilityDef a) => a.ShellInterval / Mathf.Max(0.1f, 1f + AbilityBonus(a, "shellRate"));

        /// <summary>Shield layers over the base: each soaks one breach whatever it costs.</summary>
        public static int ShieldLayers => Mathf.Max(0, Mathf.RoundToInt(Sum("base_shield_layer_delta")));
        /// <summary>Seconds to bring one spent layer back.</summary>
        public static float ShieldRecharge => 5f / Mathf.Max(0.1f, 1f + Sum("base_shield_recharge_multiplier_delta"));

        /// <summary>
        /// The two abilities carried into a round, Q then E, as Orc Horde's
        /// abilityLoadout resolves them: saved choices that are unlocked and not
        /// repeated, then any gap filled with Mortar, Bombing Run, then whatever
        /// else is unlocked.
        /// </summary>
        public static AbilityDef[] Loadout
        {
            get
            {
                var slots = new AbilityDef[2];
                for (int i = 0; i < 2; i++)
                {
                    var saved = i < Data.loadout.Count ? Abilities.ById(Data.loadout[i]) : null;
                    if (saved != null && AbilityUnlocked(saved) && Array.IndexOf(slots, saved) < 0) slots[i] = saved;
                }
                var fillers = new List<AbilityDef> { Abilities.Mortar, Abilities.BombingRun };
                fillers.AddRange(Abilities.All);
                for (int i = 0; i < 2; i++)
                    if (slots[i] == null)
                        slots[i] = fillers.Find(a => AbilityUnlocked(a) && Array.IndexOf(slots, a) < 0);
                return slots;
            }
        }

        /// <summary>Puts an ability in a hand. If it was in the other hand, the two swap.</summary>
        public static void Equip(int slot, AbilityDef ability)
        {
            var current = Loadout;
            if (current[1 - slot] == ability) current[1 - slot] = current[slot];
            current[slot] = ability;
            Data.loadout = new List<string> { current[0]?.Id ?? "", current[1]?.Id ?? "" };
            Persist();
        }

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
            public int Kills, Completion, Valor, Glory;
            public bool FirstClear;
            public int Total => Kills + Completion;
        }

        /// <summary>Pays out a finished round, as Orc Horde's settleRoundAttempt does.</summary>
        public static RoundReward Settle(MapData map, int round, float killCurrency, bool victory, bool flawless = false)
        {
            var reward = new RoundReward { Kills = Mathf.Max(0, Mathf.RoundToInt(killCurrency)) };
            if (victory)
            {
                // Orc Horde's premium payouts, each once per campaign: Valor for the first win of a
                // round with a set-piece boss, Glory once every round of a map has been won flawless.
                if (map.rounds[round].bosses.Length > 0 && Claim($"valor:{map.id}:{round}")) { Data.valor++; reward.Valor = 1; }
                if (flawless && !Data.flawlessRounds.Contains($"{map.id}:{round}")) Data.flawlessRounds.Add($"{map.id}:{round}");
                bool allFlawless = true;
                for (int i = 0; i < map.rounds.Length; i++) allFlawless &= Data.flawlessRounds.Contains($"{map.id}:{i}");
                if (allFlawless && Claim($"glory:{map.id}")) { Data.glory++; reward.Glory = 1; }

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

        /// <summary>Records a once-only award as paid. False if it already was.</summary>
        static bool Claim(string key)
        {
            if (Data.awards.Contains(key)) return false;
            Data.awards.Add(key);
            return true;
        }

        /// <summary>For the smoke test: every node that touches an ability, bought to the top, free.</summary>
        public static void GrantAbilitiesForTest()
        {
            foreach (var n in Tree.nodes)
            {
                bool ability = Array.Exists(n.levels, l => Array.Exists(l.effects, e => e.kind.StartsWith("ability_") || e.kind == "unlock_ability"));
                if (!ability) continue;
                int i = Data.nodeIds.IndexOf(n.id);
                if (i < 0) { Data.nodeIds.Add(n.id); Data.nodeLevels.Add(n.levels.Length); }
                else Data.nodeLevels[i] = n.levels.Length;
            }
            Persist();
        }

        /// <summary>For the smoke test: a purse to spend.</summary>
        public static void GrantForTest(int currency, int valor, int glory)
        {
            Data.currency += currency;
            Data.valor += valor;
            Data.glory += glory;
            Persist();
        }

        /// <summary>Back to a fresh campaign: no currency, no skills, no clears.</summary>
        public static void Reset()
        {
            save = new Save();
            Persist();
        }
    }
}
