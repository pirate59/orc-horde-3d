using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Builds a mesh from simple solids: skinned for orcs, plain for towers and guns. Each solid is bound wholly to
    /// the current bone, so a part moves rigidly with its joint; joints are
    /// hidden by overlapping round shapes, as on an action figure.
    ///
    /// Everything is authored in the rig's model space at its bind pose. Each
    /// vertex carries a colour, a texture coordinate for surface detail, and in
    /// its second texture coordinate how much it glows (x) and how smooth it is (y).
    /// </summary>
    public sealed class OrcMesh
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Vector2> extras = new List<Vector2>();
        readonly List<BoneWeight> weights = new List<BoneWeight>();
        readonly List<int> tris = new List<int>();

        /// <summary>The bone the next solids are bound to.</summary>
        public int Bone;
        public Color Color = Color.white;
        /// <summary>0 for none; around 1 to 3 for eyes, runes and embers.</summary>
        public float Glow;
        /// <summary>0 for hide and cloth, about 0.5 for worn metal.</summary>
        public float Smooth = 0.15f;

        public int VertexCount => verts.Count;

        /// <summary>Sets bone, colour and finish in one go.</summary>
        public OrcMesh Paint(int bone, Color color, float smooth = 0.15f, float glow = 0f)
        {
            Bone = bone;
            Color = color;
            Smooth = smooth;
            Glow = glow;
            return this;
        }

        int Vert(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(p);
            normals.Add(n.normalized);
            colors.Add(Color);
            uvs.Add(uv);
            extras.Add(new Vector2(Glow, Smooth));
            weights.Add(new BoneWeight { boneIndex0 = Bone, weight0 = 1f });
            return verts.Count - 1;
        }

        void Quad(int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        /// <summary>
        /// An ellipsoid, or a band of one between latitudes <paramref name="from"/>
        /// and <paramref name="to"/> (0 is the bottom pole, 1 the top), for domes
        /// such as pauldrons and helmets.
        /// </summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, Quaternion rotation, int segments = 14, int rings = 10,
            float from = 0f, float to = 1f)
        {
            int start = verts.Count;
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.Lerp(from, to, (float)r / rings) * Mathf.PI - Mathf.PI / 2;
                for (int s = 0; s <= segments; s++)
                {
                    float lon = (float)s / segments * Mathf.PI * 2;
                    var unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    var p = Vector3.Scale(unit, radii);
                    var n = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z);
                    Vert(center + rotation * p, rotation * n, new Vector2((float)s / segments * 2, (float)r / rings));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + r * (segments + 1) + s;
                Quad(a, a + segments + 1, a + segments + 2, a + 1);
            }
        }

        public void Sphere(Vector3 center, float radius, int segments = 10, int rings = 7) =>
            Ellipsoid(center, Vector3.one * radius, Quaternion.identity, segments, rings);

        /// <summary>A rounded, tapering limb from <paramref name="a"/> to <paramref name="b"/>: a capsule whose ends may differ in size.</summary>
        public void Capsule(Vector3 a, Vector3 b, float ra, float rb, int segments = 12, int capRings = 4)
        {
            var axis = b - a;
            float length = axis.magnitude;
            var d = length > 1e-5f ? axis / length : Vector3.up;
            var side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, d);

            // Rings from the far side of a's cap, along the shaft, to the far side of b's.
            var rings = new List<(Vector3 c, float r, float slope)>();
            for (int i = 0; i <= capRings; i++)
            {
                float t = -Mathf.PI / 2 + Mathf.PI / 2 * i / capRings;
                rings.Add((a + d * (ra * Mathf.Sin(t)), ra * Mathf.Cos(t), Mathf.Sin(t)));
            }
            for (int i = 0; i <= capRings; i++)
            {
                float t = Mathf.PI / 2 * i / capRings;
                rings.Add((b + d * (rb * Mathf.Sin(t)), rb * Mathf.Cos(t), Mathf.Sin(t)));
            }

            int start = verts.Count;
            for (int r = 0; r < rings.Count; r++)
            for (int s = 0; s <= segments; s++)
            {
                float lon = (float)s / segments * Mathf.PI * 2;
                var radial = side * Mathf.Cos(lon) + up * Mathf.Sin(lon);
                var (c, rad, slope) = rings[r];
                var n = radial * Mathf.Sqrt(Mathf.Max(0, 1 - slope * slope)) + d * slope;
                Vert(c + radial * rad, n, new Vector2((float)s / segments, (float)r / rings.Count * Mathf.Max(1, length * 4)));
            }
            for (int r = 0; r < rings.Count - 1; r++)
            for (int s = 0; s < segments; s++)
            {
                int i0 = start + r * (segments + 1) + s;
                Quad(i0, i0 + 1, i0 + segments + 2, i0 + segments + 1);
            }
        }

        /// <summary>A box, for plates, blades, hafts and straps.</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            var h = size / 2;
            var faces = new[]
            {
                (Vector3.right, Vector3.up, Vector3.forward), (Vector3.left, Vector3.up, Vector3.back),
                (Vector3.up, Vector3.forward, Vector3.right), (Vector3.down, Vector3.back, Vector3.right),
                (Vector3.forward, Vector3.up, Vector3.left), (Vector3.back, Vector3.up, Vector3.right),
            };
            foreach (var (n, u, w) in faces)
            {
                Vector3 S(Vector3 v) => Vector3.Scale(v, h);
                var c = center + rotation * S(n);
                var du = rotation * S(u);
                var dw = rotation * S(w);
                var rn = rotation * n;
                int i0 = Vert(c - du - dw, rn, new Vector2(0, 0));
                int i1 = Vert(c + du - dw, rn, new Vector2(0, 1));
                int i2 = Vert(c + du + dw, rn, new Vector2(1, 1));
                int i3 = Vert(c - du + dw, rn, new Vector2(1, 0));
                Quad(i0, i1, i2, i3);
            }
        }

        /// <summary>A cone from a round base to a point: spikes, horns' tips, ears, teeth.</summary>
        public void Cone(Vector3 baseCenter, Vector3 tip, float radius, int segments = 8)
        {
            var d = (tip - baseCenter).normalized;
            var side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, d);
            float slope = radius / Mathf.Max((tip - baseCenter).magnitude, 1e-4f);
            for (int s = 0; s < segments; s++)
            {
                float a0 = (float)s / segments * Mathf.PI * 2, a1 = (float)(s + 1) / segments * Mathf.PI * 2;
                var r0 = side * Mathf.Cos(a0) + up * Mathf.Sin(a0);
                var r1 = side * Mathf.Cos(a1) + up * Mathf.Sin(a1);
                var rm = (r0 + r1).normalized;
                int i0 = Vert(baseCenter + r0 * radius, r0 + d * slope, new Vector2(0, 0));
                int i1 = Vert(baseCenter + r1 * radius, r1 + d * slope, new Vector2(1, 0));
                int i2 = Vert(tip, rm + d * slope, new Vector2(0.5f, 1));
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                // Base cap.
                int b0 = Vert(baseCenter + r0 * radius, -d, Vector2.zero);
                int b1 = Vert(baseCenter + r1 * radius, -d, Vector2.zero);
                int bc = Vert(baseCenter, -d, Vector2.zero);
                tris.Add(b0); tris.Add(b1); tris.Add(bc);
            }
        }

        /// <summary>A short tube around an axis: belts, bracers, bands on a haft.</summary>
        public void Band(Vector3 center, Vector3 axis, float radius, float width, int segments = 16)
        {
            var d = axis.normalized;
            Capsule(center - d * width / 2, center + d * width / 2, radius, radius, segments, 1);
        }

        /// <summary>A capped cylinder from <paramref name="a"/> to <paramref name="b"/>, with hard edges: barrels, drums, tanks, plinths.</summary>
        public void Cylinder(Vector3 a, Vector3 b, float radius, int segments = 16, bool capA = true, bool capB = true)
        {
            var d = (b - a).normalized;
            var side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, d);
            int start = verts.Count;
            float len = (b - a).magnitude;
            for (int s = 0; s <= segments; s++)
            {
                float lon = (float)s / segments * Mathf.PI * 2;
                var radial = side * Mathf.Cos(lon) + up * Mathf.Sin(lon);
                Vert(a + radial * radius, radial, new Vector2((float)s / segments * 2, 0));
                Vert(b + radial * radius, radial, new Vector2((float)s / segments * 2, len * 2));
            }
            for (int s = 0; s < segments; s++)
            {
                int i = start + s * 2;
                Quad(i, i + 2, i + 3, i + 1);
            }
            foreach (var (c, n, on) in new[] { (a, -d, capA), (b, d, capB) })
            {
                if (!on) continue;
                int center = Vert(c, n, new Vector2(0.5f, 0.5f));
                int first = verts.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float lon = (float)s / segments * Mathf.PI * 2;
                    var radial = side * Mathf.Cos(lon) + up * Mathf.Sin(lon);
                    Vert(c + radial * radius, n, new Vector2(0.5f + Mathf.Cos(lon) * 0.5f, 0.5f + Mathf.Sin(lon) * 0.5f));
                }
                for (int s = 0; s < segments; s++) { tris.Add(center); tris.Add(first + s); tris.Add(first + s + 1); }
            }
        }

        /// <summary>A ring around <paramref name="axis"/>: coil turns, lens rims, toroids, belts of rivets.</summary>
        public void Torus(Vector3 center, Vector3 axis, float radius, float tube, int segments = 24, int sides = 8)
        {
            var d = axis.normalized;
            var side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, d);
            int start = verts.Count;
            for (int s = 0; s <= segments; s++)
            {
                float a = (float)s / segments * Mathf.PI * 2;
                var ring = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                for (int k = 0; k <= sides; k++)
                {
                    float b = (float)k / sides * Mathf.PI * 2;
                    var n = ring * Mathf.Cos(b) + d * Mathf.Sin(b);
                    Vert(center + ring * radius + n * tube, n, new Vector2((float)s / segments * 4, (float)k / sides));
                }
            }
            for (int s = 0; s < segments; s++)
            for (int k = 0; k < sides; k++)
            {
                int i = start + s * (sides + 1) + k;
                Quad(i, i + sides + 1, i + sides + 2, i + 1);
            }
        }

        /// <summary>A curved horn: a chain of tapering capsules along a bent path.</summary>
        public void Horn(Vector3 root, Vector3 outward, Vector3 curl, float length, float radius, int pieces = 5)
        {
            var p = root;
            var dir = outward.normalized;
            for (int i = 0; i < pieces; i++)
            {
                float t0 = (float)i / pieces, t1 = (float)(i + 1) / pieces;
                dir = (dir + curl.normalized * (1.2f / pieces)).normalized;
                var next = p + dir * length / pieces;
                if (i == pieces - 1) Cone(p, next + dir * length * 0.08f, radius * (1 - t0) + 0.002f, 8);
                else Capsule(p, next, radius * (1 - t0) + 0.002f, radius * (1 - t1) + 0.002f, 8, 1);
                p = next;
            }
        }

        /// <summary>A plain, unskinned mesh, for props built from the same solids.</summary>
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, extras);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public Mesh Build(string name, Matrix4x4[] bindposes)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, extras);
            mesh.SetTriangles(tris, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
