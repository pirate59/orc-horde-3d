using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>Immediate-mode HUD: round state, health, ammo, abilities, the build bar and boss bars.</summary>
    public sealed class Hud : MonoBehaviour
    {
        static readonly Color Mint = U.Hex(0x5effc4), Amber = U.Hex(0xffb347), Red = U.Hex(0xff4a4a);
        static readonly Color Panel = new Color(0.05f, 0.06f, 0.08f, 0.72f);

        GUIStyle label, big, small, center, banner;
        float scale;

        void EnsureStyles()
        {
            scale = Mathf.Max(1f, Screen.height / 1080f);
            if (label != null && Mathf.Approximately(label.fontSize, Mathf.RoundToInt(18 * scale))) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * scale), richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(14 * scale), wordWrap = true };
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(30 * scale), fontStyle = FontStyle.Bold };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            banner = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(24 * scale), wordWrap = true };
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Bar(Rect r, float fill, Color c)
        {
            Box(r, new Color(0, 0, 0, 0.6f));
            Box(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(fill), r.height - 4), c);
        }

        float S(float v) => v * scale;

        /// <summary>No HUD at all: for clean screenshots.</summary>
        public static bool Hidden;

        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null || Hidden) return;
            EnsureStyles();

            DrawBossBars();
            DrawTopBar(gm);
            DrawPlayer();
            DrawAbilities();
            bool tactical = PlayerController.I != null && PlayerController.I.Tactical;
            if (gm.Phase == Phase.Build && !gm.ShopOpen) DrawBuildBar();
            DrawCurrency(gm);
            DrawPerf();
            if (gm.Phase == Phase.Build && !gm.ShopOpen && !gm.Paused) DrawBriefing(gm);
            if (gm.Phase == Phase.Build && !gm.ShopOpen) DrawInspection();
            if (!gm.ShopOpen && !tactical) DrawCrosshair();
            if (!gm.ShopOpen) DrawBanner(gm);
            if (PlayerController.AwaitingClick && !gm.Paused && !gm.ShopOpen) DrawClickToPlay();
            if (gm.HelpOpen && !gm.Paused && !gm.ShopOpen && !tactical) DrawHelp();
            if (gm.ShopOpen && !gm.Paused) DrawShop(gm);
            if (gm.Paused) DrawPause(gm);
        }

        void DrawTopBar(GameManager gm)
        {
            var r = new Rect(Screen.width / 2f - S(320), S(12), S(640), S(70));
            Box(r, Panel);
            string phase = gm.Phase switch
            {
                Phase.Build => "<color=#5effc4>BUILD</color>  ·  <b>G</b> starts the wave",
                Phase.Combat => $"<color=#ffb347>FIGHT</color>  {Mathf.Max(0, gm.Round.Duration - gm.RoundTime):0}s" +
                    (gm.Toughness > 1.01f ? $"  <color=#ff8a8a>horde ×{gm.Toughness:0.0} health</color>" : ""),
                Phase.Won => "<color=#5effc4>ROUND WON</color>",
                Phase.Lost => "<color=#ff4a4a>ROUND LOST</color>",
                _ => "<color=#5effc4>MAP HELD</color>",
            };
            GUI.Label(new Rect(r.x + S(14), r.y + S(6), S(400), S(30)),
                $"<b>{gm.Map.name}</b> · Round {gm.RoundIndex + 1}/{gm.Map.rounds.Length}: {gm.Round.name}", label);
            GUI.Label(new Rect(r.x + S(14), r.y + S(36), S(300), S(30)), phase, label);
            GUI.Label(new Rect(r.xMax - S(200), r.y + S(6), S(190), S(30)),
                $"Orcs: {Enemy.All.Count}   Kills: {gm.Kills}", label);

            GUI.Label(new Rect(r.xMax - S(200), r.y + S(36), S(60), S(30)), "Base", label);
            Bar(new Rect(r.xMax - S(145), r.y + S(40), S(130), S(20)), gm.BaseHealth / (float)gm.MaxBaseHealth,
                gm.BaseHealth > gm.MaxBaseHealth / 3 ? Mint : Red);
            GUI.Label(new Rect(r.xMax - S(145), r.y + S(37), S(130), S(26)),
                $"<color=#0d1014><b>{gm.BaseHealth}/{gm.MaxBaseHealth}</b></color>", center);

            if (gm.Phase == Phase.Combat)
                Bar(new Rect(r.x, r.yMax, r.width, S(5)), gm.SpawnProgress, Amber);

            // Shield layers over the base, one pip each, the next one filling as it recharges.
            if (gm.MaxShields > 0)
            {
                float pip = S(14), sx = r.xMax - S(145);
                for (int i = 0; i < gm.MaxShields; i++)
                {
                    var p = new Rect(sx + i * (pip + S(4)), r.yMax + S(8), pip, S(6));
                    Box(p, new Color(0, 0, 0, 0.6f));
                    float fill = i < gm.Shields ? 1f : i == gm.Shields ? gm.ShieldRecharge : 0f;
                    Box(new Rect(p.x, p.y, p.width * fill, p.height), i < gm.Shields ? U.Hex(0x7ad0ff) : new Color(0.48f, 0.82f, 1f, 0.4f));
                }
            }
        }

        void DrawPlayer()
        {
            var hp = PlayerHealth.I;
            var w = PlayerWeapon.I;
            if (hp == null || w == null) return;

            var r = new Rect(S(20), Screen.height - S(80), S(300), S(60));
            Box(r, Panel);
            GUI.Label(new Rect(r.x + S(12), r.y + S(6), S(200), S(24)), "Health", small);
            Bar(new Rect(r.x + S(12), r.y + S(28), r.width - S(24), S(20)), hp.Health / PlayerHealth.MaxHealth,
                hp.Health > 35 ? Mint : Red);

            var a = new Rect(Screen.width - S(220), Screen.height - S(80), S(200), S(60));
            Box(a, Panel);
            string ammo = w.Reloading ? "<color=#ffb347>RELOADING</color>" : $"<b>{w.Ammo}</b> / {PlayerWeapon.MagazineSize}";
            GUI.Label(new Rect(a.x + S(14), a.y + S(4), a.width, S(50)), ammo, big);

            if (Time.time - hp.LastHurt < 0.25f) Box(new Rect(0, 0, Screen.width, Screen.height), new Color(1, 0, 0, 0.12f));
            if (!hp.Alive)
            {
                Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0.3f, 0, 0, 0.45f));
                GUI.Label(new Rect(0, Screen.height / 2f - S(30), Screen.width, S(60)), $"Respawning in {hp.RespawnIn:0.0}s", banner);
            }
        }

        void DrawAbilities()
        {
            var ab = PlayerAbilities.I;
            if (ab == null) return;
            float w = S(190), h = S(54);
            float x = Screen.width / 2f - w - S(6), y = Screen.height - h - S(20);
            DrawAbility(new Rect(x, y, w, h), "Q", ab.Left, ab);
            DrawAbility(new Rect(x + w + S(12), y, w, h), "E", ab.Right, ab);
        }

        void DrawAbility(Rect r, string key, AbilityDef a, PlayerAbilities ab)
        {
            if (a == null) return;
            Box(r, Panel);
            Box(new Rect(r.x, r.y, S(4), r.height), a.Color);
            float cd = ab.CooldownLeft(a);
            if (cd > 0) Box(new Rect(r.x, r.y, r.width * cd / Progression.AbilityCooldown(a), r.height), new Color(1, 1, 1, 0.08f));
            if (a == Abilities.RallyingCry && AbilityFields.RallyLeft > 0)
                Box(new Rect(r.x, r.yMax - S(4), r.width * AbilityFields.RallyLeft, S(4)), a.Color);
            string state = !ab.Usable ? "<color=#888>in rounds only</color>"
                : cd > 0 ? $"<color=#ffb347>{cd:0.0}s</color>"
                : ab.AimProblem != null && Input.GetKey(key == "Q" ? KeyCode.Q : KeyCode.E) ? $"<color=#ff8a8a>{ab.AimProblem}</color>"
                : "<color=#5effc4>ready</color>";
            GUI.Label(new Rect(r.x + S(10), r.y + S(4), r.width, S(24)), $"<b>[{key}]</b> {a.Name}", small);
            GUI.Label(new Rect(r.x + S(10), r.y + S(26), r.width, S(24)), state, small);
        }

        void DrawBuildBar()
        {
            var bc = BuildController.I;
            if (bc == null) return;
            float w = S(132), h = S(64), gap = S(6);
            int n = Towers.All.Length + 1;
            float x = Screen.width / 2f - (w * n + gap * (n - 1)) / 2f, y = Screen.height - S(170);

            for (int i = 0; i < n; i++)
            {
                var r = new Rect(x + i * (w + gap), y, w, h);
                bool isWall = i == Towers.All.Length;
                bool selected = isWall ? bc.Tool == BuildTool.Barricade : bc.SelectedTower == Towers.All[i] && bc.Tool == BuildTool.Tower;
                string name = isWall ? Walls.Barricade.Name : Towers.All[i].Name;
                bool locked = !isWall && !Progression.Tower(Towers.All[i]).Unlocked;
                string count = isWall
                    ? $"{Barricade.All.Count}/{Progression.WallLimit}"
                    : locked ? "<color=#888>locked · B</color>"
                    : $"{BuildController.Placed(Towers.All[i])}/{Progression.Tower(Towers.All[i]).Limit}";
                Box(r, selected ? new Color(0.37f, 1f, 0.77f, 0.35f) : Panel);
                Box(new Rect(r.x, r.y, r.width, S(4)), isWall ? Walls.Barricade.Color : Towers.All[i].Color);
                GUI.Label(new Rect(r.x + S(8), r.y + S(8), r.width, S(22)), $"<b>[{i + 1}]</b> {name}", small);
                GUI.Label(new Rect(r.x + S(8), r.y + S(34), r.width, S(22)), count, small);
            }

            string info = bc.Tool switch
            {
                BuildTool.Tower => $"<b>{bc.SelectedTower.Name}</b> — {bc.SelectedTower.Description}",
                BuildTool.Barricade => $"<b>Barricade</b> — {Walls.Barricade.Description}",
                _ => PlayerController.I.Tactical
                    ? "Tactical view: WASD pan · Q/E turn · wheel zoom · T walk from here · M back on foot"
                    : "Pick a tower [1–5] or barricade [6]. M for the tactical view. B skill tree. G starts the wave.",
            };
            GUI.Label(new Rect(Screen.width / 2f - S(420), y - S(56), S(840), S(26)), info, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            if (!string.IsNullOrEmpty(bc.Hint))
                GUI.Label(new Rect(Screen.width / 2f - S(420), y - S(30), S(840), S(26)),
                    bc.GhostValid ? $"<color=#5effc4>{bc.Hint}</color>" : $"<color=#ff8a8a>{bc.Hint}</color>",
                    new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        }

        void DrawCrosshair()
        {
            var w = PlayerWeapon.I;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            // Down the sight, the holo reticle does the job: the crosshair fades away.
            var c = new Color(1, 1, 1, 0.85f * (1f - (w != null ? w.Aim : 0f)));
            Box(new Rect(cx - S(10), cy - S(1), S(7), S(2)), c);
            Box(new Rect(cx + S(3), cy - S(1), S(7), S(2)), c);
            Box(new Rect(cx - S(1), cy - S(10), S(2), S(7)), c);
            Box(new Rect(cx - S(1), cy + S(3), S(2), S(7)), c);
            if (w != null && Time.time < w.HitMarkerUntil)
            {
                var hc = w.LastHitWasHead ? Red : Color.white;
                float d = S(9);
                Box(new Rect(cx - d - S(3), cy - d - S(3), S(5), S(5)), hc);
                Box(new Rect(cx + d - S(2), cy - d - S(3), S(5), S(5)), hc);
                Box(new Rect(cx - d - S(3), cy + d - S(2), S(5), S(5)), hc);
                Box(new Rect(cx + d - S(2), cy + d - S(2), S(5), S(5)), hc);
            }
        }

        void DrawBossBars()
        {
            var cam = PlayerController.I != null ? PlayerController.I.Cam : null;
            if (cam == null) return;
            foreach (var e in Enemy.All)
            {
                bool always = e.Def.ShowsHealthBar;
                if (!always && e.Health >= e.MaxHealth) continue;
                var p = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (e.Def.Height + 0.3f));
                if (p.z <= 0 || p.z > (always ? 80f : 25f)) continue;
                float w = always ? S(90) : S(36);
                var r = new Rect(p.x - w / 2, Screen.height - p.y, w, always ? S(9) : S(5));
                Bar(r, e.Health / e.MaxHealth, always ? Red : Amber);
                if (always) GUI.Label(new Rect(r.x - S(30), r.y - S(22), r.width + S(60), S(22)), e.Def.Name, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            }
        }

        void DrawBanner(GameManager gm)
        {
            if (Time.unscaledTime > gm.BannerUntil || string.IsNullOrEmpty(gm.Banner)) return;
            var r = new Rect(Screen.width / 2f - S(450), S(100), S(900), S(56));
            Box(r, Panel);
            var old = GUI.contentColor;
            GUI.contentColor = gm.BannerColor;
            GUI.Label(r, gm.Banner, banner);
            GUI.contentColor = old;
        }

        void DrawHelp()
        {
            var r = new Rect(S(20), S(100), S(340), S(440));
            Box(r, Panel);
            GUI.Label(new Rect(r.x + S(14), r.y + S(10), r.width - S(28), r.height - S(20)),
                $"<b>{GameManager.I.Map.name.ToUpper()}</b>\n" +
                $"{GameManager.I.Map.description}\n\n" +
                "<b>Move</b>  WASD · Shift sprint · Space jump\n" +
                "<b>Shoot</b>  Left mouse · <b>right mouse aims</b> · R reload · headshots ×2\n" +
                "<b>Abilities</b>  hold Q / E to aim, release\n\n" +
                "<b>Between rounds</b>\n" +
                "1–5 towers · 6 barricade · R rotate\n" +
                "Left click place · X / right click remove\n" +
                "F pick up and move · 0 put away\n" +
                "<b>M tactical view</b> · V routes · <b>B skill tree</b>\n" +
                "<b>G start the wave</b>\n\n" +
                "Towers stand on the raised ground beside the path, one per hex. Barricades go on the path and reroute the horde.\n\n" +
                "H hide help · Esc pause / choose map", small);
        }

        /// <summary>In a browser the mouse can only be captured on a click: ask for one.</summary>
        void DrawClickToPlay()
        {
            var r = new Rect(Screen.width / 2f - S(200), Screen.height / 2f - S(50), S(400), S(100));
            Box(r, Panel);
            GUI.Label(new Rect(r.x, r.y + S(14), r.width, S(36)), "<b>Click to play</b>", banner);
            GUI.Label(new Rect(r.x, r.y + S(56), r.width, S(30)), "<color=#bbb>Esc frees the mouse and pauses</color>", center);
        }

        void DrawPause(GameManager gm)
        {
            Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            var btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * scale) };
            if (gm.MapMenuOpen) { DrawMapMenu(gm, btn); return; }

            bool cloud = CloudSave.Available, web = Application.platform == RuntimePlatform.WebGLPlayer;
            float tall = S(490) + (cloud ? S(140) : 0) - (web ? S(50) : 0);
            var r = new Rect(Screen.width / 2f - S(170), Screen.height / 2f - tall / 2, S(340), tall);
            Box(r, Panel);
            GUI.Label(new Rect(r.x, r.y + S(10), r.width, S(40)), "Paused", banner);
            float y = r.y + S(60);
            bool Button(string text)
            {
                bool pressed = GUI.Button(new Rect(r.x + S(40), y, r.width - S(80), S(40)), text, btn);
                y += S(50);
                return pressed;
            }
            if (Button("Resume")) gm.SetPaused(false);
            GUI.Label(new Rect(r.x + S(40), y, S(90), S(30)), "Volume", label);
            float vol = GUI.HorizontalSlider(new Rect(r.x + S(130), y + S(10), r.width - S(170), S(20)), Sfx.Volume, 0f, 1f);
            if (!Mathf.Approximately(vol, Sfx.Volume)) Sfx.Volume = vol;
            y += S(50);
            // Time of day: each map's own, or one forced everywhere.
            GUI.Label(new Rect(r.x + S(40), y, S(90), S(30)), "Sky", label);
            var choices = new Mood?[] { null, Mood.Day, Mood.Dusk, Mood.Night, Mood.Overcast };
            int at = System.Array.IndexOf(choices, Sky.Override);
            string name = Sky.Override.HasValue ? Sky.Override.Value.ToString() : $"Auto: {Sky.Current}";
            if (GUI.Button(new Rect(r.x + S(130), y, S(40), S(32)), "<", btn)) { Sky.Override = choices[(at + choices.Length - 1) % choices.Length]; Sky.Apply(gm.Map); }
            GUI.Label(new Rect(r.x + S(172), y + S(4), r.width - S(252), S(28)), name, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
            if (GUI.Button(new Rect(r.xMax - S(80), y, S(40), S(32)), ">", btn)) { Sky.Override = choices[(at + 1) % choices.Length]; Sky.Apply(gm.Map); }
            y += S(50);
            if (Button("Choose map")) gm.MapMenuOpen = true;
            if (Button("Restart map")) gm.RestartMap();
            if (gm.Phase == Phase.Build && Button("Clear layout")) { BuildController.ClearLayout(); gm.SetPaused(false); }
            if (Button("Reset campaign")) gm.ResetCampaign();
            if (cloud)
            {
                // The cloud copy, and the code that carries the campaign to another device.
                var colour = CloudSave.Status == CloudSave.State.Synced ? "#5effc4" : CloudSave.Status == CloudSave.State.Refused ? "#ff8a8a" : "#ffb347";
                GUI.Label(new Rect(r.x + S(24), y - S(4), r.width - S(48), S(44)), $"<color={colour}>{CloudSave.Note}</color>", small);
                y += S(40);
                if (Button("Copy save code")) CloudSave.CopyCode();
                if (Button(web ? "Use a save code..." : "Use save code from clipboard")) CloudSave.EnterCode();
            }
            // A browser tab can't be quit from inside the page.
            if (!web && Button("Quit"))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        /// <summary>
        /// What the next wave brings: its gates and how long each walk to the base
        /// is, the enemy mix, bosses and when they come, and how tough the horde is.
        /// Read from the same rules the round will spawn by.
        /// </summary>
        void DrawBriefing(GameManager gm)
        {
            var round = gm.Round;
            var lines = new System.Text.StringBuilder();
            lines.Append($"<b>NEXT WAVE</b>  <color=#bbb>{round.Duration:0}s · base {gm.MaxBaseHealth}</color>\n");
            float tough = gm.RoundToughness;
            if (tough > 1.01f) lines.Append($"<color=#ff8a8a>Orcs have ×{tough:0.0} health</color>\n");

            lines.Append("\n<b>Gates</b>\n");
            var routes = RoutePreview.I != null ? RoutePreview.I.Routes : null;
            if (routes != null)
                foreach (var r in routes)
                {
                    string walk = r.Sealed ? "<color=#ff6a6a>SEALED — they'll break through</color>"
                        : $"{r.Length:0} m, ~{r.Length / Enemies.Fodder.Speed:0}s walk";
                    string share = r.Share > 0 ? $"{r.Share:P0}" : "boss";
                    lines.Append($"{r.Gate.name}  <color=#bbb>{share} · {walk}</color>\n");
                }

            lines.Append("\n<b>Horde</b>\n");
            var weights = new System.Collections.Generic.Dictionary<string, float>();
            float total = 0, pressure = 0;
            foreach (var e in round.enemyRules)
            {
                weights[e.enemy] = (weights.TryGetValue(e.enemy, out var w) ? w : 0) + e.weight;
                total += e.weight;
                var def = Enemies.ById(e.enemy);
                if (def != null) pressure += e.weight * def.Pressure;
            }
            foreach (var kv in weights)
            {
                var def = Enemies.ById(kv.Key);
                if (def == null) continue;
                lines.Append($"<color=#{ColorUtility.ToHtmlStringRGB(def.Color)}>■</color> {def.Name}  <color=#bbb>{kv.Value / total:P0}</color>\n");
            }
            if (pressure > 0)
                lines.Append($"<color=#bbb>about {gm.RoundBudget / (pressure / total):0} orcs</color>\n");

            if (round.bosses.Length > 0 || round.guaranteed.Length > 0)
            {
                lines.Append("\n<b>Set pieces</b>\n");
                foreach (var b in round.bosses)
                    lines.Append($"<color=#ff8a8a>{Enemies.ById(b.enemy)?.Name ?? b.enemy}</color>  <color=#bbb>{b.atMs / 1000:0}s · {gm.Map.Spawn(b.spawn).name}</color>\n");
                foreach (var g in round.guaranteed)
                    lines.Append($"{g.count}× {Enemies.ById(g.enemy)?.Name ?? g.enemy}  <color=#bbb>{g.atMs / 1000:0}s</color>\n");
            }

            var text = lines.ToString();
            float h = small.CalcHeight(new GUIContent(text), S(260)) + S(20);
            var rect = new Rect(Screen.width - S(300), S(122), S(280), h);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + S(12), rect.y + S(10), S(260), h), text, small);
        }

        /// <summary>What the aim rests on, between rounds: its numbers and what can be done with it.</summary>
        void DrawInspection()
        {
            var bc = BuildController.I;
            if (bc == null || bc.Inspected == null) return;
            string text;
            if (bc.Inspected is Tower t)
            {
                var s = t.Stats;
                string dps = $"{s.Damage / s.FireInterval:0.#} damage/s";
                string height = Fx.HeightNote(t.RangeFactor);
                text = $"<b>{t.Def.Name}</b>\n{dps} · range {t.Range:0.#} m · every {s.FireInterval:0.00}s" +
                       (s.Splash > 0 ? $" · splash {s.Splash:0.#} m" : "") +
                       (height.Length > 0 ? "\n" + height : "") +
                       "\n<color=#bbb>F move · X remove</color>";
            }
            else if (bc.Inspected is Barricade b)
                text = $"<b>Barricade</b>\n{b.MaxHealth:0} health\n<color=#bbb>F move · X remove</color>";
            else return;
            var rect = new Rect(Screen.width / 2f + S(30), Screen.height / 2f + S(20), S(320), S(76));
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + S(12), rect.y + S(8), rect.width - S(24), rect.height), text, small);
        }

        // --- Performance ticker ------------------------------------------------------------

        float perfFps, perfMs, perfTps, perfWindow, perfWorst, worstShown;
        int perfFrames, perfTicks;

        /// <summary>Counts physics ticks: FixedUpdate runs once per tick, however the frames fall.</summary>
        void FixedUpdate() => perfTicks++;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            perfFrames++;
            perfWindow += dt;
            perfWorst = Mathf.Max(perfWorst, dt);
            if (perfWindow < 0.5f) return;
            perfFps = perfFrames / perfWindow;
            perfMs = perfWindow / perfFrames * 1000f;
            // Ticks per real second, so a paused or fast-forwarded game shows what it's really doing.
            perfTps = perfTicks / perfWindow;
            worstShown = perfWorst * 1000f;
            perfFrames = perfTicks = 0;
            perfWindow = perfWorst = 0;
        }

        /// <summary>
        /// Frames per second (and the average and worst frame in milliseconds), and physics ticks
        /// per second: when the frame rate drops, TPS shows whether the simulation still keeps time.
        /// </summary>
        void DrawPerf()
        {
            var r = new Rect(Screen.width - S(240), S(88), S(220), S(26));
            Box(r, Panel);
            string colour = perfFps >= 55 ? "#5effc4" : perfFps >= 30 ? "#ffb347" : "#ff6a6a";
            GUI.Label(new Rect(r.x + S(10), r.y + S(3), r.width, r.height),
                $"<color={colour}><b>{perfFps:0}</b> FPS</color>  <color=#bbb>{perfMs:0.0}/{worstShown:0} ms · <b>{perfTps:0}</b> TPS</color>", small);
        }

        void DrawCurrency(GameManager gm)
        {
            var r = new Rect(Screen.width - S(240), S(12), S(220), S(70));
            Box(r, Panel);
            GUI.Label(new Rect(r.x + S(14), r.y + S(6), r.width, S(30)), $"<b>{Progression.Currency:N0}</b> currency" +
                (Progression.Valor + Progression.Glory > 0 ? $"  <color=#7ab8ff>{Progression.Valor}</color> <color=#c08aff>{Progression.Glory}</color>" : ""), label);
            string sub = gm.Phase == Phase.Combat ? $"+{gm.RoundCurrency:N0} this round" : "B: skill tree";
            GUI.Label(new Rect(r.x + S(14), r.y + S(36), r.width, S(26)), $"<color=#bbb>{sub}</color>", small);
        }

        readonly SkillWeb web = new SkillWeb();

        /// <summary>The skill web, as Orc Horde draws it: see <see cref="SkillWeb"/>.</summary>
        void DrawShop(GameManager gm) => web.Draw(scale);


        Vector2 mapScroll;

        /// <summary>The campaign in order, locked past the furthest map held, then the test grounds.</summary>
        void DrawMapMenu(GameManager gm, GUIStyle btn)
        {
            var catalog = Session.Catalog;
            var r = new Rect(Screen.width / 2f - S(330), S(60), S(660), Screen.height - S(120));
            Box(r, Panel);
            GUI.Label(new Rect(r.x, r.y + S(8), r.width, S(40)), "Choose map", banner);

            var others = System.Array.FindAll(catalog.maps, m => catalog.CampaignIndex(m.id) < 0);
            int count = catalog.campaign.Length + others.Length;
            float rowH = S(62);
            var view = new Rect(r.x + S(16), r.y + S(56), r.width - S(32), r.height - S(120));
            mapScroll = GUI.BeginScrollView(view, mapScroll, new Rect(0, 0, view.width - S(20), rowH * count));
            for (int i = 0; i < count; i++)
            {
                var map = i < catalog.campaign.Length ? catalog.Get(catalog.campaign[i]) : others[i - catalog.campaign.Length];
                bool open = Session.IsAvailable(map.id);
                string label = i < catalog.campaign.Length ? $"{i + 1}. {map.name}" : $"Test · {map.name}";
                string detail = !open ? "<color=#888>Locked: hold the map before it</color>"
                    : $"<color=#bbb>{map.description}</color>";
                var row = new Rect(0, i * rowH, view.width - S(20), rowH - S(6));
                GUI.enabled = open;
                if (GUI.Button(new Rect(row.x, row.y, S(200), row.height), label, btn)) Session.Play(map.id);
                GUI.enabled = true;
                GUI.Label(new Rect(row.x + S(210), row.y, row.width - S(210), row.height), detail, small);
            }
            GUI.EndScrollView();

            if (GUI.Button(new Rect(r.x + S(16), r.yMax - S(54), S(200), S(40)), "Back", btn)) gm.MapMenuOpen = false;
            // A shortcut for testing: only in development builds, never in one players get.
            if (Debug.isDebugBuild && GUI.Button(new Rect(r.xMax - S(256), r.yMax - S(54), S(240), S(40)), "Unlock all (dev)", btn))
                Session.Unlocked = catalog.campaign.Length;
        }
    }
}
