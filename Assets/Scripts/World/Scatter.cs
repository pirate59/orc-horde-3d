using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OrcHordeFps
{
    /// <summary>
    /// Life on the ground: grass that sways, bushes, pebbles and boulders, dead
    /// trees, cacti and snow drifts, chosen by biome. Every piece is a small
    /// procedural mesh drawn instanced (Shaders/Scatter.shader), grouped by
    /// chunk so whole chunks behind the camera or past the fog are skipped.
    ///
    /// The road keeps only a few pebbles at its edges, so the fight stays easy
    /// to read; ledges get grass round their rims, clear of where a tower stands;
    /// rock tops and the country beyond the map get the larger pieces.
    /// </summary>
    public sealed class Scatter : MonoBehaviour
    {
        enum Kind { Tuft, Pebble, Boulder, Bush, DeadTree, Cactus, Drift }

        /// <summary>One kind of piece in one chunk: the matrices and colours of every copy.</summary>
        sealed class Batch
        {
            public Kind Kind;
            public Bounds Bounds;
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
            public readonly List<Vector4> Colors = new List<Vector4>();
            public Matrix4x4[][] Chunks;
            public MaterialPropertyBlock[] Blocks;
        }

        const float ChunkSize = 24f, DrawDistance = 150f;

        /// <summary>How many pieces the last map got: for the smoke test.</summary>
        public static int Placed;
        const int PerCall = 1023;

        static readonly Dictionary<Kind, Mesh> meshes = new Dictionary<Kind, Mesh>();
        static Material grassMaterial, solidMaterial;
        readonly Dictionary<(Kind, int, int), Batch> batches = new Dictionary<(Kind, int, int), Batch>();
        System.Random rng;
        string biome;

        public static void Build(MapData map, HexGrid grid, Transform root)
        {
            var s = new GameObject("Scatter").AddComponent<Scatter>();
            s.transform.SetParent(root, false);
            s.biome = map.biome;
            s.rng = new System.Random(map.id.GetHashCode());
            if (grassMaterial == null)
            {
                var shader = Shader.Find("OrcHorde/Scatter");
                grassMaterial = new Material(shader) { enableInstancing = true };
                grassMaterial.SetFloat("_Wind", 0.09f);
                grassMaterial.SetFloat("_UpNormals", 0.85f);
                solidMaterial = new Material(shader) { enableInstancing = true };
                solidMaterial.SetFloat("_Wind", 0.02f);
            }
            s.Place(map, grid);
            Placed = 0;
            foreach (var b in s.batches.Values) Placed += b.Matrices.Count;
            s.Seal();
        }

        float R() => (float)rng.NextDouble();
        float R(float a, float b) => a + (b - a) * R();

        /// <summary>The surface under a point, by raycast: road, ledge or rock top, whichever is there.</summary>
        static bool Surface(Vector3 p, out Vector3 at, out int layer)
        {
            at = p;
            layer = -1;
            if (!Physics.Raycast(new Vector3(p.x, 80f, p.z), Vector3.down, out var hit, 160f, Layers.GroundMask, QueryTriggerInteraction.Ignore))
                return false;
            // Only fairly level ground: nothing perches on a bevel or a cliff face.
            if (hit.normal.y < 0.8f) return false;
            at = hit.point;
            layer = hit.collider.gameObject.layer;
            return true;
        }

        void Place(MapData map, HexGrid grid)
        {
            float apothem = grid.Apothem;
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
            {
                var cell = new Vector2Int(c, r);
                char kind = grid.Kind(cell);
                var centre = grid.Center(cell);

                if (HexGrid.IsPath(kind))
                {
                    // A pebble or two, pushed out to the edge of the road.
                    if (biome != "snow" && R() < 0.18f) Drop(centre, apothem * R(0.75f, 0.95f), Kind.Pebble, SurfaceKind.Road);
                    if (biome == "grassland" && R() < 0.12f) Drop(centre, apothem * R(0.8f, 0.95f), Kind.Tuft, SurfaceKind.Road);
                    continue;
                }
                if (kind == HexGrid.TowerGround)
                {
                    // A ring of grass round the rim, clear of the tower's footing at the middle.
                    int n = biome == "snow" ? 1 : 4;
                    for (int i = 0; i < n; i++) Drop(centre, apothem * R(0.6f, 0.9f), LedgeKind(), SurfaceKind.Ledge);
                    if (R() < 0.3f) Drop(centre, apothem * R(0.6f, 0.9f), Kind.Pebble, SurfaceKind.Ledge);
                    continue;
                }
                // Rock tops: the bigger pieces, now and then.
                if (R() < 0.35f) Drop(centre, apothem * R(0f, 0.55f), RockKind(), SurfaceKind.Rock);
                if (biome != "snow" && R() < 0.5f) Drop(centre, apothem * R(0.1f, 0.6f), Kind.Tuft, SurfaceKind.Rock);
            }

            // The country beyond: sparse near the map, thinning out into the fog.
            for (int i = 0; i < 2600; i++)
            {
                float side = R();
                float d = Mathf.Pow(R(), 1.6f) * 120f + 3f;
                Vector3 p = side < 0.25f ? new Vector3(-d, 0, R() * map.Depth)
                    : side < 0.5f ? new Vector3(map.Width + d, 0, R() * map.Depth)
                    : side < 0.75f ? new Vector3(R(-d, map.Width + d), 0, -d)
                    : new Vector3(R(-d, map.Width + d), 0, map.Depth + d);
                float h = Landscape.Height(p.x, p.z);
                if (h < 0) continue;
                var at = new Vector3(p.x, h, p.z);
                Add(OutsideKind(), at, R() * 360f, R(0.9f, 1.6f));
            }
        }

        enum SurfaceKind { Road, Ledge, Rock }

        void Drop(Vector3 centre, float distance, Kind kind, SurfaceKind on)
        {
            float a = R() * Mathf.PI * 2;
            var p = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * distance;
            if (!Surface(p, out var at, out int layer)) return;
            int want = on == SurfaceKind.Road ? Layers.Road : on == SurfaceKind.Ledge ? Layers.BuildGround : Layers.Rock;
            if (layer != want) return;
            Add(kind, at, R() * 360f, R(0.75f, 1.25f));
        }

        Kind LedgeKind() => biome switch
        {
            "snow" => R() < 0.6f ? Kind.Drift : Kind.Pebble,
            "canyon" => R() < 0.8f ? Kind.Tuft : Kind.Pebble,
            _ => Kind.Tuft,
        };

        Kind RockKind() => biome switch
        {
            "snow" => R() < 0.6f ? Kind.Drift : R() < 0.5f ? Kind.DeadTree : Kind.Boulder,
            "canyon" => R() < 0.45f ? Kind.Cactus : R() < 0.6f ? Kind.Boulder : Kind.Bush,
            "grassland" => R() < 0.7f ? Kind.Bush : R() < 0.3f ? Kind.DeadTree : Kind.Boulder,
            _ => R() < 0.4f ? Kind.DeadTree : R() < 0.5f ? Kind.Boulder : Kind.Bush,
        };

        Kind OutsideKind() => biome switch
        {
            "snow" => R() < 0.5f ? Kind.DeadTree : R() < 0.5f ? Kind.Drift : Kind.Boulder,
            "canyon" => R() < 0.4f ? Kind.Cactus : R() < 0.6f ? Kind.Boulder : Kind.Bush,
            "grassland" => R() < 0.6f ? Kind.Bush : R() < 0.5f ? Kind.DeadTree : Kind.Boulder,
            _ => R() < 0.45f ? Kind.DeadTree : R() < 0.5f ? Kind.Bush : Kind.Boulder,
        };

        /// <summary>Each piece's colour, by biome, with a little variety per copy.</summary>
        Color Tint(Kind kind)
        {
            Color c = kind switch
            {
                Kind.Tuft => biome switch
                {
                    "grassland" => U.Hex(0x6f9a3e), "canyon" => U.Hex(0xb89a5c), "snow" => U.Hex(0xb8c4c8), _ => U.Hex(0x8a8a4a),
                },
                Kind.Bush => biome switch { "canyon" => U.Hex(0x6e7444), "grassland" => U.Hex(0x4a7230), _ => U.Hex(0x5a6438) },
                Kind.Pebble => biome switch
                {
                    "canyon" => U.Hex(0xa87a5a), "snow" => U.Hex(0x8e969e), _ => U.Hex(0x8e887e),
                },
                Kind.Boulder => biome switch
                {
                    "canyon" => U.Hex(0x7e5640), "snow" => U.Hex(0x6a7078), _ => U.Hex(0x625d56),
                },
                Kind.DeadTree => U.Hex(0x4a3e34),
                Kind.Cactus => U.Hex(0x5f8a4a),
                _ => U.Hex(0xf2f6fa),
            };
            float v = R(0.82f, 1.12f);
            return new Color(c.r * v, c.g * v * R(0.95f, 1.05f), c.b * v, 1);
        }

        void Add(Kind kind, Vector3 at, float yaw, float size)
        {
            var key = (kind, Mathf.FloorToInt(at.x / ChunkSize), Mathf.FloorToInt(at.z / ChunkSize));
            if (!batches.TryGetValue(key, out var b))
            {
                b = new Batch { Kind = kind, Bounds = new Bounds(at, Vector3.one) };
                batches[key] = b;
            }
            var squash = kind == Kind.Pebble || kind == Kind.Boulder ? new Vector3(1, R(0.5f, 0.8f), R(0.8f, 1.2f)) : Vector3.one;
            b.Matrices.Add(Matrix4x4.TRS(at, Quaternion.Euler(kind == Kind.DeadTree ? R(-6f, 6f) : 0, yaw, 0), squash * size));
            b.Colors.Add(Tint(kind));
            b.Bounds.Encapsulate(at + Vector3.up * 4f);
        }

        /// <summary>Packs every batch into arrays of at most a thousand copies, each with its colours.</summary>
        void Seal()
        {
            foreach (var b in batches.Values)
            {
                int calls = (b.Matrices.Count + PerCall - 1) / PerCall;
                b.Chunks = new Matrix4x4[calls][];
                b.Blocks = new MaterialPropertyBlock[calls];
                for (int i = 0; i < calls; i++)
                {
                    int start = i * PerCall, count = Mathf.Min(PerCall, b.Matrices.Count - start);
                    b.Chunks[i] = b.Matrices.GetRange(start, count).ToArray();
                    b.Blocks[i] = new MaterialPropertyBlock();
                    b.Blocks[i].SetVectorArray("_Color", b.Colors.GetRange(start, count));
                }
                b.Matrices.Clear();
                b.Colors.Clear();
            }
        }

        static readonly Plane[] planes = new Plane[6];

        void Update()
        {
            // The player's camera, or a photo camera standing in for it.
            var cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var eye = cam.transform.position;
            foreach (var b in batches.Values)
            {
                // Whole chunks off screen or deep in the fog aren't drawn. Photo cameras sit inside
                // the same reach, so they see what the player would.
                if ((b.Bounds.ClosestPoint(eye) - eye).sqrMagnitude > DrawDistance * DrawDistance) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, b.Bounds) && Camera.allCamerasCount <= 1) continue;
                var mesh = MeshFor(b.Kind);
                bool grass = b.Kind == Kind.Tuft;
                var shadows = grass || b.Kind == Kind.Pebble || b.Kind == Kind.Drift ? ShadowCastingMode.Off : ShadowCastingMode.On;
                for (int i = 0; i < b.Chunks.Length; i++)
                    Graphics.DrawMeshInstanced(mesh, 0, grass ? grassMaterial : solidMaterial, b.Chunks[i], b.Chunks[i].Length,
                        b.Blocks[i], shadows, true, 0);
            }
        }

        // --- The pieces -------------------------------------------------------------

        static Mesh MeshFor(Kind kind)
        {
            if (meshes.TryGetValue(kind, out var m)) return m;
            var b = new Builder();
            switch (kind)
            {
                case Kind.Tuft: Tuft(b); break;
                case Kind.Pebble: b.Blob(Vector3.up * 0.06f, new Vector3(0.14f, 0.1f, 0.12f), 0.85f, 1.05f, 0, 3); break;
                case Kind.Boulder: b.Blob(Vector3.up * 0.3f, new Vector3(0.7f, 0.55f, 0.6f), 0.8f, 1.1f, 0, 5); break;
                case Kind.Bush:
                    b.Blob(new Vector3(0, 0.35f, 0), new Vector3(0.55f, 0.42f, 0.55f), 0.8f, 1.1f, 0.15f, 7);
                    b.Blob(new Vector3(0.35f, 0.28f, 0.1f), new Vector3(0.38f, 0.3f, 0.38f), 0.75f, 1.05f, 0.15f, 8);
                    b.Blob(new Vector3(-0.25f, 0.25f, -0.25f), new Vector3(0.36f, 0.3f, 0.36f), 0.75f, 1.05f, 0.15f, 9);
                    break;
                case Kind.DeadTree: DeadTree(b); break;
                case Kind.Cactus: Cactus(b); break;
                case Kind.Drift: b.Blob(Vector3.up * 0.02f, new Vector3(0.9f, 0.22f, 0.6f), 0.95f, 1.1f, 0, 11); break;
            }
            m = b.Mesh(kind.ToString());
            meshes[kind] = m;
            return m;
        }

        /// <summary>Seven blades fanning out of the ground, dark at the root and swaying at the tip.</summary>
        static void Tuft(Builder b)
        {
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.4f, lean = 0.12f + (i % 3) * 0.06f, h = 0.32f + (i % 4) * 0.07f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir) * 0.025f;
                var root = dir * 0.04f;
                var tip = root + dir * lean + Vector3.up * h;
                b.Tri(root - side, root + side, tip, new Color(0.55f, 0.55f, 0.55f, 0), new Color(0.55f, 0.55f, 0.55f, 0), new Color(1.1f, 1.1f, 1.1f, 1), dir);
            }
        }

        static void DeadTree(Builder b)
        {
            b.Limb(Vector3.zero, new Vector3(0.1f, 2.2f, 0.05f), 0.13f, 0.05f, 0.1f);
            b.Limb(new Vector3(0.05f, 1.2f, 0), new Vector3(0.7f, 1.9f, 0.2f), 0.06f, 0.02f, 0.5f);
            b.Limb(new Vector3(0.05f, 1.6f, 0), new Vector3(-0.5f, 2.3f, -0.3f), 0.05f, 0.02f, 0.6f);
            b.Limb(new Vector3(0.08f, 0.8f, 0), new Vector3(-0.3f, 1.2f, 0.45f), 0.05f, 0.02f, 0.4f);
        }

        static void Cactus(Builder b)
        {
            b.Limb(Vector3.zero, new Vector3(0, 1.6f, 0), 0.16f, 0.13f, 0.05f);
            b.Limb(new Vector3(0, 0.7f, 0), new Vector3(0.35f, 0.75f, 0), 0.09f, 0.09f, 0.05f);
            b.Limb(new Vector3(0.35f, 0.72f, 0), new Vector3(0.38f, 1.2f, 0), 0.09f, 0.08f, 0.05f);
            b.Limb(new Vector3(0, 0.95f, 0), new Vector3(-0.3f, 1.0f, 0), 0.08f, 0.08f, 0.05f);
            b.Limb(new Vector3(-0.3f, 0.98f, 0), new Vector3(-0.32f, 1.35f, 0), 0.08f, 0.07f, 0.05f);
        }

        /// <summary>Gathers triangles with vertex colours for the scatter shader: rgb shading, alpha sway.</summary>
        sealed class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Color> c = new List<Color>();
            readonly List<int> t = new List<int>();

            /// <summary>A triangle, turned so its face points along <paramref name="outward"/>: lit from outside, not within.</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color ca, Color cb, Color cd, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0) { (b, d) = (d, b); (cb, cd) = (cd, cb); }
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                c.Add(ca); c.Add(cb); c.Add(cd);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            /// <summary>A lumpy ball: an octahedron subdivided twice, each vertex pushed in or out a little.</summary>
            public void Blob(Vector3 centre, Vector3 size, float low, float high, float sway, int seed)
            {
                var r = new System.Random(seed);
                var tris = new List<Vector3[]>
                {
                    new[] { Vector3.up, Vector3.forward, Vector3.right }, new[] { Vector3.up, Vector3.right, Vector3.back },
                    new[] { Vector3.up, Vector3.back, Vector3.left }, new[] { Vector3.up, Vector3.left, Vector3.forward },
                    new[] { Vector3.down, Vector3.right, Vector3.forward }, new[] { Vector3.down, Vector3.back, Vector3.right },
                    new[] { Vector3.down, Vector3.left, Vector3.back }, new[] { Vector3.down, Vector3.forward, Vector3.left },
                };
                for (int pass = 0; pass < 2; pass++)
                {
                    var next = new List<Vector3[]>();
                    foreach (var f in tris)
                    {
                        Vector3 ab = (f[0] + f[1]).normalized, bc = (f[1] + f[2]).normalized, ca = (f[2] + f[0]).normalized;
                        next.Add(new[] { f[0], ab, ca }); next.Add(new[] { ab, f[1], bc });
                        next.Add(new[] { ca, bc, f[2] }); next.Add(new[] { ab, bc, ca });
                    }
                    tris = next;
                }
                var bump = new Dictionary<Vector3, float>();
                float Bump(Vector3 p)
                {
                    var k = new Vector3(Mathf.Round(p.x * 1000), Mathf.Round(p.y * 1000), Mathf.Round(p.z * 1000));
                    if (!bump.TryGetValue(k, out var s)) bump[k] = s = low + (float)r.NextDouble() * (high - low);
                    return s;
                }
                foreach (var f in tris)
                {
                    var p = new Vector3[3];
                    var col = new Color[3];
                    for (int k = 0; k < 3; k++)
                    {
                        var d = f[k] * Bump(f[k]);
                        p[k] = centre + Vector3.Scale(d, size);
                        // Darker underneath, as light would leave it.
                        float shade = 0.7f + 0.3f * Mathf.Clamp01(f[k].y * 0.5f + 0.5f);
                        col[k] = new Color(shade, shade, shade, sway * Mathf.Clamp01(p[k].y));
                    }
                    Tri(p[0], p[1], p[2], col[0], col[1], col[2], (p[0] + p[1] + p[2]) / 3 - centre);
                }
            }

            /// <summary>A tapering six-sided limb from one point to another.</summary>
            public void Limb(Vector3 from, Vector3 to, float r0, float r1, float sway)
            {
                var axis = (to - from).normalized;
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(axis, u);
                for (int i = 0; i < 6; i++)
                {
                    float a0 = i * Mathf.PI / 3, a1 = (i + 1) * Mathf.PI / 3;
                    Vector3 o0 = u * Mathf.Cos(a0) + w * Mathf.Sin(a0), o1 = u * Mathf.Cos(a1) + w * Mathf.Sin(a1);
                    float s0 = 0.75f + 0.25f * Mathf.Cos(a0 - 0.8f), s1 = 0.75f + 0.25f * Mathf.Cos(a1 - 0.8f);
                    Color c0 = new Color(s0, s0, s0, 0), c1 = new Color(s1, s1, s1, 0);
                    Color t0 = new Color(s0, s0, s0, sway), t1 = new Color(s1, s1, s1, sway);
                    Tri(from + o0 * r0, to + o0 * r1, to + o1 * r1, c0, t0, t1, o0 + o1);
                    Tri(from + o0 * r0, to + o1 * r1, from + o1 * r0, c0, t1, c1, o0 + o1);
                }
            }

            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetColors(c);
                m.SetTriangles(t, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
