using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>
    /// Bridges and tunnels, after Orc Horde's MapLinkDefinition: a one-way span
    /// the horde crosses on its own layer. A bridge carries it over another lane,
    /// in the open; a tunnel takes it under rock, out of sight and out of reach.
    ///
    /// Each is a navmesh link from the entry face to the exit face. Orcs cross it
    /// under their own control (<see cref="Enemy"/>), so they rise onto the deck
    /// or vanish into the rock rather than sliding through the ground.
    /// </summary>
    public static class Links
    {
        public const float DeckHeight = 3f;
        /// <summary>The underside of a tunnel's roof: clear of the tallest boss.</summary>
        public const float TunnelRoof = 5f;
        /// <summary>How far past each face the link's ends reach for the floor.</summary>
        const float FaceReach = 3f;

        public sealed class Info
        {
            public MapLink Data;
            public Vector3 Entry, Exit, Direction;
            /// <summary>Half the link's usable width: how far across it an orc may keep its place.</summary>
            public float HalfWidth;
            public bool Tunnel => Data.IsTunnel;
        }

        public static readonly List<Info> All = new List<Info>();
        static readonly List<NavMeshLinkInstance> instances = new List<NavMeshLinkInstance>();

        /// <summary>The span as a rect on the XZ plane, in metres.</summary>
        public static Rect SpanRect(MapData map, MapLink l)
        {
            var a = map.World(l.minX, l.maxY);
            var b = map.World(l.maxX, l.minY);
            return Rect.MinMaxRect(a.x, a.z, b.x, b.z);
        }

        /// <summary>Which way the horde travels across the span.</summary>
        public static Vector3 Direction(MapLink l) => l.direction switch
        {
            "north" => Vector3.forward,
            "south" => Vector3.back,
            "east" => Vector3.right,
            _ => Vector3.left,
        };

        public static void BuildVisual(MapData map, MapLink link, Transform root)
        {
            var rect = SpanRect(map, link);
            var dir = Direction(link);
            bool alongZ = dir.z != 0;
            float length = alongZ ? rect.height : rect.width;
            float width = alongZ ? rect.width : rect.height;
            var center = new Vector3(rect.center.x, 0, rect.center.y);
            var go = new GameObject(link.IsTunnel ? "Tunnel" : "Bridge").transform;
            go.SetParent(root, false);
            go.position = center;
            go.rotation = Quaternion.LookRotation(dir);

            if (link.IsTunnel)
            {
                // A bore through the rock, open at both ends, built with the rock itself (MapBuilder):
                // the columns either side are its walls, and the columns over it, broken off underneath
                // high enough for the biggest boss, are its roof. Light shows through from the far end,
                // and so does whatever is walking through.
                return;
            }

            var deck = Mats.Textured("wood", Color.white, new Vector2(3, 6));
            var rail = Mats.Textured("metal", U.Hex(0x6a717a), new Vector2(1, 8), 0.45f, 0.5f);
            var glow = Mats.Lit(U.Hex(0xffb347), 1.5f);
            // The deck carries a collider on the default layer: the player can stand on it, the navmesh ignores it.
            Shapes.Prim(PrimitiveType.Cube, go, new Vector3(0, DeckHeight - 0.2f, 0), new Vector3(width, 0.4f, length), deck, true);
            foreach (var side in new[] { -1f, 1f })
            {
                Shapes.Prim(PrimitiveType.Cube, go, new Vector3(side * (width / 2 - 0.1f), DeckHeight + 0.5f, 0), new Vector3(0.15f, 1f, length), rail, true);
                Shapes.Prim(PrimitiveType.Cube, go, new Vector3(side * (width / 2 - 0.1f), DeckHeight + 1.02f, 0), new Vector3(0.18f, 0.06f, length), glow);
            }
            // Ramps down to the path at each face, drawn only: the crossing itself is animated.
            foreach (var end in new[] { -1f, 1f })
            {
                var ramp = Shapes.Prim(PrimitiveType.Cube, go, new Vector3(0, DeckHeight / 2 - 0.2f, end * (length / 2 + 1.4f)),
                    new Vector3(width * 0.95f, 0.3f, 3.6f), deck);
                ramp.transform.localRotation = Quaternion.Euler(end * 40f, 0, 0);
            }
        }

        /// <summary>Adds each link to the navmesh. Runs after the bake, since the ends must find the floor.</summary>
        public static void Connect(MapData map)
        {
            All.Clear();
            foreach (var link in map.links ?? new MapLink[0])
            {
                var rect = SpanRect(map, link);
                var dir = Direction(link);
                var center = new Vector3(rect.center.x, 0, rect.center.y);
                float half = (dir.z != 0 ? rect.height : rect.width) / 2;
                float width = dir.z != 0 ? rect.width : rect.height;

                if (!Snap(center - dir * half, -dir, out var entry) || !Snap(center + dir * half, dir, out var exit))
                {
                    Debug.LogWarning($"[Links] {link.id}: no floor beside the span on {map.id}");
                    continue;
                }

                var data = new NavMeshLinkData
                {
                    startPosition = entry,
                    endPosition = exit,
                    width = width * 0.6f,
                    bidirectional = false,
                    costModifier = -1,
                    area = 0,
                    agentTypeID = 0,
                };
                var instance = NavMesh.AddLink(data);
                if (!NavMesh.IsLinkValid(instance)) { Debug.LogWarning($"[Links] {link.id}: navmesh refused the link"); continue; }
                instances.Add(instance);
                All.Add(new Info { Data = link, Entry = entry, Exit = exit, Direction = dir, HalfWidth = data.width / 2 });
            }
        }

        /// <summary>The nearest floor just outside a face, stepping outward until there is some.</summary>
        static bool Snap(Vector3 face, Vector3 outward, out Vector3 point)
        {
            for (float d = 0.6f; d <= FaceReach; d += 0.6f)
                if (NavMesh.SamplePosition(face + outward * d, out var hit, 0.8f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            point = default;
            return false;
        }

        public static Info Nearest(Vector3 p)
        {
            Info best = null;
            float bestDist = float.MaxValue;
            foreach (var l in All)
            {
                float d = (l.Entry - p).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = l; }
            }
            return best;
        }

        public static void Clear()
        {
            foreach (var i in instances) NavMesh.RemoveLink(i);
            instances.Clear();
            All.Clear();
        }
    }
}
