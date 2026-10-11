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

        void OnGUI()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            EnsureStyles();

            DrawBossBars();
            DrawTopBar(gm);
            DrawPlayer();
            DrawAbilities();
            if (gm.Phase == Phase.Build) DrawBuildBar();
            DrawCrosshair();
            DrawBanner(gm);
            if (gm.HelpOpen && !gm.Paused) DrawHelp();
            if (gm.Paused) DrawPause(gm);
        }

        void DrawTopBar(GameManager gm)
        {
            var r = new Rect(Screen.width / 2f - S(320), S(12), S(640), S(70));
            Box(r, Panel);
            string phase = gm.Phase switch
            {
                Phase.Build => "<color=#5effc4>BUILD</color>",
                Phase.Combat => $"<color=#ffb347>FIGHT</color>  {Mathf.Max(0, gm.Round.Duration - gm.RoundTime):0}s",
                Phase.Won => "<color=#5effc4>ROUND WON</color>",
                Phase.Lost => "<color=#ff4a4a>ROUND LOST</color>",
                _ => "<color=#5effc4>MAP HELD</color>",
            };
            GUI.Label(new Rect(r.x + S(14), r.y + S(6), S(400), S(30)),
                $"<b>{gm.Map.Name}</b> · Round {gm.RoundIndex + 1}/{gm.Map.Rounds.Length}: {gm.Round.Name}", label);
            GUI.Label(new Rect(r.x + S(14), r.y + S(36), S(300), S(30)), phase, label);
            GUI.Label(new Rect(r.xMax - S(200), r.y + S(6), S(190), S(30)),
                $"Orcs: {Enemy.All.Count}   Kills: {gm.Kills}", label);

            GUI.Label(new Rect(r.xMax - S(200), r.y + S(36), S(60), S(30)), "Base", label);
            Bar(new Rect(r.xMax - S(145), r.y + S(40), S(130), S(20)), gm.BaseHealth / (float)gm.Round.StartingHealth,
                gm.BaseHealth > gm.Round.StartingHealth / 3 ? Mint : Red);
            GUI.Label(new Rect(r.xMax - S(145), r.y + S(37), S(130), S(26)),
                $"<color=#0d1014><b>{gm.BaseHealth}/{gm.Round.StartingHealth}</b></color>", center);

            if (gm.Phase == Phase.Combat)
                Bar(new Rect(r.x, r.yMax, r.width, S(5)), gm.SpawnProgress, Amber);
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
            float w = S(170), h = S(54);
            float x = Screen.width / 2f - w - S(6), y = Screen.height - h - S(20);
            DrawAbility(new Rect(x, y, w, h), "Q", ab.Left, ab);
            DrawAbility(new Rect(x + w + S(12), y, w, h), "E", ab.Right, ab);
        }

        void DrawAbility(Rect r, string key, AbilityDef a, PlayerAbilities ab)
        {
            Box(r, Panel);
            float cd = ab.CooldownLeft(a);
            if (cd > 0) Box(new Rect(r.x, r.y, r.width * cd / a.Cooldown, r.height), new Color(1, 1, 1, 0.08f));
            string state = !ab.Usable ? "<color=#888>in rounds only</color>"
                : cd > 0 ? $"<color=#ffb347>{cd:0.0}s</color>" : "<color=#5effc4>ready</color>";
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
                string count = isWall
                    ? $"{Barricade.All.Count}/{Walls.Barricade.Limit}"
                    : $"{BuildController.Placed(Towers.All[i])}/{Towers.All[i].Limit}";
                Box(r, selected ? new Color(0.37f, 1f, 0.77f, 0.35f) : Panel);
                Box(new Rect(r.x, r.y, r.width, S(4)), isWall ? Walls.Barricade.Color : Towers.All[i].Color);
                GUI.Label(new Rect(r.x + S(8), r.y + S(8), r.width, S(22)), $"<b>[{i + 1}]</b> {name}", small);
                GUI.Label(new Rect(r.x + S(8), r.y + S(34), r.width, S(22)), count, small);
            }

            string info = bc.Tool switch
            {
                BuildTool.Tower => $"<b>{bc.SelectedTower.Name}</b> — {bc.SelectedTower.Description}",
                BuildTool.Barricade => $"<b>Barricade</b> — {Walls.Barricade.Description}",
                _ => "Pick a tower [1–5] or barricade [6]. Aim at a placed one and press X to remove it. G starts the round.",
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
            var c = new Color(1, 1, 1, 0.85f);
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
                if (!always && e.Health >= e.Def.MaxHealth) continue;
                var p = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (e.Def.Height + 0.3f));
                if (p.z <= 0 || p.z > (always ? 80f : 25f)) continue;
                float w = always ? S(90) : S(36);
                var r = new Rect(p.x - w / 2, Screen.height - p.y, w, always ? S(9) : S(5));
                Bar(r, e.Health / e.Def.MaxHealth, always ? Red : Amber);
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
            var r = new Rect(S(20), S(100), S(340), S(400));
            Box(r, Panel);
            GUI.Label(new Rect(r.x + S(14), r.y + S(10), r.width - S(28), r.height - S(20)),
                "<b>ORC HORDE: FPS</b>\n" +
                "Hold the road. Orcs pour in from the west gate and march to the yard behind you.\n\n" +
                "<b>Move</b>  WASD · Shift sprint · Space jump\n" +
                "<b>Shoot</b>  Left mouse · R reload · headshots ×2\n" +
                "<b>Abilities</b>  hold Q / E to aim, release\n\n" +
                "<b>Between rounds</b>\n" +
                "1–5 towers · 6 barricade · R rotate\n" +
                "Left click place · X / right click remove\n" +
                "0 put away · <b>G start round</b>\n\n" +
                "Towers stand on the raised shoulders. Barricades go on the road and reroute the horde.\n\n" +
                "H hide help · Esc pause", small);
        }

        void DrawPause(GameManager gm)
        {
            Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            var r = new Rect(Screen.width / 2f - S(160), Screen.height / 2f - S(110), S(320), S(220));
            Box(r, Panel);
            GUI.Label(new Rect(r.x, r.y + S(10), r.width, S(40)), "Paused", banner);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * scale) };
            if (GUI.Button(new Rect(r.x + S(40), r.y + S(60), r.width - S(80), S(40)), "Resume", btn)) gm.SetPaused(false);
            if (GUI.Button(new Rect(r.x + S(40), r.y + S(110), r.width - S(80), S(40)), "Restart campaign", btn)) gm.RestartCampaign();
            if (GUI.Button(new Rect(r.x + S(40), r.y + S(160), r.width - S(80), S(40)), "Quit", btn))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
