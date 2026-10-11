using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>
    /// A wooden barricade across the road. It carves itself out of the horde's
    /// navmesh, so orcs route around it; destroyed during a round, it is put
    /// back up at the start of every attempt.
    /// </summary>
    public sealed class Barricade : MonoBehaviour
    {
        public static readonly List<Barricade> All = new List<Barricade>();

        public WallDef Def { get; private set; }
        public float Health { get; private set; }
        /// <summary>Full health with the skill tree's reinforcement, taken when built and on every restore.</summary>
        public float MaxHealth { get; private set; }
        public bool Standing { get; private set; } = true;

        BoxCollider box;
        NavMeshObstacle obstacle;
        GameObject intact, rubble;

        public static Barricade Create(WallDef def, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(def.Name);
            go.transform.SetPositionAndRotation(position, rotation);
            var b = go.AddComponent<Barricade>();
            b.Def = def;
            b.MaxHealth = b.Health = Progression.WallHealth;

            b.intact = BuildVisual(def, go.transform);
            b.rubble = new GameObject("Rubble");
            b.rubble.transform.SetParent(go.transform, false);
            var wood = Mats.Lit(def.Color * 0.6f);
            for (int i = 0; i < 5; i++)
            {
                var plank = Shapes.Prim(PrimitiveType.Cube, b.rubble.transform,
                    new Vector3(Random.Range(-1.6f, 1.6f), 0.08f, Random.Range(-0.6f, 0.6f)),
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
            b.obstacle.size = b.box.size;
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
            var wood = Mats.Lit(def.Color);
            var dark = Mats.Lit(def.Color * 0.7f);
            var iron = Mats.Lit(U.Hex(0x50555c));
            float half = def.Length / 2;
            foreach (var x in new[] { -half + 0.15f, 0f, half - 0.15f })
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
            Fx.Burst(transform.position + Vector3.up, 2.5f, Def.Color, 0.5f);
        }

        public void Restore()
        {
            Standing = true;
            MaxHealth = Health = Progression.WallHealth;
            intact.SetActive(true);
            rubble.SetActive(false);
            box.enabled = true;
            obstacle.enabled = true;
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
