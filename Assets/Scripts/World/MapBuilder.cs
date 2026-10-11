using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace OrcHordeFps
{
    /// <summary>
    /// Raises an Orc Horde map into 3D.
    ///
    /// The ground is classified on a grid: the road is open floor the horde
    /// walks, the shoulder either side is a low platform where towers may
    /// stand, and everything else is a rock wall too tall to climb. Same-kind
    /// cells along a row are merged into one block to keep the object count low.
    /// </summary>
    public static class MapBuilder
    {
        const float Cell = 1.6f;
        public const float ShoulderHeight = 0.35f;
        const float RockHeight = 5f;

        enum Kind { Road, Shoulder, Rock }

        public static void Build(MapDef map, Transform root)
        {
            int cols = Mathf.CeilToInt(map.Width / Cell);
            int rows = Mathf.CeilToInt(map.Depth / Cell);

            var floor = Shapes.Prim(PrimitiveType.Cube, root, new Vector3(map.Width / 2, -0.25f, map.Depth / 2),
                new Vector3(map.Width + 40, 0.5f, map.Depth + 40), Mats.Lit(map.FloorColor), collider: true);
            floor.name = "Road floor";
            floor.layer = Layers.Road;

            var rockMat = Mats.Lit(map.RockColor);
            var rockMatDark = Mats.Lit(map.RockColor * 0.85f);
            var shoulderMat = Mats.Lit(map.ShoulderColor);
            var edgeMat = Mats.Lit(U.Hex(0x3ec9e0), 1.2f);

            for (int z = 0; z < rows; z++)
            {
                int x = 0;
                while (x < cols)
                {
                    var kind = Classify(map, x, z);
                    int start = x;
                    while (x < cols && Classify(map, x, z) == kind) x++;
                    if (kind == Kind.Road) continue;

                    float minX = start * Cell, maxX = Mathf.Min(x * Cell, map.Width);
                    float cz = (z + 0.5f) * Cell;
                    if (kind == Kind.Rock)
                    {
                        // A ragged skyline reads as cut stone rather than a wall of boxes.
                        float hgt = RockHeight + Mathf.PerlinNoise(start * 0.37f, z * 0.41f) * 3f;
                        var rock = Shapes.Prim(PrimitiveType.Cube, root, new Vector3((minX + maxX) / 2, hgt / 2, cz),
                            new Vector3(maxX - minX, hgt, Cell), (z + start) % 2 == 0 ? rockMat : rockMatDark, true);
                        rock.layer = Layers.Rock;
                        rock.isStatic = true;
                    }
                    else
                    {
                        var pad = Shapes.Prim(PrimitiveType.Cube, root,
                            new Vector3((minX + maxX) / 2, ShoulderHeight / 2, cz),
                            new Vector3(maxX - minX, ShoulderHeight, Cell), shoulderMat, true);
                        pad.layer = Layers.BuildGround;
                        pad.isStatic = true;
                    }
                }
            }

            BuildRoadEdges(map, root, edgeMat);
            BuildBounds(map, root);
            BuildSpawnGate(map, root);
            BuildBase(map, root);
        }

        static Kind Classify(MapDef map, int x, int z)
        {
            var p = new Vector2((x + 0.5f) * Cell, (z + 0.5f) * Cell);
            foreach (var r in map.Road)
                if (r.Contains(p)) return Kind.Road;
            foreach (var r in map.Road)
            {
                var grown = Rect.MinMaxRect(r.xMin - map.ShoulderDepth, r.yMin - map.ShoulderDepth,
                    r.xMax + map.ShoulderDepth, r.yMax + map.ShoulderDepth);
                if (grown.Contains(p)) return Kind.Shoulder;
            }
            return Kind.Rock;
        }

        /// <summary>Thin glowing strips where road meets shoulder, so the build edge is legible.</summary>
        static void BuildRoadEdges(MapDef map, Transform root, Material mat)
        {
            int cols = Mathf.CeilToInt(map.Width / Cell);
            int rows = Mathf.CeilToInt(map.Depth / Cell);
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < cols; x++)
            {
                if (Classify(map, x, z) != Kind.Shoulder) continue;
                var c = new Vector3((x + 0.5f) * Cell, ShoulderHeight + 0.01f, (z + 0.5f) * Cell);
                if (x > 0 && Classify(map, x - 1, z) == Kind.Road)
                    Shapes.Prim(PrimitiveType.Cube, root, c + new Vector3(-Cell / 2 + 0.05f, 0, 0), new Vector3(0.1f, 0.02f, Cell), mat);
                if (x < cols - 1 && Classify(map, x + 1, z) == Kind.Road)
                    Shapes.Prim(PrimitiveType.Cube, root, c + new Vector3(Cell / 2 - 0.05f, 0, 0), new Vector3(0.1f, 0.02f, Cell), mat);
                if (z > 0 && Classify(map, x, z - 1) == Kind.Road)
                    Shapes.Prim(PrimitiveType.Cube, root, c + new Vector3(0, 0, -Cell / 2 + 0.05f), new Vector3(Cell, 0.02f, 0.1f), mat);
                if (z < rows - 1 && Classify(map, x, z + 1) == Kind.Road)
                    Shapes.Prim(PrimitiveType.Cube, root, c + new Vector3(0, 0, Cell / 2 - 0.05f), new Vector3(Cell, 0.02f, 0.1f), mat);
            }
        }

        /// <summary>Invisible walls at the map's edge, where the road runs out into the gate and the yard.</summary>
        static void BuildBounds(MapDef map, Transform root)
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

        static void BuildSpawnGate(MapDef map, Transform root)
        {
            var gate = new GameObject(map.SpawnName).transform;
            gate.SetParent(root, false);
            gate.position = new Vector3(0.6f, 0, map.Spawn.z);
            var stone = Mats.Lit(U.Hex(0x2a2522));
            var glow = Mats.Lit(U.Hex(0xff3355), 2f);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 3.5f, -6.6f), new Vector3(1.2f, 7, 1.2f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 3.5f, 6.6f), new Vector3(1.2f, 7, 1.2f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0, 7.2f, 0), new Vector3(1.4f, 0.8f, 14.4f), stone);
            Shapes.Prim(PrimitiveType.Cube, gate, new Vector3(0.75f, 7.2f, 0), new Vector3(0.1f, 0.25f, 13f), glow);
            var light = new GameObject("Gate light").AddComponent<Light>();
            light.transform.SetParent(gate, false);
            light.transform.localPosition = new Vector3(3, 4, 0);
            light.type = LightType.Point;
            light.color = U.Hex(0xff3355);
            light.range = 18;
            light.intensity = 2.5f;
        }

        /// <summary>The base: a mint beacon over the road's end, where a breach is counted.</summary>
        static void BuildBase(MapDef map, Transform root)
        {
            var yard = new GameObject(map.BaseName).transform;
            yard.SetParent(root, false);
            yard.position = map.Base;
            var metal = Mats.Lit(U.Hex(0x3a4048));
            var mint = Mats.Lit(U.Hex(0x5effc4), 2.5f);
            Shapes.Prim(PrimitiveType.Cylinder, yard, new Vector3(1.5f, 0.3f, 0), new Vector3(4f, 0.3f, 4f), metal, true);
            Shapes.Prim(PrimitiveType.Cylinder, yard, new Vector3(1.5f, 4f, 0), new Vector3(0.6f, 4f, 0.6f), metal, true);
            Shapes.Prim(PrimitiveType.Sphere, yard, new Vector3(1.5f, 8.4f, 0), Vector3.one * 1.4f, mint);
            Shapes.Prim(PrimitiveType.Cylinder, yard, new Vector3(1.5f, 0.62f, 0), new Vector3(4.4f, 0.02f, 4.4f), mint);
            foreach (var z in new[] { -5f, 5f })
            {
                Shapes.Prim(PrimitiveType.Cube, yard, new Vector3(0.5f, 0.9f, z), new Vector3(1.8f, 1.8f, 1.8f),
                    Mats.Lit(U.Hex(0x6b5a3a)), true);
                Shapes.Prim(PrimitiveType.Cube, yard, new Vector3(1.2f, 0.7f, z * 0.8f), new Vector3(1.4f, 1.4f, 1.4f),
                    Mats.Lit(U.Hex(0x5a4a30)), true);
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
        /// Bakes the horde's walkable floor. A low climb keeps orcs off the
        /// shoulders; barricades carve themselves out as they are placed.
        /// </summary>
        public static void BakeNavMesh(MapDef map)
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
            NavMesh.AddNavMeshData(data);
        }
    }
}
