using System;
using UnityEngine;

namespace OrcHordeFps
{
    public enum BuildTool { None, Tower, Barricade }

    /// <summary>
    /// Laying out the line between rounds, as in Orc Horde: towers on the
    /// tower ground, barricades on the path, each within its deployment limit.
    /// Works on foot (aim with the crosshair) or from the tactical view (aim
    /// with the mouse). Aim at something placed to inspect it; F picks it up to
    /// move it, X removes it.
    ///
    /// Every placement goes through <see cref="PlaceTower"/> or
    /// <see cref="PlaceBarricade"/>, which apply the same checks the ghost shows,
    /// so a restored layout or a test can never put down what a player couldn't.
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        public static BuildController I { get; private set; }

        const float Reach = 40f, TacticalReach = 400f;
        /// <summary>Keep barricades clear of the gate and the yard, so nothing spawns or breaches inside one.</summary>
        const float KeepClearOfEnds = 7f;

        public BuildTool Tool { get; private set; }
        public TowerDef SelectedTower { get; private set; }
        public bool GhostValid { get; private set; }
        public string Hint { get; private set; } = "";
        /// <summary>The placed tower or barricade under the aim, when no tool is held.</summary>
        public Component Inspected { get; private set; }

        /// <summary>Raised whenever something is placed, moved or removed.</summary>
        public static event Action LayoutChanged;

        GameObject ghost;
        LineRenderer rangeRing;
        /// <summary>Which of the grid's three axes the barricade in hand runs along; R turns it.</summary>
        int wallAxis;
        Material okMat, badMat;
        bool ringsShown;

        void Awake() => I = this;

        void Start()
        {
            okMat = Mats.Unlit(new Color(0.37f, 1f, 0.77f, 0.45f));
            badMat = Mats.Unlit(new Color(1f, 0.3f, 0.3f, 0.45f));
            var ring = new GameObject("Range ring");
            rangeRing = Fx.NewLine(ring.transform, new Color(0.37f, 1f, 0.77f, 0.6f), 0.06f);
            rangeRing.enabled = false;
        }

        public static int Placed(TowerDef def)
        {
            int n = 0;
            foreach (var t in Tower.All) if (t.Def == def) n++;
            return n;
        }

        /// <summary>Picks up a tool; picking up the one already held puts it away.</summary>
        void Select(BuildTool tool, TowerDef tower = null, bool toggle = true)
        {
            bool same = toggle && tool == Tool && tower == SelectedTower;
            Tool = same ? BuildTool.None : tool;
            SelectedTower = same ? null : tower;
            if (ghost != null) Destroy(ghost);
            ghost = null;
            if (Tool == BuildTool.None) return;

            ghost = new GameObject("Ghost");
            if (Tool == BuildTool.Tower) Tower.BuildVisual(tower, ghost.transform, out _, out _);
            else Barricade.BuildVisual(Walls.Barricade, ghost.transform);
        }

        void Update()
        {
            var gm = GameManager.I;
            var pc = PlayerController.I;
            rangeRing.enabled = false;
            Inspected = null;
            bool building = gm.Phase == Phase.Build && pc.InputLive;
            ShowRings(building && (Tool != BuildTool.None || pc.Tactical));
            if (!building)
            {
                if (gm.Phase != Phase.Build && Tool != BuildTool.None) Select(BuildTool.None);
                if (ghost != null) ghost.SetActive(false);
                Hint = "";
                return;
            }

            for (int i = 0; i < Towers.All.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Select(BuildTool.Tower, Towers.All[i]);
            if (Input.GetKeyDown(KeyCode.Alpha6)) Select(BuildTool.Barricade);
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.BackQuote)) Select(BuildTool.None);
            if (Tool == BuildTool.Barricade && Input.GetKeyDown(KeyCode.R)) wallAxis = (wallAxis + 1) % HexGrid.AxisSides.Length;

            var ray = pc.AimRay;
            float reach = pc.Tactical ? TacticalReach : Reach;

            if (Tool == BuildTool.None)
            {
                Hint = "";
                if (Physics.Raycast(ray, out var look, reach, Layers.TowerMask | Layers.WallMask | Layers.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    Inspected = (Component)look.collider.GetComponentInParent<Tower>() ?? look.collider.GetComponentInParent<Barricade>();
                    if (Inspected != null)
                    {
                        if (Input.GetKeyDown(KeyCode.X) || (pc.Tactical && Input.GetMouseButtonDown(1))) Remove(Inspected);
                        else if (Input.GetKeyDown(KeyCode.F)) PickUp(Inspected);
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.X))
            {
                if (Physics.Raycast(ray, out var target, reach, Layers.TowerMask | Layers.WallMask, QueryTriggerInteraction.Ignore))
                    Remove((Component)target.collider.GetComponentInParent<Tower>() ?? target.collider.GetComponentInParent<Barricade>());
                return;
            }

            if (!Physics.Raycast(ray, out var hit, reach, Layers.GroundMask | Layers.WallMask | Layers.TowerMask, QueryTriggerInteraction.Ignore))
            {
                ghost.SetActive(false);
                Hint = "Aim at the ground";
                return;
            }

            ghost.SetActive(true);
            var facing = pc.Tactical ? pc.Cam.transform.forward : pc.transform.forward;
            facing.y = 0;
            string problem;
            if (Tool == BuildTool.Tower)
            {
                var cell = gm.Grid.CellAt(hit.point);
                var pos = TowerPosition(cell);
                ghost.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward));
                rangeRing.enabled = true;
                Fx.SetCircle(rangeRing, pos + Vector3.up * 0.05f, Progression.Tower(SelectedTower).Range, 64);
                problem = TowerProblem(SelectedTower, cell);
                Report(problem, $"Left click: place {SelectedTower.Name}  ({Placed(SelectedTower)}/{Progression.Tower(SelectedTower).Limit})");
                if (problem == null && Input.GetMouseButtonDown(0)) PlaceTower(SelectedTower, cell);
            }
            else
            {
                var anchor = gm.Grid.CellAt(hit.point);
                var (pos, rot) = Barricade.Pose(gm.Grid, anchor, wallAxis);
                ghost.transform.SetPositionAndRotation(pos, rot);
                problem = BarricadeProblem(anchor, wallAxis, ignorePlayer: false);
                Report(problem, $"Left click: place barricade  ({Barricade.All.Count}/{Progression.WallLimit})   R: turn");
                if (problem == null && Input.GetMouseButtonDown(0)) PlaceBarricade(anchor, wallAxis);
            }
            Shapes.Tint(ghost, GhostValid ? okMat : badMat);
        }

        void Report(string problem, string ok)
        {
            GhostValid = problem == null;
            Hint = problem ?? ok;
        }

        public static Vector3 TowerPosition(Vector2Int cell) =>
            GameManager.I.Grid.Center(cell) + Vector3.up * MapBuilder.TowerGroundHeight;

        /// <summary>Why a tower can't stand on this cell, or null if it can. As in Orc Horde: one per tower-ground cell, at its centre.</summary>
        public static string TowerProblem(TowerDef def, Vector2Int cell)
        {
            var stats = Progression.Tower(def);
            if (!stats.Unlocked) return $"{def.Name} is locked — unlock it in the skill tree (B)";
            if (Placed(def) >= stats.Limit) return $"All {stats.Limit} {def.Name}s are placed — X removes one, B buys capacity";
            if (GameManager.I.Grid.Kind(cell) != HexGrid.TowerGround) return "Towers stand on the raised ground beside the path";
            var pos = TowerPosition(cell);
            foreach (var t in Tower.All)
                if ((t.transform.position - pos).sqrMagnitude < 0.25f) return "A tower already stands here";
            return null;
        }

        /// <summary>
        /// Why a barricade can't run along this axis from this anchor, or null if
        /// it can. As in Orc Horde: every cell it covers must be path a barricade
        /// may stand on, and no two barricades share a cell.
        /// </summary>
        public static string BarricadeProblem(Vector2Int anchor, int axis, bool ignorePlayer)
        {
            var def = Walls.Barricade;
            var gm = GameManager.I;
            var grid = gm.Grid;
            var map = gm.Map;
            if (Barricade.All.Count >= Progression.WallLimit) return $"All {Progression.WallLimit} barricades are placed — X removes one";
            foreach (var cell in Barricade.CellsFor(def, anchor, axis))
            {
                if (grid.Kind(cell) != HexGrid.WallPath) return "Every cell must be path — R turns it";
                if (Barricade.Covers(cell)) return "Another barricade is in the way";
                var c = grid.Center(cell);
                if (Vector3.Distance(c, map.Base) < KeepClearOfEnds) return "Too close to the base";
                foreach (var s in map.spawns)
                    if (Vector3.Distance(c, map.World(s.x, s.y)) < KeepClearOfEnds) return "Too close to a gate";
                foreach (var link in Links.All)
                    if (Vector3.Distance(c, link.Entry) < KeepClearOfEnds * 0.6f || Vector3.Distance(c, link.Exit) < KeepClearOfEnds * 0.6f)
                        return "Keep the bridge and tunnel mouths clear";
            }
            if (!ignorePlayer)
            {
                var (pos, rot) = Barricade.Pose(grid, anchor, axis);
                var half = new Vector3(def.Length / 2, def.Height / 2, def.Thickness / 2);
                if (Physics.CheckBox(pos + Vector3.up * (def.Height / 2 + 0.05f), half, rot, 1 << Layers.Player, QueryTriggerInteraction.Ignore))
                    return "You're standing in it";
            }
            return null;
        }

        public static Tower PlaceTower(TowerDef def, Vector2Int cell)
        {
            if (TowerProblem(def, cell) != null) return null;
            var t = Tower.Create(def, TowerPosition(cell));
            t.Cell = cell;
            Fx.Burst(t.transform.position + Vector3.up, 1.5f, U.Hex(0x5effc4), 0.3f);
            LayoutChanged?.Invoke();
            return t;
        }

        public static Barricade PlaceBarricade(Vector2Int anchor, int axis, bool ignorePlayer = false)
        {
            if (BarricadeProblem(anchor, axis, ignorePlayer) != null) return null;
            var b = Barricade.Create(Walls.Barricade, GameManager.I.Grid, anchor, axis);
            Fx.Burst(b.transform.position + Vector3.up, 1.5f, U.Hex(0x5effc4), 0.3f);
            LayoutChanged?.Invoke();
            return b;
        }

        public static void Remove(Component placed)
        {
            if (placed is Tower t) { Tower.All.Remove(t); Destroy(t.gameObject); }
            else if (placed is Barricade b) { Barricade.All.Remove(b); Destroy(b.gameObject); }
            else return;
            Fx.Burst(placed.transform.position + Vector3.up, 1f, U.Hex(0xff6a3a), 0.25f);
            LayoutChanged?.Invoke();
        }

        /// <summary>Lifts something placed back into the hand, to put down elsewhere.</summary>
        void PickUp(Component placed)
        {
            if (placed is Tower t)
            {
                var def = t.Def;
                Remove(t);
                Select(BuildTool.Tower, def, toggle: false);
            }
            else if (placed is Barricade b)
            {
                wallAxis = b.Axis;
                Remove(b);
                Select(BuildTool.Barricade, toggle: false);
            }
        }

        /// <summary>Every placed tower's reach, shown while building so gaps in the line are plain.</summary>
        void ShowRings(bool show)
        {
            if (show == ringsShown && !show) return;
            ringsShown = show;
            foreach (var t in Tower.All) t.ShowRange(show);
        }

        /// <summary>Clears every tower and barricade.</summary>
        public static void ClearLayout()
        {
            foreach (var t in Tower.All.ToArray()) { Tower.All.Remove(t); Destroy(t.gameObject); }
            foreach (var b in Barricade.All.ToArray()) { Barricade.All.Remove(b); Destroy(b.gameObject); }
            LayoutChanged?.Invoke();
        }
    }
}
