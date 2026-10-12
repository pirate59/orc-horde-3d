using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The skill web, after Orc Horde's SkillTreeView: one web grown from Gun
    /// Nests, drawn at its authored positions, panned with a drag and zoomed with
    /// the wheel, beside a detail panel that never moves.
    ///
    /// Only what the campaign has reached is drawn. Owned and open nodes carry
    /// their names; a node one step further out is a locked outline, so there is
    /// visibly more without saying what; anything beyond is not drawn. Each node
    /// is outlined in the colour of what it costs: gold currency, Valor blue or
    /// Glory violet.
    /// </summary>
    public sealed class SkillWeb
    {
        static readonly Color Gold = U.Hex(0xffc94d), ValorBlue = U.Hex(0x7ab8ff), GloryViolet = U.Hex(0xc08aff);
        static readonly Color Mint = U.Hex(0x5effc4), Panel = new Color(0.05f, 0.06f, 0.08f, 0.9f);
        const float NodeSize = 46f, MinZoom = 0.35f, MaxZoom = 1.8f, DragThreshold = 6f;
        const int Bulk = 5;

        Vector2 pan;
        float zoom = 1f;
        bool placed, dragging;
        Vector2 pressAt;
        string selected = "machine_gun_capacity";
        float scale;
        GUIStyle label, small, title, nodeText, nameText, button;

        public static Color CurrencyColor(string currency) =>
            currency == "valor" ? ValorBlue : currency == "glory" ? GloryViolet : Gold;

        static string CurrencyName(string currency, int amount) =>
            currency == "valor" ? "Valor" : currency == "glory" ? "Glory" : amount == 1 ? "currency" : "currency";

        float S(float v) => v * scale;

        void Styles(float s)
        {
            if (label != null && Mathf.Approximately(scale, s)) return;
            scale = s;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17 * s), richText = true, wordWrap = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(14 * s) };
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold, wordWrap = false };
            nodeText = new GUIStyle(label) { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            nameText = new GUIStyle(label) { fontSize = Mathf.RoundToInt(12 * s), alignment = TextAnchor.UpperCenter, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(15 * s), richText = true };
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Frame(Rect r, Color c, float w)
        {
            Box(new Rect(r.x, r.y, r.width, w), c);
            Box(new Rect(r.x, r.yMax - w, r.width, w), c);
            Box(new Rect(r.x, r.y, w, r.height), c);
            Box(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>A straight line, as a thin rotated quad: IMGUI has no line of its own.</summary>
        static void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1f) return;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Box(new Rect(a.x, a.y - width / 2, d.magnitude, width), c);
            GUI.matrix = matrix;
        }

        /// <summary>The whole screen: the web on the left, the selected node on the right.</summary>
        public void Draw(float uiScale)
        {
            Styles(uiScale);
            Box(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.04f, 1f));
            float panelW = S(380);
            var view = new Rect(0, S(70), Screen.width - panelW, Screen.height - S(70));
            var panel = new Rect(Screen.width - panelW, 0, panelW, Screen.height);
            if (!placed) { placed = true; CentreOn(Progression.NodeById(selected), view); }

            Header();
            Input(view);
            Web(view);
            Tools(view);
            Detail(panel);
        }

        void Header()
        {
            GUI.Label(new Rect(S(24), S(14), S(400), S(40)), "Skill web", title);
            float x = S(230);
            foreach (var (currency, amount) in new[] { ("currency", Progression.Currency), ("valor", Progression.Valor), ("glory", Progression.Glory) })
            {
                var c = CurrencyColor(currency);
                Box(new Rect(x, S(24), S(14), S(14)), c);
                GUI.Label(new Rect(x + S(20), S(17), S(220), S(30)), $"<b>{amount:N0}</b> <color=#bbb>{(currency == "currency" ? "currency" : currency == "valor" ? "Valor" : "Glory")}</color>", label);
                x += currency == "currency" ? S(210) : S(130);
            }

        }

        Vector2 ToScreen(SkillNode n, Rect view) => view.position + pan + new Vector2(n.x, n.y) * zoom * scale;

        void CentreOn(SkillNode n, Rect view)
        {
            if (n == null) return;
            pan = view.size / 2 - new Vector2(n.x, n.y) * zoom * scale;
        }

        void ZoomAround(Vector2 screen, float to, Rect view)
        {
            to = Mathf.Clamp(to, MinZoom, MaxZoom);
            var world = (screen - view.position - pan) / (zoom * scale);
            zoom = to;
            pan = screen - view.position - world * zoom * scale;
        }

        void Input(Rect view)
        {
            var e = Event.current;
            if (!view.Contains(e.mousePosition) && !dragging) return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    pressAt = e.mousePosition;
                    dragging = false;
                    break;
                case EventType.MouseDrag when e.button == 0:
                    if (dragging || (e.mousePosition - pressAt).magnitude > DragThreshold) dragging = true;
                    if (dragging) { pan += e.delta; e.Use(); }
                    break;
                case EventType.MouseUp when e.button == 0:
                    if (!dragging)
                    {
                        var hit = NodeAt(e.mousePosition, view);
                        if (hit != null) { selected = hit.id; Sfx.UI("ui_click", 0.6f); e.Use(); }
                    }
                    dragging = false;
                    break;
                case EventType.ScrollWheel:
                    ZoomAround(e.mousePosition, zoom * (e.delta.y > 0 ? 0.9f : 1.1f), view);
                    e.Use();
                    break;
            }
        }

        SkillNode NodeAt(Vector2 p, Rect view)
        {
            float half = NodeSize * zoom * scale / 2;
            foreach (var n in Progression.Tree.nodes)
            {
                if (Progression.VisibilityOf(n) == Progression.Visibility.Hidden) continue;
                var c = ToScreen(n, view);
                if (Mathf.Abs(p.x - c.x) <= half && Mathf.Abs(p.y - c.y) <= half) return n;
            }
            return null;
        }

        void Web(Rect view)
        {
            GUI.BeginClip(view);
            var local = new Rect(0, 0, view.width, view.height);
            var nodes = Progression.Tree.nodes;
            var vis = new Dictionary<string, Progression.Visibility>();
            foreach (var n in nodes) vis[n.id] = Progression.VisibilityOf(n);
            Vector2 At(SkillNode n) => ToScreen(n, local);

            // Links first, under the nodes: lit in the child's currency once the requirement is met.
            foreach (var n in nodes)
            {
                if (vis[n.id] == Progression.Visibility.Hidden) continue;
                foreach (var p in n.prerequisites)
                {
                    var from = Progression.NodeById(p.nodeId);
                    if (from == null || vis[from.id] == Progression.Visibility.Hidden) continue;
                    bool met = Progression.Level(from.id) >= p.minimumLevel;
                    var c = vis[n.id] == Progression.Visibility.Hinted ? new Color(1, 1, 1, 0.12f)
                        : met ? CurrencyColor(n.currency) * new Color(1, 1, 1, 0.75f) : new Color(1, 1, 1, 0.25f);
                    Line(At(from), At(n), c, S(met ? 3f : 2f) * Mathf.Max(0.6f, zoom));
                }
                // Blueprint shortcuts: drawn once the Blueprint is owned, dashed by being thin and pale.
                foreach (var p in n.alternatives)
                {
                    var from = Progression.NodeById(p.nodeId);
                    if (from == null || Progression.Level(from.id) == 0 || vis[n.id] == Progression.Visibility.Hinted) continue;
                    Line(At(from), At(n), new Color(1, 1, 1, 0.35f), S(1.5f));
                }
            }

            float size = NodeSize * zoom * scale;
            foreach (var n in nodes)
            {
                var v = vis[n.id];
                if (v == Progression.Visibility.Hidden) continue;
                var c = At(n);
                if (c.x < -size || c.y < -size || c.x > local.width + size || c.y > local.height + size) continue;
                DrawNode(n, v, new Rect(c.x - size / 2, c.y - size / 2, size, size));
            }
            GUI.EndClip();
        }

        void DrawNode(SkillNode n, Progression.Visibility v, Rect r)
        {
            var tint = CurrencyColor(n.currency);
            bool chosen = n.id == selected;
            if (chosen) Box(new Rect(r.x - S(5), r.y - S(5), r.width + S(10), r.height + S(10)), new Color(1, 1, 1, 0.25f));

            if (v == Progression.Visibility.Hinted)
            {
                // A locked outline: something is there, not what.
                Box(r, new Color(0.08f, 0.09f, 0.11f, 0.9f));
                Frame(r, tint * new Color(1, 1, 1, 0.35f), S(2));
                if (zoom > 0.5f) GUI.Label(r, "<color=#666>?</color>", nodeText);
                return;
            }

            int level = Progression.Level(n.id);
            bool supported = Progression.IsSupported(n);
            bool maxed = level >= n.levels.Length;
            bool affordable = Progression.CanBuy(n);
            var fill = !supported ? new Color(0.12f, 0.12f, 0.13f, 0.95f)
                : maxed ? tint * new Color(0.55f, 0.55f, 0.55f, 1f)
                : level > 0 ? tint * new Color(0.3f, 0.3f, 0.3f, 1f)
                : new Color(0.1f, 0.11f, 0.14f, 0.95f);
            Box(r, fill);
            float pulse = affordable ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
            Frame(r, !supported ? new Color(0.4f, 0.4f, 0.4f) : affordable ? tint * pulse : tint * new Color(1, 1, 1, v == Progression.Visibility.Open && level == 0 ? 0.55f : 0.9f), S(affordable ? 3 : 2));

            // Its level as pips, or a bar for a long ladder like a Mastery's forty.
            if (n.levels.Length <= 6)
            {
                float pip = r.width / 9f;
                float startX = r.center.x - (n.levels.Length * pip * 1.4f - pip * 0.4f) / 2;
                for (int i = 0; i < n.levels.Length; i++)
                    Box(new Rect(startX + i * pip * 1.4f, r.yMax - pip * 1.8f, pip, pip), i < level ? Color.white : new Color(1, 1, 1, 0.2f));
            }
            else
            {
                var bar = new Rect(r.x + r.width * 0.15f, r.yMax - r.height * 0.18f, r.width * 0.7f, r.height * 0.07f);
                Box(bar, new Color(1, 1, 1, 0.2f));
                Box(new Rect(bar.x, bar.y, bar.width * level / n.levels.Length, bar.height), Color.white);
            }
            if (zoom > 0.45f)
                GUI.Label(new Rect(r.x, r.y, r.width, r.height * 0.75f),
                    !supported ? "<color=#777>—</color>" : maxed ? "<b>★</b>" : $"<b>{level}</b>", nodeText);
            if (zoom > 0.6f)
                GUI.Label(new Rect(r.center.x - S(70), r.yMax + S(3), S(140), S(36)),
                    $"<color={(supported ? "#ddd" : "#777")}>{n.name}</color>", nameText);
        }

        void Tools(Rect view)
        {
            float b = S(40), x = view.xMax - b * 3 - S(30), y = view.yMax - b - S(20);
            if (GUI.Button(new Rect(x, y, b, b), "+", button)) ZoomAround(view.center, zoom * 1.25f, view);
            if (GUI.Button(new Rect(x + b + S(5), y, b, b), "−", button)) ZoomAround(view.center, zoom * 0.8f, view);
            if (GUI.Button(new Rect(x + (b + S(5)) * 2, y, b, b), "◎", button)) CentreOn(Progression.NodeById(selected), view);
            GUI.Label(new Rect(view.x + S(24), view.yMax - S(46), S(600), S(30)),
                "<color=#888>Drag to pan · wheel to zoom · click a node · B closes</color>", small);
        }

        void Detail(Rect r)
        {
            Box(r, Panel);
            var n = Progression.NodeById(selected);
            float x = r.x + S(22), w = r.width - S(44), y = r.y + S(24);
            if (n == null) return;
            var v = Progression.VisibilityOf(n);
            var tint = CurrencyColor(n.currency);

            if (v == Progression.Visibility.Hinted || v == Progression.Visibility.Hidden)
            {
                GUI.Label(new Rect(x, y, w, S(36)), "<b>Undiscovered</b>", title);
                GUI.Label(new Rect(x, y + S(46), w, S(80)), $"<color=#aaa>Needs {Requirements(n)}.</color>", label);
                Loadout(r);
                return;
            }

            int level = Progression.Level(n.id);
            var next = Progression.NextLevel(n);
            GUI.Label(new Rect(x, y, w, S(36)), $"<b>{n.name}</b>", title);
            y += S(40);
            GUI.Label(new Rect(x, y, w, S(26)), $"<color=#{ColorUtility.ToHtmlStringRGB(tint)}>■</color> <color=#bbb>Level {level}/{n.levels.Length}</color>", small);
            y += S(30);
            float h = label.CalcHeight(new GUIContent(n.description), w);
            GUI.Label(new Rect(x, y, w, h), $"<color=#ccc>{n.description}</color>", label);
            y += h + S(14);

            if (level > 0)
            {
                var now = n.levels[level - 1];
                GUI.Label(new Rect(x, y, w, S(22)), "<color=#888>NOW</color>", small);
                y += S(20);
                h = small.CalcHeight(new GUIContent(now.description), w);
                GUI.Label(new Rect(x, y, w, h), $"<b>{now.label}</b> · {now.description}", small);
                y += h + S(10);
            }

            if (!Progression.IsSupported(n))
            {
                GUI.Label(new Rect(x, y, w, S(60)), "<color=#ff9a7a>Not in the FPS yet: this part of Orc Horde hasn't been brought over. It can't be bought for now.</color>", small);
                y += S(64);
            }
            else if (next != null)
            {
                GUI.Label(new Rect(x, y, w, S(22)), "<color=#888>NEXT</color>", small);
                y += S(20);
                h = small.CalcHeight(new GUIContent(next.description), w);
                GUI.Label(new Rect(x, y, w, h), $"<b>{next.label}</b> · {next.description}", small);
                y += h + S(12);
                if (!Progression.PrerequisitesMet(n))
                {
                    GUI.Label(new Rect(x, y, w, S(44)), $"<color=#ff9a7a>Needs {Requirements(n)}.</color>", small);
                    y += S(46);
                }
                int balance = Progression.Balance(n.currency);
                GUI.enabled = Progression.CanBuy(n);
                if (GUI.Button(new Rect(x, y, w * 0.62f, S(46)), $"Buy · {next.cost:N0} {CurrencyName(n.currency, next.cost)}", button))
                    Sfx.UI(Progression.Buy(n) ? "ui_buy" : "ui_invalid");
                int left = n.levels.Length - level;
                if (left > 1)
                {
                    int levels = Mathf.Min(Bulk, left), cost = 0;
                    for (int i = 0; i < levels; i++) cost += n.levels[level + i].cost;
                    if (GUI.Button(new Rect(x + w * 0.65f, y, w * 0.35f, S(46)), $"×{levels} · {cost:N0}", button))
                        Sfx.UI(Progression.Buy(n, levels) > 0 ? "ui_buy" : "ui_invalid");
                }
                GUI.enabled = true;
                y += S(54);
                if (balance < next.cost)
                    GUI.Label(new Rect(x, y, w, S(26)), $"<color=#888>{next.cost - balance:N0} more {CurrencyName(n.currency, 2)} needed</color>", small);
                y += S(30);
            }
            else
            {
                GUI.Label(new Rect(x, y, w, S(26)), $"<color=#{ColorUtility.ToHtmlStringRGB(Mint)}>Complete</color>", label);
                y += S(32);
            }

            // An ability's unlock node offers to carry it straight away.
            var ability = UnlockedAbility(n);
            if (ability != null && Progression.AbilityUnlocked(ability))
            {
                GUI.Label(new Rect(x, y, w, S(24)), "<color=#888>CARRY IT</color>", small);
                y += S(22);
                EquipButtons(new Rect(x, y, w, S(40)), ability);
            }
            Loadout(r);
        }

        static AbilityDef UnlockedAbility(SkillNode n)
        {
            foreach (var l in n.levels)
                foreach (var e in l.effects)
                    if (e.kind == "unlock_ability") return Abilities.ById(e.target);
            return null;
        }

        void EquipButtons(Rect r, AbilityDef ability)
        {
            var loadout = Progression.Loadout;
            for (int slot = 0; slot < 2; slot++)
            {
                bool held = loadout[slot] == ability;
                var b = new Rect(r.x + slot * (r.width / 2 + S(4)), r.y, r.width / 2 - S(4), r.height);
                GUI.enabled = !held;
                if (GUI.Button(b, held ? $"<b>On {(slot == 0 ? "Q" : "E")}</b>" : $"Put on {(slot == 0 ? "Q" : "E")}", button))
                {
                    Progression.Equip(slot, ability);
                    Sfx.UI("ui_click");
                }
                GUI.enabled = true;
            }
        }

        /// <summary>The two hands and every ability unlocked, along the panel's foot.</summary>
        void Loadout(Rect r)
        {
            var open = new List<AbilityDef>();
            foreach (var a in Abilities.All) if (Progression.AbilityUnlocked(a)) open.Add(a);
            float row = S(34), x = r.x + S(22), w = r.width - S(44);
            float y = r.yMax - S(40) - row * open.Count - S(30);
            GUI.Label(new Rect(x, y, w, S(24)), "<color=#888>LOADOUT · one ability in each hand</color>", small);
            y += S(28);
            var loadout = Progression.Loadout;
            foreach (var a in open)
            {
                Box(new Rect(x, y + S(4), S(10), S(10)), a.Color);
                GUI.Label(new Rect(x + S(18), y, w * 0.55f, row), a.Name, small);
                for (int slot = 0; slot < 2; slot++)
                {
                    bool held = loadout[slot] == a;
                    var b = new Rect(x + w - (2 - slot) * S(52), y, S(48), row - S(6));
                    if (GUI.Button(b, held ? $"<b><color=#5effc4>{(slot == 0 ? "Q" : "E")}</color></b>" : $"<color=#777>{(slot == 0 ? "Q" : "E")}</color>", button) && !held)
                    {
                        Progression.Equip(slot, a);
                        Sfx.UI("ui_click");
                    }
                }
                y += row;
            }
        }

        static string Requirements(SkillNode n)
        {
            var parts = new List<string>();
            foreach (var p in n.prerequisites)
                parts.Add($"{Progression.NodeById(p.nodeId)?.name ?? p.nodeId} {p.minimumLevel}");
            string chain = string.Join(" and ", parts);
            var alts = new List<string>();
            foreach (var p in n.alternatives) alts.Add(Progression.NodeById(p.nodeId)?.name ?? p.nodeId);
            return alts.Count > 0 ? $"{chain}, or {string.Join(" or ", alts)}" : chain;
        }
    }
}
