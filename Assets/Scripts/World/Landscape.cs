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

        static MapData map;
        static Vector2[] valleys = new Vector2[0];
        static Vector2[] valleyDirs = new Vector2[0];

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

            // Valleys out from the gates and the base: a flat floor first, then rising walls.
            var p = new Vector2(x, z);
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
                h = Mathf.Lerp(Mathf.Min(h, floor), h, open);
            }
            return h;
        }

        public static void Build(MapData m, Transform root, Material ground)
        {
            map = m;
            var points = new List<Vector2>();
            var dirs = new List<Vector2>();
            void Valley(Vector3 at)
            {
                var inward = m.Inward(at);
                points.Add(new Vector2(at.x, at.z));
                dirs.Add(new Vector2(-inward.x, -inward.z));
            }
            foreach (var s in m.spawns) Valley(m.World(s.x, s.y));
            // The base only opens onto the country when it sits by the edge.
            var b = m.Base;
            if (Mathf.Min(Mathf.Min(b.x, m.Width - b.x), Mathf.Min(b.z, m.Depth - b.z)) < 12f) Valley(b);
            valleys = points.ToArray();
            valleyDirs = dirs.ToArray();

            float x0 = -Reach, z0 = -Reach;
            int nx = Mathf.CeilToInt((m.Width + Reach * 2) / Cell) + 1, nz = Mathf.CeilToInt((m.Depth + Reach * 2) / Cell) + 1;
            // In strips, so no one mesh is huge and the camera culls what's behind it.
            const int Strip = 24;
            for (int sz = 0; sz < nz - 1; sz += Strip)
            for (int sx = 0; sx < nx - 1; sx += Strip)
                BuildPatch(root, ground, x0, z0, sx, sz, Mathf.Min(sx + Strip, nx - 1), Mathf.Min(sz + Strip, nz - 1));
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
