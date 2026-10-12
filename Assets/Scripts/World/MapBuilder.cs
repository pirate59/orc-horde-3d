using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace OrcHordeFps
{
    /// <summary>
    /// Raises an exported Orc Horde map into hex columns.
    ///
    /// Path is open floor that follows the land (<see cref="Relief"/>), rising and
    /// falling in slopes. Tower ground is a ledge beside it, at least a step
    /// above the road and terraced, that the player can walk onto and build on.
    /// Other rock is a cliff too tall to climb. Cells
    /// are merged into one mesh per kind per chunk, so a big map stays a few
    /// dozen draw calls.
    /// </summary>
    public static class MapBuilder
    {
        const float CliffMin = 5f, CliffVariance = 3.5f;
        const int ChunkSize = 16;
        /// <summary>Metres per texture repeat on the path, on platform tops and up cliff faces.</summary>
        const float PathTile = 4f, TopTile = 3f, SideTile = 3.5f;

        /// <summary>A grey vertex colour: per-cell light and dark on top of the texture.</summary>
        static Color Shade(float v) => new Color(v, v, v, 1);

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

            // Textures: Orc Horde's painted biome tiles on the path and the tower ground, the
            // baked cliff face on every side. Cliff tops reuse the rock slabs, darker.
            string biome = System.Array.IndexOf(new[] { "grassland", "canyon", "snow", "dirt" }, map.biome) >= 0 ? map.biome : "dirt";
            var cliffTint = new Color(Mathf.Min(1, rockColor.r * 1.9f), Mathf.Min(1, rockColor.g * 1.9f), Mathf.Min(1, rockColor.b * 1.9f));
            // Rock tops wear the biome: moss on grassland, snow, sand on the canyon, dust in the quarry.
            var cap = biome switch
            {
                "snow" => new Color(0.95f, 0.97f, 1f),
                "grassland" => new Color(0.5f, 0.66f, 0.38f),
                "canyon" => new Color(0.86f, 0.7f, 0.52f),
                _ => new Color(0.66f, 0.62f, 0.56f),
            };
            var rockMats = new[]
            {
                Mats.Terrain($"ground_{biome}", cap),
                new Material(Mats.Terrain("cliff", cliffTint, 1.2f)),
            };
            rockMats[1].SetFloat("_Bands", 1f);
            var groundMats = new[]
            {
                Mats.Terrain($"ground_{biome}", new Color(1.05f, 1.02f, 0.96f)),
                Mats.Terrain("cliff", Color.Lerp(cliffTint, groundColor, 0.4f), 1.2f),
            };
            var pathMats = new[] { Mats.Terrain($"path_{biome}", Color.white, 0.8f) };
            // Its own material, so the glow can fade in combat without touching anything else cyan.
            var edgeMat = new Material(Mats.Lit(U.Hex(0x3ec9e0), 1.2f));
            var corners = grid.CornerOffsets();
            var links = map.links ?? new MapLink[0];
            Relief.Init(map, grid);
            // The base faces up the road; the rock behind it is cleared for it to stand in.
            BaseFacing = OpenDirection(grid, map.Base);
            basePoint = map.Base;

            for (int cx = 0; cx < grid.Columns; cx += ChunkSize)
            for (int cy = grid.FirstRow; cy <= grid.LastRow; cy += ChunkSize)
            {
                var rock = new MeshBatch();
                var ground = new MeshBatch();
                var path = new MeshBatch();
                var bore = new MeshBatch();
                var edges = new MeshBatch();

                for (int c = cx; c < Mathf.Min(cx + ChunkSize, grid.Columns); c++)
                for (int r = cy; r < Mathf.Min(cy + ChunkSize, grid.LastRow + 1); r++)
                {
                    char kind = grid.Kind(c, r);
                    var center = grid.Center(c, r);
                    float noise = Mathf.PerlinNoise(c * 0.31f + 7f, r * 0.29f + 3f);
                    float h = Height(map, grid, links, c, r);

                    // Rock a tunnel runs through is bored out: its neighbours' faces become the passage
                    // walls, and its floor is drawn only, so the navmesh still crosses by the link.
                    if (InTunnel(map, links, center))
                    {
                        bore.AddTop(center, corners, Shade(0.75f + noise * 0.1f), PathTile, Relief.Ground);
                        // The roof is the column field carried on over the passage: each column weathered at
                        // the top as usual, and broken off underneath at its own height, so the bore's
                        // ceiling is a ragged run of column ends.
                        var crown = new float[6];
                        var faceShade = Shade(0.78f + noise * 0.2f);
                        RockTop(rock, center, corners, c, r, RockHeight(c, r, center), crown, Shade(0.85f + noise * 0.25f), faceShade);
                        float under = Links.TunnelRoof + Hash(c, r, 20) * 0.8f;
                        rock.AddUnder(center, corners, under, faceShade * 0.55f, SideTile);
                        for (int s = 0; s < 6; s++)
                        {
                            var n = HexGrid.Across(c, r, s);
                            var nc = grid.Center(n);
                            // Against solid rock nothing shows. Anywhere else (more of the roof, or the road at
                            // either mouth) the column's side runs down to its own broken end, so neighbouring
                            // columns broken off at different heights still close up into one mass.
                            if (grid.Kind(n) == HexGrid.Rock && !InTunnel(map, links, nc)) continue;
                            rock.AddSide(center, corners[s], corners[(s + 1) % 6], under, under, crown[s], crown[(s + 1) % 6], faceShade, SideTile);
                        }
                        continue;
                    }

                    if (HexGrid.IsPath(kind))
                    {
                        path.AddTop(center, corners, Shade(0.9f + noise * 0.15f), PathTile, Relief.Ground);
                        continue;
                    }

                    bool cliff = kind == HexGrid.Rock;
                    var batch = cliff ? rock : ground;
                    var side = Shade(0.78f + noise * 0.2f);
                    // Each corner's height at the top of the wall: level for a ledge or a bridge's bed,
                    // weathered for rock (see RockTop).
                    var tops = new float[6];
                    bool weathered = cliff && !Mathf.Approximately(h, Links.DeckHeight) && !InBaseClearing(center);
                    if (weathered) RockTop(batch, center, corners, c, r, h, tops, Shade(0.85f + noise * 0.25f), side);
                    else
                    {
                        batch.AddTop(center + Vector3.up * h, corners, Shade(0.85f + noise * 0.25f), TopTile, null);
                        for (int i = 0; i < 6; i++) tops[i] = h;
                    }

                    for (int s = 0; s < 6; s++)
                    {
                        var n = HexGrid.Across(c, r, s);
                        Vector3 a = corners[s], b = corners[(s + 1) % 6];
                        char nk = grid.Kind(n);
                        bool road = HexGrid.IsPath(nk);
                        // Against the road the wall's foot follows the slope; against other ground it is level.
                        // Against weathered rock it runs on down into the neighbour, so the groove between
                        // two columns' bevels never shows daylight.
                        float nh = Height(map, grid, links, n.x, n.y);
                        if (nk == HexGrid.Rock && !Mathf.Approximately(nh, Links.DeckHeight)) nh -= RockBevelDeepest;
                        float lowA = road ? Relief.Ground(center + a) : nh, lowB = road ? Relief.Ground(center + b) : nh;
                        float highA = tops[s], highB = tops[(s + 1) % 6];
                        if (lowA >= highA && lowB >= highB) continue;
                        batch.AddSide(center, a, b, lowA, lowB, highA, highB, side, SideTile);
                        if (!cliff && road)
                            edges.AddStrip(center, corners[s], corners[(s + 1) % 6], h + 0.01f, 0.08f);
                    }
                }

                rock.Emit(root, "Rock", rockMats, Layers.Rock, collider: true);
                ground.Emit(root, "Tower ground", groundMats, Layers.BuildGround, collider: true);
                path.Emit(root, "Path", pathMats, Layers.Road, collider: true);
                bore.Emit(root, "Tunnel floor", pathMats, -1, collider: false);
                edges.Emit(root, "Build edge", new[] { edgeMat }, -1, collider: false);
            }
            root.gameObject.AddComponent<BuildEdgeGlow>().Init(edgeMat);

            // The country round the map, then the scatter, which finds its footing by raycast.
            Landscape.Build(map, root, Mats.Terrain($"ground_{biome}", Color.Lerp(cap, new Color(0.55f, 0.55f, 0.55f), 0.35f)), rockMats[1]);
            Physics.SyncTransforms();
            Scatter.Build(map, grid, root);

            BuildBounds(map, root);
            foreach (var s in map.spawns) BuildGate(map, grid, root, s);
            BuildBase(map, grid, root);
            foreach (var link in links) Links.BuildVisual(map, link, root);
        }

        /// <summary>How far a rock column's weathered rim falls below its top, at most.</summary>
        const float RockBevelDeepest = 0.9f;

        static float Hash(int c, int r, int k) => Mathf.Abs(Mathf.Sin(c * 127.1f + r * 311.7f + k * 74.7f) * 43758.5453f) % 1f;

        /// <summary>
        /// A rock column's top, weathered: a slightly tilted crown inset from the
        /// edge, then a bevel falling away to the rim, deeper at some corners than
        /// others, and now and then a corner broken right off. Neighbouring columns'
        /// bevels meet in a groove, so a cliff reads as basalt columns, not blocks.
        /// Fills <paramref name="tops"/> with each corner's height at the rim.
        /// </summary>
        static void RockTop(MeshBatch batch, Vector3 center, Vector3[] corners, int c, int r, float h, float[] tops, Color topColor, Color sideColor)
        {
            const float Inset = 0.78f;
            var crown = new Vector3[6];
            var rim = new Vector3[6];
            float tilt = (Hash(c, r, 9) - 0.5f) * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                float lift = (Hash(c, r, i) - 0.5f) * 0.35f + tilt * Mathf.Cos(i * Mathf.PI / 3);
                float drop = 0.3f + Hash(c, r, i + 6) * 0.35f;
                if (Hash(c, r, 12) < 0.18f && i == (int)(Hash(c, r, 13) * 6)) drop = RockBevelDeepest;
                crown[i] = center + corners[i] * Inset + Vector3.up * (h + lift);
                rim[i] = center + corners[i] + Vector3.up * (h + lift - drop);
                tops[i] = rim[i].y;
            }
            batch.AddFan(center + Vector3.up * (h + 0.06f), crown, topColor, TopTile);
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                batch.AddQuad(1, crown[i], crown[j], rim[j], rim[i], (corners[i] + corners[j]) / 2 + Vector3.up * 0.4f,
                    sideColor * 0.95f, SideTile);
            }
        }

        /// <summary>
        /// A cell's standing height (a road cell's is the land at its centre). Rock
        /// inside a bridge's span is cut down to the deck, so the bridge rests on
        /// it; a tunnel's rock stays whole. Relief is level around both.
        /// </summary>
        static Vector3 basePoint;

        /// <summary>
        /// The courtyard behind the base: rock there is cut down to a low stone plinth, so the base
        /// (see BaseModels) stands in the open instead of behind a ring of columns. A plinth a step
        /// higher than the road, so the horde never walks onto it.
        /// </summary>
        public static bool InBaseClearing(Vector3 p)
        {
            var d = p - basePoint;
            d.y = 0;
            return d.magnitude < 15f && Vector3.Dot(d, -BaseFacing) > -1.5f;
        }

        const float PlinthHeight = 0.65f;

        static float Height(MapData map, HexGrid grid, MapLink[] links, int c, int r)
        {
            char kind = grid.Kind(c, r);
            var cell = new Vector2Int(c, r);
            var center = grid.Center(c, r);
            if (HexGrid.IsPath(kind)) return Relief.Ground(center);
            if (kind == HexGrid.TowerGround) return Relief.TowerGround(cell);
            foreach (var link in links)
                if (!link.IsTunnel && Links.SpanRect(map, link).Contains(new Vector2(center.x, center.z)))
                    return Links.DeckHeight;
            if (InTunnel(map, links, center)) return Relief.Ground(center);
            if (InBaseClearing(center)) return Relief.CellPeak(cell) + PlinthHeight;
            return RockHeight(c, r, center);
        }

        /// <summary>A rock column's height, from the land under it.</summary>
        static float RockHeight(int c, int r, Vector3 center) =>
            Relief.CellPeak(new Vector2Int(c, r)) + CliffMin + Mathf.PerlinNoise(c * 0.37f, r * 0.41f) * CliffVariance;

        /// <summary>Whether a tunnel runs through this point: its rock is bored out.</summary>
        static bool InTunnel(MapData map, MapLink[] links, Vector3 p)
        {
            foreach (var link in links)
                if (link.IsTunnel && Links.SpanRect(map, link).Contains(new Vector2(p.x, p.z))) return true;
            return false;
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

            var stone = Mats.Textured("stone", U.Hex(0x8a7f78), new Vector2(1, 3));
            var lintel = Mats.Textured("stone", U.Hex(0x8a7f78), new Vector2(6, 1));
            var glow = Mats.Lit(U.Hex(0xff3355), 2f);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(-half, 3.5f, 0), new Vector3(1.2f, 7, 1.2f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(half, 3.5f, 0), new Vector3(1.2f, 7, 1.2f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 7.2f, 0), new Vector3(half * 2 + 1.2f, 0.8f, 1.4f), lintel);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 7.2f, 0.75f), new Vector3(half * 2, 0.25f, 0.1f), glow);
            var light = new GameObject("Gate light").AddComponent<Light>();
            light.transform.SetParent(gate, false);
            light.transform.localPosition = new Vector3(0, 4, 3);
            light.type = LightType.Point;
            light.color = U.Hex(0xff3355);
            light.range = 18;
            light.intensity = 2.5f;
        }

        /// <summary>Which way the base looks: up the road the horde comes down.</summary>
        public static Vector3 BaseFacing { get; private set; } = Vector3.forward;

        /// <summary>The base at the path's end, where a breach is counted, built for the biome (see BaseModels).</summary>
        static void BuildBase(MapData map, HexGrid grid, Transform root)
        {
            var yard = new GameObject(map.endpoint.name).transform;
            yard.SetParent(root, false);
            yard.position = map.Base;
            var outward = -OpenDirection(grid, map.Base);
            yard.rotation = Quaternion.LookRotation(outward);

            BaseModels.Build(yard, map.biome);
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

        /// <summary>
        /// Gathers faces with flat normals, vertex colours and world-space UVs, in
        /// two submeshes: tops (0) and sides (1), so each can take its own texture.
        /// </summary>
        sealed class MeshBatch
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int>[] tris = { new List<int>(), new List<int>() };

            /// <summary>Adds a triangle, wound so its face points along <paramref name="outward"/>.</summary>
            void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward, Color col)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); }
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
                colors.Add(col); colors.Add(col); colors.Add(col);
                tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
            }

            static Vector2 Plan(Vector3 p, float tile) => new Vector2(p.x, p.z) / tile;

            /// <summary>A hex top: flat, or with <paramref name="land"/> set, each vertex lifted onto the land so neighbours join in one slope.</summary>
            public void AddTop(Vector3 center, Vector3[] corners, Color col, float tile, System.Func<Vector3, float> land)
            {
                Vector3 Lift(Vector3 p) => land == null ? p : new Vector3(p.x, land(p), p.z);
                for (int i = 0; i < 6; i++)
                {
                    Vector3 a = Lift(center), b = Lift(center + corners[i]), c = Lift(center + corners[(i + 1) % 6]);
                    Tri(0, a, b, c, Plan(a, tile), Plan(b, tile), Plan(c, tile), Vector3.up, col);
                }
            }

            /// <summary>A wall face; its texture runs along the edge and up the height, continuous between neighbours.</summary>
            public void AddSide(Vector3 center, Vector3 a, Vector3 b, float lowA, float lowB, float highA, float highB, Color col, float tile)
            {
                var outward = (a + b) / 2;
                Vector3 a0 = center + a + Vector3.up * Mathf.Min(lowA, highA), b0 = center + b + Vector3.up * Mathf.Min(lowB, highB);
                Vector3 a1 = center + a + Vector3.up * highA, b1 = center + b + Vector3.up * highB;
                var along = (b - a).normalized;
                Vector2 Uv(Vector3 p) => new Vector2(Vector3.Dot(p, along), p.y) / tile;
                Tri(1, a0, b0, b1, Uv(a0), Uv(b0), Uv(b1), outward, col);
                Tri(1, a0, b1, a1, Uv(a0), Uv(b1), Uv(a1), outward, col);
            }

            /// <summary>A fan from a centre point round a ring of points, on the tops' submesh with plan UVs.</summary>
            public void AddFan(Vector3 middle, Vector3[] ring, Color col, float tile)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    Vector3 a = middle, b = ring[i], c = ring[(i + 1) % ring.Length];
                    Tri(0, a, b, c, Plan(a, tile), Plan(b, tile), Plan(c, tile), Vector3.up, col);
                }
            }

            /// <summary>A hex face looking down, on the walls' submesh: the broken end of a column overhead.</summary>
            public void AddUnder(Vector3 center, Vector3[] corners, float y, Color col, float tile)
            {
                var mid = center + Vector3.up * y;
                for (int i = 0; i < 6; i++)
                {
                    Vector3 a = mid, b = mid + corners[i], c = mid + corners[(i + 1) % 6];
                    Tri(1, a, b, c, Plan(a, tile), Plan(b, tile), Plan(c, tile), Vector3.down, col);
                }
            }

            /// <summary>A quad on a submesh, textured like a wall: along its top edge and up the height.</summary>
            public void AddQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color col, float tile)
            {
                var along = new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
                Vector2 Uv(Vector3 p) => new Vector2(Vector3.Dot(p, along), p.y) / tile;
                Tri(sub, a, b, c, Uv(a), Uv(b), Uv(c), outward, col);
                Tri(sub, a, c, d, Uv(a), Uv(c), Uv(d), outward, col);
            }

            /// <summary>A thin flat strip along a top edge, inset from it.</summary>
            public void AddStrip(Vector3 center, Vector3 a, Vector3 b, float y, float width)
            {
                var inset = -((a + b) / 2).normalized * width;
                Vector3 p0 = center + a + Vector3.up * y, p1 = center + b + Vector3.up * y;
                Tri(0, p0, p1, p1 + inset, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up, Color.white);
                Tri(0, p0, p1 + inset, p0 + inset, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up, Color.white);
            }

            public void Emit(Transform root, string name, Material[] mats, int layer, bool collider)
            {
                if (verts.Count == 0) return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uvs);
                int subs = mats.Length;
                mesh.subMeshCount = subs;
                for (int s = 0; s < subs; s++) mesh.SetTriangles(s < tris.Length ? tris[s] : new List<int>(), s);
                // Faces meant for a submesh this batch has no material for join the first.
                if (subs == 1 && tris[1].Count > 0) { var all = new List<int>(tris[0]); all.AddRange(tris[1]); mesh.SetTriangles(all, 0); }
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();

                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                if (!collider) mr.shadowCastingMode = ShadowCastingMode.Off;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (layer >= 0) go.layer = layer;
                go.isStatic = true;
            }
        }
    }

    /// <summary>
    /// The cyan line round every ledge: full in the build phase, where it shows
    /// where towers go, faded to a trace in a fight, where it would only be noise.
    /// </summary>
    public sealed class BuildEdgeGlow : MonoBehaviour
    {
        const float InFight = 0.12f;
        static readonly Color Dull = new Color(0.16f, 0.2f, 0.22f);
        Material material;
        Color colour;
        float level = 1f;

        public void Init(Material m)
        {
            material = m;
            colour = m.color;
        }

        void Update()
        {
            var gm = GameManager.I;
            float want = gm == null || gm.Phase != Phase.Combat ? 1f : InFight;
            if (Mathf.Approximately(level, want)) return;
            level = Mathf.MoveTowards(level, want, Time.deltaTime * 1.5f);
            material.color = Color.Lerp(Dull, colour, level);
            material.SetColor("_EmissionColor", colour * 1.2f * level);
        }
    }
}
