using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    public enum Phase { Build, Combat, Won, Lost, CampaignComplete }

    /// <summary>
    /// The round loop, after Orc Horde: build, fight, then win or lose and go
    /// again with the layout kept and the barricades put back up.
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
        /// <summary>Orc Horde swings its spawn intensity between these, in segments of a few seconds.</summary>
        const float MinIntensity = 0.6f, MaxIntensity = 1.4f;

        public MapDef Map { get; private set; }
        public int RoundIndex { get; private set; }
        public RoundDef Round => Map.Rounds[RoundIndex];
        public Phase Phase { get; private set; }
        public float RoundTime { get; private set; }
        public int BaseHealth { get; private set; }
        public int Kills { get; private set; }
        public int Currency { get; private set; }
        public bool Paused { get; private set; }
        public bool HelpOpen { get; set; } = true;

        public string Banner { get; private set; } = "";
        public float BannerUntil { get; private set; }
        public Color BannerColor { get; private set; } = Color.white;

        public float SpawnProgress => budget <= 0 ? 0 : Mathf.Clamp01(spent / budget);

        readonly List<(float at, EnemyDef def)> scheduled = new List<(float, EnemyDef)>();
        readonly List<(float at, string text)> warnings = new List<(float, string)>();
        float budget, spent, pool, intensity = 1f, nextSegment;
        EnemyDef next;

        public void Init(MapDef map)
        {
            I = this;
            Map = map;
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
            BaseHealth = Round.StartingHealth;
            ClearHorde();
            foreach (var b in Barricade.All) b.Restore();
            PlayerHealth.I?.Revive();
            Announce($"Round {RoundIndex + 1}: {Round.Name}  —  build your line, press G to start", Color.white, 6f);
        }

        public void StartRound()
        {
            if (Phase != Phase.Build) return;
            Phase = Phase.Combat;
            RoundTime = 0;
            budget = Round.PressureBudget * HordeScale;
            spent = pool = 0;
            nextSegment = 0;
            next = PickWeighted();

            scheduled.Clear();
            warnings.Clear();
            foreach (var g in Round.Guaranteed)
                for (int i = 0; i < g.count; i++) scheduled.Add((g.at + i * g.spacing, g.def));
            foreach (var boss in Round.Bosses)
            {
                scheduled.Add((boss.at, boss.def));
                warnings.Add((boss.at - boss.warning, $"{boss.def.Name} approaching {Map.SpawnName}!"));
            }
            Announce($"{Round.Name}  —  here they come", U.Hex(0xffb347));
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetPaused(!Paused);
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
                case Phase.CampaignComplete: RoundIndex = 0; EnterBuild(); break;
            }
        }

        void TickCombat(float dt)
        {
            RoundTime += dt;
            float window = Round.Duration * SpawnWindowFraction;

            if (RoundTime >= nextSegment)
            {
                nextSegment = RoundTime + Random.Range(5f, 12f);
                intensity = Random.Range(MinIntensity, MaxIntensity);
            }
            if (RoundTime < window && spent < budget) pool += budget / window * intensity * dt;

            while (next != null && pool >= next.Pressure && spent < budget && Enemy.All.Count < MaxAlive)
            {
                if (Enemy.Spawn(next, Map.Spawn) == null) break;
                pool -= next.Pressure;
                spent += next.Pressure;
                next = PickWeighted();
            }

            for (int i = warnings.Count - 1; i >= 0; i--)
                if (RoundTime >= warnings[i].at)
                {
                    Announce(warnings[i].text, U.Hex(0xff4a4a), 5f);
                    warnings.RemoveAt(i);
                }
            for (int i = scheduled.Count - 1; i >= 0; i--)
                if (RoundTime >= scheduled[i].at && Enemy.Spawn(scheduled[i].def, Map.Spawn, 1f) != null)
                    scheduled.RemoveAt(i);

            bool spawningDone = RoundTime >= window && scheduled.Count == 0;
            if (spawningDone && Enemy.All.Count == 0) Win();
        }

        EnemyDef PickWeighted()
        {
            float total = 0;
            foreach (var r in Round.Enemies) total += r.weight;
            float roll = Random.value * total;
            foreach (var r in Round.Enemies)
            {
                roll -= r.weight;
                if (roll <= 0) return r.def;
            }
            return Round.Enemies[Round.Enemies.Length - 1].def;
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
            if (e.Def.BreachLosesRound) Lose($"The {e.Def.Name} reached the {Map.BaseName}.");
            else if (BaseHealth <= 0) Lose($"The {Map.BaseName} has fallen.");
        }

        void Win()
        {
            if (RoundIndex >= Map.Rounds.Length - 1)
            {
                Phase = Phase.CampaignComplete;
                Announce($"{Map.Name} held!  All {Map.Rounds.Length} rounds survived. Press G to play again.", U.Hex(0x5effc4), 30f);
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
            Time.timeScale = paused ? 0f : 1f;
        }

        public void RestartCampaign()
        {
            foreach (var t in Tower.All.ToArray()) Destroy(t.gameObject);
            foreach (var b in Barricade.All.ToArray()) Destroy(b.gameObject);
            Tower.All.Clear();
            Barricade.All.Clear();
            Kills = Currency = 0;
            RoundIndex = 0;
            SetPaused(false);
            EnterBuild();
        }
    }
}
