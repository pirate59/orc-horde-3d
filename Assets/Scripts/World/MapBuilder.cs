using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace OrcHordeFps
{
    /// <summary>
    /// Raises an exported Orc Horde map into hex columns.
    ///
    /// Path is open floor at ground level. Tower ground is a low step the player
    /// can walk onto and build on. Other rock is a cliff too tall to climb. Cells
    /// are merged into one mesh per kind per chunk, so a big map stays a few
    /// dozen draw calls.
    /// </summary>
    public static class MapBuilder
    {
        public const float TowerGroundHeight = 0.35f;
        const float CliffMin = 4.5f, CliffVariance = 3.5f;
        const int ChunkSize = 16;

        static readonly List<NavMeshDataInstance> navData = new List<NavMeshDataInstance>();

        public static void Build(MapData map, HexGrid grid, Transform root)
        {
            var floorColor = U.Hex((uint)map.floorColor);
            var rockColor = U.Hex((uint)map.rockColor);
            var groundColor = Color.Lerp(rockColor, U.Hex(0xa8946c), 0.55f);
            if (map.biome == "snow") groundColor = Color.Lerp(rockColor, U.Hex(0xdfe6ee), 0.6f);
            if (map.biome == "grassland") groundColor = Color.Lerp(rockColor, U.Hex(0x7d9a5a), 0.5f);

            // A backstop under everything, just below ground and on a layer the navmesh bake ignores.
            // The walkable floor is the path hexes alone: a slab at ground level would also run on under
            // every rock column, which is open underneath, and the bake would find floor in there.
            var floor = Shapes.Prim(PrimitiveType.Cube, root, new Vector3(map.Width / 2, -0.55f, map.Depth / 2),
                new Vector3(map.Width + 40, 0.5f, map.Depth + 40), Mats.Lit(floorColor * 0.8f), collider: true);
            floor.name = "Backstop";

            var terrain = Mats.VertexLit();
            var edgeMat = Mats.Lit(U.Hex(0x3ec9e0), 1.2f);
            var corners = grid.CornerOffsets();
            var links = map.links ?? new MapLink[0];

            for (int cx = 0; cx < grid.Columns; cx += ChunkSize)
            for (int cy = grid.FirstRow; cy <= grid.LastRow; cy += ChunkSize)
            {
                var rock = new MeshBatch();
                var ground = new MeshBatch();
                var path = new MeshBatch();
                var edges = new MeshBatch();

                for (int c = cx; c < Mathf.Min(cx + ChunkSize, grid.Columns); c++)
                for (int r = cy; r < Mathf.Min(cy + ChunkSize, grid.LastRow + 1); r++)
                {
                    char kind = grid.Kind(c, r);
                    var center = grid.Center(c, r);
                    float noise = Mathf.PerlinNoise(c * 0.31f + 7f, r * 0.29f + 3f);
                    float h = Height(map, grid, links, c, r);

                    if (HexGrid.IsPath(kind))
                    {
                        var tint = floorColor * (0.92f + noise * 0.16f);
                        tint.a = 1;
                        path.AddTop(center, corners, tint);
                        continue;
                    }

                    bool cliff = kind == HexGrid.Rock;
                    var top = cliff ? rockColor * (1.05f + noise * 0.25f) : groundColor * (0.9f + noise * 0.2f);
                    if (cliff && map.biome == "snow") top = Color.Lerp(top, Color.white, 0.55f);
                    top.a = 1;
                    var side = (cliff ? rockColor : groundColor) * (0.7f + noise * 0.15f);
                    side.a = 1;
                    var batch = cliff ? rock : ground;
                    batch.AddTop(center + Vector3.up * h, corners, top);

                    for (int s = 0; s < 6; s++)
                    {
                        var n = HexGrid.Across(c, r, s);
                        float nh = Height(map, grid, links, n.x, n.y);
                        if (nh >= h) continue;
                        batch.AddSide(center, corners[s], corners[(s + 1) % 6], nh, h, side);
                        if (!cliff && HexGrid.IsPath(grid.Kind(n)))
                            edges.AddStrip(center, corners[s], corners[(s + 1) % 6], h + 0.01f, 0.08f);
                    }
                }

                rock.Emit(root, "Rock", terrain, Layers.Rock, collider: true);
                ground.Emit(root, "Tower ground", terrain, Layers.BuildGround, collider: true);
                path.Emit(root, "Path", terrain, Layers.Road, collider: true);
                edges.Emit(root, "Build edge", edgeMat, -1, collider: false);
            }

            BuildBounds(map, root);
            foreach (var s in map.spawns) BuildGate(map, grid, root, s);
            BuildBase(map, grid, root);
            foreach (var link in links) Links.BuildVisual(map, link, root);
        }

        /// <summary>
        /// A cell's standing height. Rock inside a bridge's span is cut down to
        /// the deck, so the bridge rests on it; a tunnel's rock stays whole.
        /// </summary>
        static float Height(MapData map, HexGrid grid, MapLink[] links, int c, int r)
        {
            char kind = grid.Kind(c, r);
            if (HexGrid.IsPath(kind)) return 0f;
            if (kind == HexGrid.TowerGround) return TowerGroundHeight;
            var center = grid.Center(c, r);
            foreach (var link in links)
                if (!link.IsTunnel && Links.SpanRect(map, link).Contains(new Vector2(center.x, center.z)))
                    return Links.DeckHeight;
            return CliffMin + Mathf.PerlinNoise(c * 0.37f, r * 0.41f) * CliffVariance;
        }

        /// <summary>Invisible walls at the map's edge, where the path runs out into a gate or the yard.</summary>
        static void BuildBounds(MapData map, Transform root)
        {
            void Wall(Vector3 c, Vector3 s)
            {
                var go = new GameObject("Bounds");
                go.transform.SetParent(root, false);
                go.transform.position = c;
                go.AddComponent<BoxCollider>().size = s;
                go.layer = Layers.Rock;
            }

            Wall(new Vector3(-0.5f, 10, map.Depth / 2), new Vector3(1, 20, map.Depth));
            Wall(new Vector3(map.Width + 0.5f, 10, map.Depth / 2), new Vector3(1, 20, map.Depth));
            Wall(new Vector3(map.Width / 2, 10, -0.5f), new Vector3(map.Width, 20, 1));
            Wall(new Vector3(map.Width / 2, 10, map.Depth + 0.5f), new Vector3(map.Width, 20, 1));
        }

        /// <summary>
        /// The way the path runs out from a point, as the longest open run among
        /// sixteen headings. Bases sit on the rim on most maps but at the heart of
        /// some, so the nearest edge is no guide to which way the horde comes from.
        /// </summary>
        public static Vector3 OpenDirection(HexGrid grid, Vector3 from, float max = 30f)
        {
            var best = Vector3.forward;
            float bestRun = -1;
            for (int i = 0; i < 16; i++)
            {
                var dir = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
                float run = 0;
                while (run < max && HexGrid.IsPath(grid.Kind(grid.CellAt(from + dir * (run + 0.5f))))) run += 0.5f;
                if (run > bestRun) { bestRun = run; best = dir; }
            }
            return best;
        }

        /// <summary>A few strides in front of the base, facing up the path the horde comes down.</summary>
        public static (Vector3 position, float yaw) PlayerStart(MapData map, HexGrid grid)
        {
            var dir = OpenDirection(grid, map.Base);
            return (map.Base + dir * 7f, Quaternion.LookRotation(dir).eulerAngles.y);
        }

        /// <summary>How far open path runs either way along <paramref name="axis"/> from a point.</summary>
        static (float lo, float hi) OpenSpan(HexGrid grid, Vector3 from, Vector3 axis, float max = 14f)
        {
            float lo = 0, hi = 0;
            for (float d = 0.5f; d <= max && HexGrid.IsPath(grid.Kind(grid.CellAt(from + axis * d))); d += 0.5f) hi = d;
            for (float d = 0.5f; d <= max && HexGrid.IsPath(grid.Kind(grid.CellAt(from - axis * d))); d += 0.5f) lo = d;
            return (lo, hi);
        }

        static void BuildGate(MapData map, HexGrid grid, Transform root, MapPoint spawn)
        {
            var pos = map.World(spawn.x, spawn.y);
            var inward = map.Inward(pos);
            var across = Vector3.Cross(Vector3.up, inward);
            var (lo, hi) = OpenSpan(grid, pos + inward * 1.5f, across);

            var gate = new GameObject(spawn.name).transform;
            gate.SetParent(root, false);
            gate.position = pos - inward * 1.6f + across * ((hi - lo) / 2);
            gate.rotation = Quaternion.LookRotation(inward);
            float half = (hi + lo) / 2 + 1.2f;

            var stone = Mats.Lit(U.Hex(0x2a2522));
            var glow = Mats.Lit(U.Hex(0xff3355), 2f);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(-half, 3.5f, 0), new Vector3(1.2f, 7, 1.2f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(half, 3.5f, 0), new Vector3(1.2f, 7, 1.2f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 7.2f, 0), new Vector3(half * 2 + 1.2f, 0.8f, 1.4f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 7.2f, 0.75f), new Vector3(half * 2, 0.25f, 0.1f), glow);
            var light = new GameObject("Gate light").AddComponent<Light>();
            light.transform.SetParent(gate, false);
            light.transform.localPosition = new Vector3(0, 4, 3);
            light.type = LightType.Point;
            light.color = U.Hex(0xff3355);
            light.range = 18;
            light.intensity = 2.5f;
        }

        /// <summary>The base: a mint beacon at the path's end, where a breach is counted.</summary>
        static void BuildBase(MapData map, HexGrid grid, Transform root)
        {
            var yard = new GameObject(map.endpoint.name).transform;
            yard.SetParent(root, false);
            yard.position = map.Base;
            var outward = -OpenDirection(grid, map.Base);
            yard.rotation = Quaternion.LookRotation(outward);

            var metal = Mats.Lit(U.Hex(0x3a4048));
            var mint = Mats.Lit(U.Hex(0x5effc4), 2.5f);
            Shapes.Prim(PrimitiveType.Cylinder, yard, new Vector3(0, 0.3f, 1.5f), new Vector3(4f, 0.3f, 4f), metal, true);
            Shapes.Prim(PrimitiveType.Cylinder, yard, new Vector3(0, 4f, 1.5f), new Vector3(0.6f, 4f, 0.6f), metal, true);
            Shapes.Prim(PrimitiveType.Sphere, yard, new Vector3(0, 8.4f, 1.5f), Vector3.one * 1.4f, mint);
            Shapes.Prim(PrimitiveType.Cylinder, yard, new Vector3(0, 0.62f, 1.5f), new Vector3(4.4f, 0.02f, 4.4f), mint);
            foreach (var x in new[] { -5f, 5f })
            {
                Shapes.Prim(PrimitiveType.Cube, yard, new Vector3(x, 0.9f, 0.5f), new Vector3(1.8f, 1.8f, 1.8f), Mats.Lit(U.Hex(0x6b5a3a)), true);
                Shapes.Prim(PrimitiveType.Cube, yard, new Vector3(x * 0.8f, 0.7f, 1.2f), new Vector3(1.4f, 1.4f, 1.4f), Mats.Lit(U.Hex(0x5a4a30)), true);
            }
            var light = new GameObject("Beacon light").AddComponent<Light>();
            light.transform.SetParent(yard, false);
            light.transform.localPosition = new Vector3(0, 6, 0);
            light.type = LightType.Point;
            light.color = U.Hex(0x5effc4);
            light.range = 20;
            light.intensity = 3f;
        }

        /// <summary>
        /// Bakes the horde's walkable floor. A low climb keeps orcs off the tower
        /// ground; barricades carve themselves out as they are placed.
        /// </summary>
        public static void BakeNavMesh(MapData map)
        {
            var bounds = new Bounds(new Vector3(map.Width / 2, 5, map.Depth / 2), new Vector3(map.Width + 4, 20, map.Depth + 4));
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, Layers.GroundMask, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.45f;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 30f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            navData.Add(NavMesh.AddNavMeshData(data));
        }

        /// <summary>Navmesh data outlives the scene that added it, so it is removed by hand on the way out.</summary>
        public static void ClearNavMesh()
        {
            foreach (var d in navData) d.Remove();
            navData.Clear();
        }

        /// <summary>Gathers faces with flat normals and vertex colours, then emits them as one mesh.</summary>
        sealed class MeshBatch
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> tris = new List<int>();

            /// <summary>Adds a triangle, wound so its face points along <paramref name="outward"/>.</summary>
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color col)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) (b, c) = (c, b);
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                colors.Add(col); colors.Add(col); colors.Add(col);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }

            public void AddTop(Vector3 center, Vector3[] corners, Color col)
            {
                for (int i = 0; i < 6; i++)
                    Tri(center, center + corners[i], center + corners[(i + 1) % 6], Vector3.up, col);
            }

            public void AddSide(Vector3 center, Vector3 a, Vector3 b, float low, float high, Color col)
            {
                var outward = (a + b) / 2;
                Vector3 a0 = center + a + Vector3.up * low, b0 = center + b + Vector3.up * low;
                Vector3 a1 = center + a + Vector3.up * high, b1 = center + b + Vector3.up * high;
                Tri(a0, b0, b1, outward, col);
                Tri(a0, b1, a1, outward, col);
            }

            /// <summary>A thin flat strip along a top edge, inset from it.</summary>
            public void AddStrip(Vector3 center, Vector3 a, Vector3 b, float y, float width)
            {
                var inset = -((a + b) / 2).normalized * width;
                Vector3 p0 = center + a + Vector3.up * y, p1 = center + b + Vector3.up * y;
                Tri(p0, p1, p1 + inset, Vector3.up, Color.white);
                Tri(p0, p1 + inset, p0 + inset, Vector3.up, Color.white);
            }

            public void Emit(Transform root, string name, Material mat, int layer, bool collider)
            {
                if (verts.Count == 0) return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetColors(colors);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                if (!collider) mr.shadowCastingMode = ShadowCastingMode.Off;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (layer >= 0) go.layer = layer;
                go.isStatic = true;
            }
        }
    }
}
