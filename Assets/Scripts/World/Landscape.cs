using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrcHordeFps
{
    /// <summary>
    /// The country beyond the map: a ring of hills and mountains that rises from
    /// just under the boundary cliffs, so each map sits in a landscape instead of
    /// on a slab. A valley runs out from every gate and from the base, so the
    /// horde comes down a pass and the yard looks out on open ground.
    ///
    /// Scenery only: nothing walks or builds on it, and the fog swallows the far
    /// side.
    /// </summary>
    public static class Landscape
    {
        /// <summary>How far it reaches past the map's edge, and the spacing of its grid.</summary>
        public const float Reach = 220f;
        const float Cell = 5f;
        /// <summary>Where it starts, just below the boundary cliffs' tops.</summary>
        const float RimHeight = 3.5f;
        const float ValleyHalfWidth = 9f, ValleyFloor = 0f;

        /// <summary>How far out a gate's valley runs before it ends in the hillside and the cave.</summary>
        public const float CaveDistance = 15f;
        /// <summary>How deep the cave goes into the hill: where the horde starts, out of sight.</summary>
        const float TunnelDepth = 9f;
        const float MouthHalfWidth = 3f, MouthHeight = 5f;

        static MapData map;
        static Vector2[] valleys = new Vector2[0];
        static Vector2[] valleyDirs = new Vector2[0];
        /// <summary>Which valleys end in a cave: the gates' do, the base's runs on into the country.</summary>
        static bool[] caves = new bool[0];
        static readonly Dictionary<string, Vector3[]> entries = new Dictionary<string, Vector3[]>();

        /// <summary>
        /// The way a gate's orcs come in: from the back of its cave, out of the mouth, down the
        /// valley and in at the gate, where they join the map. Null where a gate has no cave.
        /// </summary>
        public static Vector3[] EntryPath(string spawnId) => CavesOn && entries.TryGetValue(spawnId, out var p) ? p : null;

        /// <summary>Off for the smoke test's comparison runs: orcs appear at the gate as they used to.</summary>
        public static bool CavesOn = true;

        /// <summary>The land's height at a point outside the map.</summary>
        public static float Height(float x, float z)
        {
            float dx = Mathf.Max(0, Mathf.Max(-x, x - map.Width));
            float dz = Mathf.Max(0, Mathf.Max(-z, z - map.Depth));
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            // Inside the map the land sits just under the slab beneath it, so no gap opens at the edge.
            if (d <= 0) return -0.4f;

            // Rolling first, then ridges: a fold of hills near the map, mountains far off.
            float near = RimHeight + d * 0.08f;
            float hills = Mathf.PerlinNoise(x * 0.018f + 11f, z * 0.018f + 3f) * 14f;
            float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.008f + 5f, z * 0.008f + 9f) * 2f - 1f);
            float mountains = ridge * ridge * 55f * Mathf.SmoothStep(0, 1, (d - 40f) / 140f);
            float h = near + hills * Mathf.SmoothStep(0, 1, d / 30f) + mountains;

            // A mound over every cave, so its rock face and roof are buried in the hill, not stood on it.
            var p = new Vector2(x, z);
            for (int i = 0; i < valleys.Length; i++)
            {
                if (!caves[i]) continue;
                var heart = valleys[i] + valleyDirs[i] * (CaveDistance + 7f);
                float far = Vector2.Distance(p, heart);
                if (far < 30f) h = Mathf.Max(h, Mathf.Lerp(14f, h, Mathf.SmoothStep(0, 1, (far - 12f) / 18f)));
            }

            // Valleys out from the gates and the base: a flat floor first, then rising walls.
            for (int i = 0; i < valleys.Length; i++)
            {
                var o = p - valleys[i];
                float along = Vector2.Dot(o, valleyDirs[i]);
                if (along < -2f) continue;
                float across = Mathf.Abs(o.x * valleyDirs[i].y - o.y * valleyDirs[i].x);
                // The valley widens and climbs as it goes, so it reads as a pass, not a trench.
                float width = ValleyHalfWidth + along * 0.18f;
                float floor = ValleyFloor + Mathf.Max(0, along - 25f) * 0.12f;
                float open = Mathf.SmoothStep(0, 1, (across - width) / (width * 0.9f + 6f));
                // A gate's valley is a ravine, steep-walled and not much wider than the road, so the
                // cave's rock face sinks into its walls rather than standing out in a bowl.
                if (caves[i])
                {
                    width = MouthHalfWidth + 5f + Mathf.Max(0, along) * 0.1f;
                    open = Mathf.SmoothStep(0, 1, (across - width) / 5f);
                }
                // Nothing of the hill inside the tunnel: it stays a dark hole into the rock. The hollow
                // this leaves in the hill is wider than the tunnel (the grid is coarse), so the cave's roof
                // is a broad slab of rock that covers all of it.
                if (caves[i] && along > CaveDistance - 1f && along < CaveDistance + TunnelDepth + 2f && across < MouthHalfWidth + 2.5f)
                    return -1f;
                if (caves[i] && along > CaveDistance)
                {
                    // A gate's valley ends in a steep hillside, high enough to hold the cave.
                    float rise = Mathf.SmoothStep(0, 1, (along - CaveDistance) / 5f);
                    open = Mathf.Max(open, rise);
                    h = Mathf.Max(h, 12f + Mathf.PerlinNoise(x * 0.05f, z * 0.05f) * 6f);
                }
                h = Mathf.Lerp(Mathf.Min(h, floor), h, open);
            }
            return h;
        }

        static float EdgeDistance(MapData m, Vector3 p) => Mathf.Min(Mathf.Min(p.x, m.Width - p.x), Mathf.Min(p.z, m.Depth - p.z));

        public static void Build(MapData m, Transform root, Material ground, Material rock)
        {
            map = m;
            entries.Clear();
            var points = new List<Vector2>();
            var dirs = new List<Vector2>();
            var hasCave = new List<bool>();
            void Valley(Vector3 at, bool cave)
            {
                var inward = m.Inward(at);
                points.Add(new Vector2(at.x, at.z));
                dirs.Add(new Vector2(-inward.x, -inward.z));
                hasCave.Add(cave);
            }
            // Gates on the map's edge get a valley ending in a cave; one inside the map has no country to come from.
            var caveGates = new List<MapPoint>();
            foreach (var s in m.spawns)
            {
                var at = m.World(s.x, s.y);
                if (EdgeDistance(m, at) > 6f) continue;
                Valley(at, true);
                caveGates.Add(s);
            }
            // The base only opens onto the country when it sits by the edge.
            var b = m.Base;
            if (EdgeDistance(m, b) < 12f) Valley(b, false);
            valleys = points.ToArray();
            valleyDirs = dirs.ToArray();
            caves = hasCave.ToArray();

            float x0 = -Reach, z0 = -Reach;
            int nx = Mathf.CeilToInt((m.Width + Reach * 2) / Cell) + 1, nz = Mathf.CeilToInt((m.Depth + Reach * 2) / Cell) + 1;
            // In strips, so no one mesh is huge and the camera culls what's behind it.
            const int Strip = 24;
            for (int sz = 0; sz < nz - 1; sz += Strip)
            for (int sx = 0; sx < nx - 1; sx += Strip)
                BuildPatch(root, ground, x0, z0, sx, sz, Mathf.Min(sx + Strip, nx - 1), Mathf.Min(sz + Strip, nz - 1));

            foreach (var gate in caveGates) BuildCave(m, gate, root, rock);
        }

        /// <summary>
        /// The cave a gate's horde comes out of: a rock face across the valley's end with an arched
        /// mouth, a dark tunnel behind it with a dull red glow at the back, and the hill over all of it.
        /// </summary>
        static void BuildCave(MapData m, MapPoint gate, Transform root, Material rock)
        {
            var at = m.World(gate.x, gate.y);
            var outward = -m.Inward(at);
            var mouth = at + outward * CaveDistance;
            var cave = new GameObject($"Cave ({gate.name})").transform;
            cave.SetParent(root, false);
            cave.SetPositionAndRotation(mouth, Quaternion.LookRotation(outward));
            var rng = new System.Random(gate.id.GetHashCode());
            float R(float a, float b2) => a + (b2 - a) * (float)rng.NextDouble();

            // The face: two great shoulders of rock either side of the mouth, a lintel over it, and
            // broken blocks along the top so its line isn't ruled.
            // The cliff texture tiled to the size of each block, so a great shoulder of rock
            // isn't a few giant stones stretched over it.
            var faces = new Dictionary<int, Material>();
            Material Tiled(Vector3 size)
            {
                int key = Mathf.RoundToInt(Mathf.Max(size.x, size.y) / 3.5f);
                if (key < 1) key = 1;
                if (!faces.TryGetValue(key, out var mat))
                {
                    mat = new Material(rock) { mainTextureScale = Vector2.one * key };
                    faces[key] = mat;
                }
                return mat;
            }
            void Block(Vector3 centre, Vector3 size, float tilt)
            {
                var go = Shapes.Prim(PrimitiveType.Cube, cave, centre, size, Tiled(size));
                go.transform.localRotation = Quaternion.Euler(R(-tilt, tilt), R(-tilt * 2, tilt * 2), R(-tilt, tilt));
            }
            float shoulder = MouthHalfWidth + 8f;
            // Kept below the hill's crest, so from above the face sinks into the slope instead of standing on it.
            Block(new Vector3(-shoulder, 3.5f, 3.5f), new Vector3(16f, 11f, 8f), 3f);
            Block(new Vector3(shoulder, 3.5f, 3.5f), new Vector3(16f, 11f, 8f), 3f);
            Block(new Vector3(0, MouthHeight + 2.4f, 3.6f), new Vector3(MouthHalfWidth * 2 + 3f, 5f, 8f), 2f);
            for (int i = 0; i < 10; i++)
            {
                float x = R(-17f, 17f);
                Block(new Vector3(x, R(7f, 9.5f) - Mathf.Abs(x) * 0.08f, R(1.5f, 5f)), new Vector3(R(2f, 5f), R(2f, 4f), R(2f, 4f)), 20f);
            }
            // Rubble spilling out of the mouth onto the valley floor.
            for (int i = 0; i < 6; i++)
            {
                float side = rng.NextDouble() < 0.5 ? -1 : 1;
                Block(new Vector3(side * R(MouthHalfWidth + 0.5f, MouthHalfWidth + 4f), R(0.2f, 0.6f), R(-3f, 0.5f)),
                    Vector3.one * R(0.6f, 1.6f), 25f);
            }

            // The tunnel: dark rock on every side, so the mouth reads as a hole into the hill.
            // Walls and roof thick enough to fill the hole carved in the hill round the tunnel.
            var dark = Mats.Lit(U.Hex(0x15110f));
            Shapes.Prim(PrimitiveType.Cube, cave, new Vector3(-MouthHalfWidth - 2f, MouthHeight / 2 + 1f, TunnelDepth / 2 + 1f), new Vector3(4f, MouthHeight + 4f, TunnelDepth + 3f), dark);
            Shapes.Prim(PrimitiveType.Cube, cave, new Vector3(MouthHalfWidth + 2f, MouthHeight / 2 + 1f, TunnelDepth / 2 + 1f), new Vector3(4f, MouthHeight + 4f, TunnelDepth + 3f), dark);
            // The roof: a slab broad and long enough to cover the whole hollow the tunnel leaves in the hill.
            // Three slabs, stepped and tilted, so from above it reads as an outcrop, not a lid.
            Block(new Vector3(-5f, MouthHeight + 2.6f, TunnelDepth / 2 + 3f), new Vector3(15f, 6f, TunnelDepth + 9f), 4f);
            Block(new Vector3(5.5f, MouthHeight + 2.2f, TunnelDepth / 2 + 2f), new Vector3(14f, 5.5f, TunnelDepth + 7f), 5f);
            Block(new Vector3(0.5f, MouthHeight + 3.4f, TunnelDepth / 2 + 6f), new Vector3(12f, 6f, TunnelDepth + 4f), 6f);
            Shapes.Prim(PrimitiveType.Cube, cave, new Vector3(0, MouthHeight / 2, TunnelDepth + 0.5f), new Vector3(MouthHalfWidth * 2 + 2f, MouthHeight + 1f, 1f), dark);
            Shapes.Prim(PrimitiveType.Cube, cave, new Vector3(0, -0.45f, TunnelDepth / 2), new Vector3(MouthHalfWidth * 2, 1f, TunnelDepth + 1f), dark);
            // Something burns down there.
            var glow = new GameObject("Cave glow").AddComponent<Light>();
            glow.transform.SetParent(cave, false);
            glow.transform.localPosition = new Vector3(0, 2.2f, TunnelDepth * 0.7f);
            glow.type = LightType.Point;
            glow.color = U.Hex(0xff4a2a);
            glow.range = 9f;
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;

            // In by the back of the tunnel, out of the mouth, then in at the gate.
            entries[gate.id] = new[] { mouth + outward * (TunnelDepth - 1.5f), mouth - outward * 0.5f, at };
        }

        static void BuildPatch(Transform root, Material ground, float x0, float z0, int ax, int az, int bx, int bz)
        {
            // Skip patches wholly inside the map: the map covers them.
            float minX = x0 + ax * Cell, maxX = x0 + bx * Cell, minZ = z0 + az * Cell, maxZ = z0 + bz * Cell;
            if (minX > 2 && maxX < map.Width - 2 && minZ > 2 && maxZ < map.Depth - 2) return;

            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var flat = new List<int>();
            int w = bx - ax + 1;
            for (int iz = az; iz <= bz; iz++)
            for (int ix = ax; ix <= bx; ix++)
            {
                float x = x0 + ix * Cell, z = z0 + iz * Cell;
                // A little jitter so the grid never shows as rows.
                x += (Mathf.PerlinNoise(ix * 0.7f, iz * 0.7f) - 0.5f) * Cell * 0.6f;
                z += (Mathf.PerlinNoise(iz * 0.7f + 4f, ix * 0.7f) - 0.5f) * Cell * 0.6f;
                float y = Height(x, z);
                verts.Add(new Vector3(x, y, z));
                float shade = 0.7f + Mathf.PerlinNoise(x * 0.05f, z * 0.05f) * 0.3f;
                colors.Add(new Color(shade, shade, shade, 1));
                uvs.Add(new Vector2(x, z) / 6f);
            }
            for (int iz = 0; iz < bz - az; iz++)
            for (int ix = 0; ix < bx - ax; ix++)
            {
                int i0 = iz * w + ix, i1 = i0 + 1, i2 = i0 + w, i3 = i2 + 1;
                // Inside the map the land is sunk out of sight: leave those squares out altogether.
                if (verts[i0].y < 0 && verts[i1].y < 0 && verts[i2].y < 0 && verts[i3].y < 0) continue;
                flat.Add(i0); flat.Add(i2); flat.Add(i1);
                flat.Add(i1); flat.Add(i2); flat.Add(i3);
            }
            if (flat.Count == 0) return;

            var mesh = new Mesh { name = "Landscape", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(flat, 0);
            mesh.RecalculateNormals();
            // Steep ground goes grey and dark, as bare rock would: blended by vertex, so no hard patches.
            var normals = mesh.normals;
            for (int i = 0; i < colors.Count; i++)
            {
                float steepness = Mathf.SmoothStep(0, 1, (0.85f - normals[i].y) / 0.3f);
                var c = colors[i];
                colors[i] = Color.Lerp(c, new Color(c.r * 0.55f, c.g * 0.56f, c.b * 0.6f, 1), steepness);
            }
            mesh.SetColors(colors);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var go = new GameObject("Landscape");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ground;
            // Far hills cast no shadows worth their cost; near ones fall within the shadow distance anyway.
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
        }
    }
}
