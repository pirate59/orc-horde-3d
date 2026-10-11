using UnityEngine;

namespace OrcHordeFps
{
    public enum BuildTool { None, Tower, Barricade }

    /// <summary>
    /// Placing the line between rounds, as in Orc Horde: towers on the
    /// shoulders, barricades on the road, each within its deployment limit.
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        public static BuildController I { get; private set; }

        const float Reach = 40f;
        /// <summary>Keep barricades clear of the gate and the yard, so nothing spawns or breaches inside one.</summary>
        const float KeepClearOfEnds = 7f;
        const float RotateStep = 45f;

        public BuildTool Tool { get; private set; }
        public TowerDef SelectedTower { get; private set; }
        public bool GhostValid { get; private set; }
        public string Hint { get; private set; } = "";

        GameObject ghost;
        LineRenderer rangeRing;
        float wallYaw;
        Material okMat, badMat;

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

        void Select(BuildTool tool, TowerDef tower = null)
        {
            bool same = tool == Tool && tower == SelectedTower;
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
            rangeRing.enabled = false;
            if (gm.Phase != Phase.Build || !PlayerController.I.InputLive)
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
            if (Tool == BuildTool.Barricade && Input.GetKeyDown(KeyCode.R)) wallYaw = (wallYaw + RotateStep) % 180f;

            if (Tool == BuildTool.None)
            {
                Hint = "";
                if (Input.GetKeyDown(KeyCode.X)) TryRemove();
                return;
            }

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.X)) { TryRemove(); return; }

            var ray = PlayerController.I.AimRay;
            if (!Physics.Raycast(ray, out var hit, Reach, Layers.GroundMask | Layers.WallMask | Layers.TowerMask,
                    QueryTriggerInteraction.Ignore))
            {
                ghost.SetActive(false);
                Hint = "Aim at the ground";
                return;
            }

            ghost.SetActive(true);
            if (Tool == BuildTool.Tower) PreviewTower(hit);
            else PreviewBarricade(hit);
            Shapes.Tint(ghost, GhostValid ? okMat : badMat);

            if (GhostValid && Input.GetMouseButtonDown(0))
            {
                if (Tool == BuildTool.Tower) Tower.Create(SelectedTower, ghost.transform.position);
                else Barricade.Create(Walls.Barricade, ghost.transform.position, ghost.transform.rotation);
                Fx.Burst(ghost.transform.position + Vector3.up, 1.5f, U.Hex(0x5effc4), 0.3f);
            }
        }

        /// <summary>As in Orc Horde, a tower stands at the centre of a tower-ground cell: one per cell.</summary>
        void PreviewTower(RaycastHit hit)
        {
            var def = SelectedTower;
            var grid = GameManager.I.Grid;
            var cell = grid.CellAt(hit.point);
            var pos = grid.Center(cell) + Vector3.up * MapBuilder.TowerGroundHeight;
            ghost.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(PlayerController.I.transform.forward));

            rangeRing.enabled = true;
            Fx.SetCircle(rangeRing, pos + Vector3.up * 0.05f, def.Range, 64);

            if (Placed(def) >= def.Limit) { Invalid($"All {def.Limit} {def.Name}s are placed — X removes one"); return; }
            if (grid.Kind(cell) != HexGrid.TowerGround) { Invalid("Towers stand on the raised ground beside the path"); return; }
            foreach (var t in Tower.All)
                if ((t.transform.position - pos).sqrMagnitude < 0.25f) { Invalid("A tower already stands here"); return; }
            Valid($"Left click: place {def.Name}  ({Placed(def)}/{def.Limit})");
        }

        void PreviewBarricade(RaycastHit hit)
        {
            var def = Walls.Barricade;
            var pos = hit.point;
            pos.y = 0;
            var rot = Quaternion.Euler(0, wallYaw, 0);
            ghost.transform.SetPositionAndRotation(pos, rot);

            if (Barricade.All.Count >= def.Limit) { Invalid($"All {def.Limit} barricades are placed — X removes one"); return; }
            if (hit.collider.gameObject.layer != Layers.Road || GameManager.I.Grid.Kind(GameManager.I.Grid.CellAt(pos)) != HexGrid.WallPath)
            { Invalid("Barricades go on the path"); return; }
            var map = GameManager.I.Map;
            if (Vector3.Distance(pos, map.Base) < KeepClearOfEnds) { Invalid("Too close to the base"); return; }
            foreach (var s in map.spawns)
                if (Vector3.Distance(pos, map.World(s.x, s.y)) < KeepClearOfEnds) { Invalid("Too close to a gate"); return; }
            foreach (var link in Links.All)
                if (Vector3.Distance(pos, link.Entry) < KeepClearOfEnds * 0.6f || Vector3.Distance(pos, link.Exit) < KeepClearOfEnds * 0.6f)
                { Invalid("Keep the bridge and tunnel mouths clear"); return; }
            // Slightly inset, so a barricade may butt up against the rock or another wall.
            var half = new Vector3(def.Length / 2 - 0.1f, def.Height / 2, def.Thickness / 2 - 0.1f);
            if (Physics.CheckBox(pos + Vector3.up * (def.Height / 2 + 0.05f), half, rot,
                    Layers.WallMask | Layers.RockMask | Layers.TowerMask | (1 << Layers.BuildGround) | (1 << Layers.Player),
                    QueryTriggerInteraction.Ignore))
            { Invalid("Blocked — or you're standing in it"); return; }
            Valid($"Left click: place barricade  ({Barricade.All.Count}/{def.Limit})   R: rotate");
        }

        void Valid(string hint) { GhostValid = true; Hint = hint; }

        void Invalid(string hint) { GhostValid = false; Hint = hint; }

        void TryRemove()
        {
            if (!Physics.Raycast(PlayerController.I.AimRay, out var hit, Reach, Layers.TowerMask | Layers.WallMask | Layers.GroundMask,
                    QueryTriggerInteraction.Ignore))
                return;
            var tower = hit.collider.GetComponentInParent<Tower>();
            var wall = hit.collider.GetComponentInParent<Barricade>();
            if (tower != null) { Tower.All.Remove(tower); Destroy(tower.gameObject); }
            else if (wall != null) { Barricade.All.Remove(wall); Destroy(wall.gameObject); }
            else return;
            Fx.Burst(hit.point, 1f, U.Hex(0xff6a3a), 0.25f);
        }
    }
}
