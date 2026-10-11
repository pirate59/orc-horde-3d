using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    public enum Phase { Build, Combat, Won, Lost, MapHeld }

    /// <summary>
    /// The round loop, after Orc Horde: build, fight, then win or lose and go
    /// again with the layout kept and the barricades put back up. Hold all of a
    /// map's rounds and the next map opens.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }

        /// <summary>
        /// DEVIATION: Orc Horde's pressure budgets field hundreds of orcs a round,
        /// which a top-down sim can afford and a crowd of 3D agents cannot. Every
        /// budget is scaled by this, keeping the original mix and pacing.
        /// </summary>
        public const float HordeScale = 0.12f;
        const int MaxAlive = 110;
        /// <summary>Spawning stops short of the round's end so its tail can arrive.</summary>
        const float SpawnWindowFraction = 0.85f;
        /// <summary>Spawn intensity swings between these, in segments of a few seconds, as Orc Horde's pacing does.</summary>
        const float MinIntensity = 0.6f, MaxIntensity = 1.4f;
        const float SpawnScatter = 2.5f;

        public MapData Map { get; private set; }
        public HexGrid Grid { get; private set; }
        public int RoundIndex { get; private set; }
        public RoundData Round => Map.rounds[RoundIndex];
        public Phase Phase { get; private set; }
        public float RoundTime { get; private set; }
        public int BaseHealth { get; private set; }
        public int Kills { get; private set; }
        public int Currency { get; private set; }
        public bool Paused { get; private set; }
        public bool HelpOpen { get; set; } = true;
        public bool MapMenuOpen { get; set; }

        public string Banner { get; private set; } = "";
        public float BannerUntil { get; private set; }
        public Color BannerColor { get; private set; } = Color.white;

        public float SpawnProgress => budget <= 0 ? 0 : Mathf.Clamp01(spent / budget);

        readonly List<(float at, EnemyDef def, string spawn)> scheduled = new List<(float, EnemyDef, string)>();
        readonly List<(float at, string text)> warnings = new List<(float, string)>();
        readonly Dictionary<EnemyDef, float> spentByType = new Dictionary<EnemyDef, float>();
        float budget, spent, pool, intensity = 1f, nextSegment;
        EnemyDef next;

        public void Init(MapData map, HexGrid grid)
        {
            I = this;
            Map = map;
            Grid = grid;
            RoundIndex = 0;
            EnterBuild();
        }

        public void Announce(string text, Color color, float seconds = 4f)
        {
            Banner = text;
            BannerColor = color;
            BannerUntil = Time.unscaledTime + seconds;
        }

        void EnterBuild()
        {
            Phase = Phase.Build;
            RoundTime = 0;
            BaseHealth = Round.startingHealth;
            ClearHorde();
            foreach (var b in Barricade.All) b.Restore();
            PlayerHealth.I?.Revive();
            Announce($"Round {RoundIndex + 1}: {Round.name}  —  build your line, press G to start", Color.white, 6f);
        }

        public void StartRound()
        {
            if (Phase != Phase.Build) return;
            Phase = Phase.Combat;
            RoundTime = 0;
            budget = Round.pressureBudget * HordeScale;
            spent = pool = 0;
            nextSegment = 0;
            spentByType.Clear();
            next = null;

            scheduled.Clear();
            warnings.Clear();
            foreach (var g in Round.guaranteed)
            {
                var def = Enemies.ById(g.enemy);
                if (def == null) continue;
                for (int i = 0; i < g.count; i++) scheduled.Add(((g.atMs + i * g.spacingMs) / 1000f, def, g.spawn));
            }
            foreach (var boss in Round.bosses)
            {
                var def = Enemies.ById(boss.enemy);
                if (def == null) { Debug.LogWarning($"Unknown boss {boss.enemy}"); continue; }
                scheduled.Add((boss.atMs / 1000f, def, boss.spawn));
                warnings.Add(((boss.atMs - boss.warningMs) / 1000f, $"{def.Name} approaching {Map.Spawn(boss.spawn).name}!"));
            }
            Announce($"{Round.name}  —  here they come", U.Hex(0xffb347));
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (MapMenuOpen) MapMenuOpen = false;
                else SetPaused(!Paused);
            }
            if (Input.GetKeyDown(KeyCode.H)) HelpOpen = !HelpOpen;
            if (Paused) return;

            if (Input.GetKeyDown(KeyCode.G)) Continue();
            if (Phase == Phase.Combat) TickCombat(Time.deltaTime);
        }

        /// <summary>The G key: start the round, or move on from its result.</summary>
        public void Continue()
        {
            switch (Phase)
            {
                case Phase.Build: StartRound(); break;
                case Phase.Won: RoundIndex++; EnterBuild(); break;
                case Phase.Lost: EnterBuild(); break;
                case Phase.MapHeld:
                    var nextMap = Session.NextMap(Map.id);
                    if (nextMap != null) Session.Play(nextMap);
                    else { RoundIndex = 0; EnterBuild(); }
                    break;
            }
        }

        void TickCombat(float dt)
        {
            RoundTime += dt;
            float ms = RoundTime * 1000f;
            float window = Round.Duration * SpawnWindowFraction;

            if (RoundTime >= nextSegment)
            {
                nextSegment = RoundTime + Random.Range(5f, 12f);
                intensity = Random.Range(MinIntensity, MaxIntensity);
            }
            if (RoundTime < window && spent < budget) pool += budget / window * intensity * dt;

            while (spent < budget && Enemy.All.Count < MaxAlive)
            {
                next ??= PickEnemy(ms);
                if (next == null || pool < next.Pressure) break;
                if (Enemy.Spawn(next, Map.SpawnPosition(PickSpawn(ms)), SpawnScatter) == null) break;
                pool -= next.Pressure;
                spent += next.Pressure;
                spentByType[next] = spentByType.TryGetValue(next, out var s) ? s + next.Pressure : next.Pressure;
                next = null;
            }

            for (int i = warnings.Count - 1; i >= 0; i--)
                if (RoundTime >= warnings[i].at)
                {
                    Announce(warnings[i].text, U.Hex(0xff4a4a), 5f);
                    warnings.RemoveAt(i);
                }
            for (int i = scheduled.Count - 1; i >= 0; i--)
            {
                var (at, def, spawn) = scheduled[i];
                if (RoundTime < at) continue;
                var gate = string.IsNullOrEmpty(spawn) ? PickSpawn(ms) : spawn;
                if (Enemy.Spawn(def, Map.SpawnPosition(gate), 1f) != null) scheduled.RemoveAt(i);
            }

            bool spawningDone = RoundTime >= window && scheduled.Count == 0;
            if (spawningDone && Enemy.All.Count == 0) Win();
        }

        /// <summary>A weighted draw among the rules active now whose share of the budget still has room.</summary>
        EnemyDef PickEnemy(float ms)
        {
            float total = 0;
            foreach (var r in Round.enemyRules)
                if (Eligible(r, ms)) total += r.weight;
            if (total <= 0) return null;
            float roll = Random.value * total;
            foreach (var r in Round.enemyRules)
            {
                if (!Eligible(r, ms)) continue;
                roll -= r.weight;
                if (roll <= 0) return Enemies.ById(r.enemy);
            }
            return null;
        }

        bool Eligible(EnemyRule r, float ms)
        {
            if (ms < r.fromMs || ms > r.untilMs) return false;
            var def = Enemies.ById(r.enemy);
            if (def == null) return false;
            float used = spentByType.TryGetValue(def, out var s) ? s : 0;
            return used + def.Pressure <= r.maxFraction * budget + 0.001f;
        }

        string PickSpawn(float ms)
        {
            float total = 0;
            foreach (var r in Round.spawnRules)
                if (ms >= r.fromMs && ms <= r.untilMs) total += r.weight;
            float roll = Random.value * total;
            foreach (var r in Round.spawnRules)
            {
                if (ms < r.fromMs || ms > r.untilMs) continue;
                roll -= r.weight;
                if (roll <= 0) return r.spawn;
            }
            return Map.spawns[0].id;
        }

        public void OnKilled(Enemy e)
        {
            Kills++;
            Currency += e.Def.Reward;
        }

        public void OnBreach(Enemy e)
        {
            if (Phase != Phase.Combat) return;
            BaseHealth = Mathf.Max(0, BaseHealth - e.Def.BreachDamage);
            Fx.Burst(Map.Base + Vector3.up * 2, 3f, U.Hex(0xff4a4a), 0.4f);
            if (e.Def.BreachLosesRound) Lose($"The {e.Def.Name} reached the {Map.endpoint.name}.");
            else if (BaseHealth <= 0) Lose($"The {Map.endpoint.name} has fallen.");
        }

        void Win()
        {
            if (RoundIndex >= Map.rounds.Length - 1)
            {
                Phase = Phase.MapHeld;
                Session.MarkHeld(Map.id);
                var nextMap = Session.NextMap(Map.id);
                string onward = nextMap != null ? $"Press G to march on to {Session.Catalog.Get(nextMap).name}." : "Press G to play it again.";
                Announce($"{Map.name} held!  {onward}", U.Hex(0x5effc4), 30f);
            }
            else
            {
                Phase = Phase.Won;
                Announce($"Round {RoundIndex + 1} won!  Press G for the next round.", U.Hex(0x5effc4), 30f);
            }
            for (int i = 0; i < 8; i++)
                Fx.Burst(Map.Base + new Vector3(Random.Range(-6f, 6f), Random.Range(6f, 12f), Random.Range(-6f, 6f)),
                    Random.Range(1.5f, 3f), Color.HSVToRGB(Random.value, 0.7f, 1f), 1.2f);
        }

        void Lose(string reason)
        {
            Phase = Phase.Lost;
            ClearHorde();
            Announce($"{reason}  Press G to retry the round — your layout is kept.", U.Hex(0xff4a4a), 30f);
        }

        void ClearHorde()
        {
            foreach (var e in Enemy.All.ToArray()) e.Die(killed: false);
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            MapMenuOpen = false;
            Time.timeScale = paused ? 0f : 1f;
        }

        public void RestartMap() => Session.Play(Map.id);

        void OnDestroy()
        {
            Links.Clear();
            MapBuilder.ClearNavMesh();
            if (I == this) I = null;
        }
    }
}
