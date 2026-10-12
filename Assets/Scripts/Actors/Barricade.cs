using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>
    /// A wooden barricade across the road: a run of hex cells along one of the
    /// grid's three axes, centred on its anchor cell. It carves itself out of the
    /// horde's navmesh, reaching a little past its end cells so it fuses with the
    /// rock or the next wall rather than leaving a gap; orcs route around it.
    /// Destroyed during a round, it is put back up at the start of every attempt.
    /// </summary>
    public sealed class Barricade : MonoBehaviour
    {
        public static readonly List<Barricade> All = new List<Barricade>();

        public WallDef Def { get; private set; }
        public float Health { get; private set; }
        /// <summary>Full health with the skill tree's reinforcement, taken when built and on every restore.</summary>
        public float MaxHealth { get; private set; }
        public bool Standing { get; private set; } = true;

        /// <summary>How far the navmesh cut reaches past the end cells, to close the seam against rock or another wall.</summary>
        const float FuseReach = 0.6f;

        public Vector2Int Anchor { get; private set; }
        public int Axis { get; private set; }
        public Vector2Int[] Cells { get; private set; }

        BoxCollider box;
        NavMeshObstacle obstacle;
        GameObject intact, rubble;

        /// <summary>The cells a barricade anchored here along this axis would cover.</summary>
        public static Vector2Int[] CellsFor(WallDef def, Vector2Int anchor, int axis)
        {
            var cells = new Vector2Int[def.Cells];
            for (int i = 0; i < def.Cells; i++) cells[i] = HexGrid.Step(anchor, axis, i - def.Cells / 2);
            return cells;
        }

        /// <summary>Where a barricade anchored here stands, and which way it faces: its length along the axis.</summary>
        public static (Vector3 position, Quaternion rotation) Pose(HexGrid grid, Vector2Int anchor, int axis)
        {
            var d = grid.AxisDirection(axis);
            return (Relief.RoadSpot(anchor), Quaternion.LookRotation(d) * Quaternion.Euler(0, -90, 0));
        }

        public static Barricade Create(WallDef def, HexGrid grid, Vector2Int anchor, int axis)
        {
            var go = new GameObject(def.Name);
            var (position, rotation) = Pose(grid, anchor, axis);
            go.transform.SetPositionAndRotation(position, rotation);
            var b = go.AddComponent<Barricade>();
            b.Def = def;
            b.Anchor = anchor;
            b.Axis = axis;
            b.Cells = CellsFor(def, anchor, axis);
            b.MaxHealth = b.Health = Progression.WallHealth;

            b.intact = BuildVisual(def, go.transform);
            b.rubble = new GameObject("Rubble");
            b.rubble.transform.SetParent(go.transform, false);
            var wood = Mats.Textured("wood", new Color(0.6f, 0.55f, 0.5f));
            for (int i = 0; i < 5; i++)
            {
                var plank = Shapes.Prim(PrimitiveType.Cube, b.rubble.transform,
                    new Vector3(Random.Range(-def.Length * 0.4f, def.Length * 0.4f), 0.08f, Random.Range(-0.6f, 0.6f)),
                    new Vector3(Random.Range(0.6f, 1.4f), 0.12f, 0.25f), wood);
                plank.transform.localRotation = Quaternion.Euler(0, Random.Range(0, 180f), Random.Range(-8f, 8f));
            }
            b.rubble.SetActive(false);

            b.box = go.AddComponent<BoxCollider>();
            b.box.center = new Vector3(0, def.Height / 2, 0);
            b.box.size = new Vector3(def.Length, def.Height, def.Thickness);

            b.obstacle = go.AddComponent<NavMeshObstacle>();
            b.obstacle.shape = NavMeshObstacleShape.Box;
            b.obstacle.center = b.box.center;
            b.obstacle.size = b.box.size + new Vector3(FuseReach * 2, 0, 0);
            b.obstacle.carving = true;

            Layers.SetRecursive(go, Layers.Wall);
            All.Add(b);
            return b;
        }

        /// <summary>The barricade's look on its own: shared with the placement ghost.</summary>
        public static GameObject BuildVisual(WallDef def, Transform parent)
        {
            var root = new GameObject("Intact");
            root.transform.SetParent(parent, false);
            // Long boards: the plank texture repeats along them rather than stretching across seven metres.
            var wood = Mats.Textured("wood", Color.white, new Vector2(3, 1));
            var dark = Mats.Textured("wood", new Color(0.7f, 0.65f, 0.6f), new Vector2(0.5f, 2));
            var iron = Mats.Textured("metal", U.Hex(0x707780), new Vector2(6, 1), 0.4f, 0.5f);
            float half = def.Length / 2;
            foreach (var x in new[] { -half + 0.15f, -half / 3, half / 3, half - 0.15f })
                Shapes.Prim(PrimitiveType.Cube, root.transform, new Vector3(x, def.Height / 2 + 0.1f, 0),
                    new Vector3(0.22f, def.Height + 0.2f, def.Thickness * 0.9f), dark);
            for (int i = 0; i < 4; i++)
                Shapes.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.25f + i * 0.38f, 0),
                    new Vector3(def.Length, 0.32f, def.Thickness * 0.55f), i % 2 == 0 ? wood : dark);
            Shapes.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, def.Height * 0.55f, -def.Thickness * 0.3f),
                new Vector3(def.Length * 0.95f, 0.08f, 0.06f), iron);
            return root;
        }

        public Vector3 ClosestPoint(Vector3 p) => box.ClosestPoint(p);

        /// <summary>Whether any standing-or-fallen barricade already covers this cell.</summary>
        public static bool Covers(Vector2Int cell)
        {
            foreach (var b in All)
                foreach (var c in b.Cells)
                    if (c == cell) return true;
            return false;
        }

        public void Damage(float amount)
        {
            if (!Standing) return;
            Health -= amount;
            if (Health <= 0f) Fall();
        }

        void Fall()
        {
            Standing = false;
            Health = 0f;
            intact.SetActive(false);
            rubble.SetActive(true);
            box.enabled = false;
            obstacle.enabled = false;
            FlowField.Rebuild();
            Fx.Burst(transform.position + Vector3.up, 2.5f, Def.Color, 0.5f);
            Sfx.At("wall_break", transform.position);
        }

        /// <summary>Emergency Repairs: gives back a share of its health, up to full.</summary>
        public void Repair(float fraction)
        {
            if (!Standing) return;
            Health = Mathf.Min(MaxHealth, Health + MaxHealth * fraction);
        }

        public void Restore()
        {
            Standing = true;
            MaxHealth = Health = Progression.WallHealth;
            intact.SetActive(true);
            rubble.SetActive(false);
            box.enabled = true;
            obstacle.enabled = true;
            FlowField.Rebuild();
        }

        public static Barricade Nearest(Vector3 from, float within)
        {
            Barricade best = null;
            float bestDist = within;
            foreach (var b in All)
            {
                if (!b.Standing) continue;
                var d = b.ClosestPoint(from) - from;
                d.y = 0;
                if (d.magnitude < bestDist) { bestDist = d.magnitude; best = b; }
            }
            return best;
        }

        void OnDestroy() => All.Remove(this);
    }
}
