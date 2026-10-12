using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The base, built for the biome it stands in:
    /// - grassland: a frontier fort, a log palisade with watchtowers, a lodge and a signal fire
    /// - quarry dirt: a dwarven forge-hold, a great door in a rock face with chimneys and forge light
    /// - snow: a stone keep behind a curtain wall, with braziers and snow on every ledge
    /// - canyon: an adobe citadel of stacked clay blocks, beam ends, awnings and a domed tower
    ///
    /// Each stands behind the breach point (its local +z runs away from the road) and leaves the
    /// front open, so the horde still runs up to the same spot. All share a mint ring on the ground
    /// where a breach counts, a shield dome while shield layers are up, and the same damage: smoke
    /// below two thirds of the base's health, fire and broken parts below a third.
    /// </summary>
    public static class BaseModels
    {
        public static BaseRig Build(Transform yard, string biome)
        {
            var rig = yard.gameObject.AddComponent<BaseRig>();
            switch (biome)
            {
                case "grassland": Fort(yard, rig); break;
                case "snow": Keep(yard, rig); break;
                case "canyon": Citadel(yard, rig); break;
                default: ForgeHold(yard, rig); break;
            }
            rig.Init();
            return rig;
        }

        /// <summary>Where a breach counts: a thin mint ring on the ground, the one thing every base shares.</summary>
        public static void Ring(Transform yard, Vector3 at)
        {
            var ring = Fx.NewLine(yard, U.Hex(0x5effc4) * 1.6f, 0.12f);
            Fx.SetGroundCircle(ring, at, 3.5f, 48);
        }

        // --- Pieces ---------------------------------------------------------------------

        /// <summary>A textured box, its texture tiled to its size so a long wall isn't a stretched stone.</summary>
        static GameObject Box(Transform parent, Vector3 at, Vector3 size, string texture, Color tint, float block = 2f,
            Vector3 euler = default, bool collider = true, float smooth = 0.15f, float metal = 0f)
        {
            var tiling = new Vector2(Mathf.Max(1, Mathf.Round(Mathf.Max(size.x, size.z) / block)), Mathf.Max(1, Mathf.Round(size.y / block)));
            var go = Shapes.Prim(PrimitiveType.Cube, parent, at, size, Mats.Textured(texture, tint, tiling, smooth, metal), collider);
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        static GameObject Solid(Transform parent, PrimitiveType type, Vector3 at, Vector3 size, Material mat, Vector3 euler = default, bool collider = false)
        {
            var go = Shapes.Prim(type, parent, at, size, mat, collider);
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        /// <summary>A log (or a beam, a post) from one point to another.</summary>
        static GameObject Log(Transform parent, Vector3 from, Vector3 to, float radius, Material mat)
        {
            var go = Shapes.Prim(PrimitiveType.Cylinder, parent, (from + to) / 2, new Vector3(radius * 2, Vector3.Distance(from, to) / 2, radius * 2), mat);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            return go;
        }

        static readonly Dictionary<(int, bool), Mesh> cones = new Dictionary<(int, bool), Mesh>();

        /// <summary>A cone or pyramid standing on its base: n sides, unit size, apex at y = 1.</summary>
        static Mesh Cone(int sides, bool smooth)
        {
            if (cones.TryGetValue((sides, smooth), out var m)) return m;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                int k = v.Count;
                v.Add(p0); v.Add(Vector3.up); v.Add(p1);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0.5f, 1)); uv.Add(new Vector2(1, 0));
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }
            m = new Mesh { name = "Cone" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            cones[(sides, smooth)] = m;
            return m;
        }

        static GameObject Roof(Transform parent, Vector3 at, Vector3 size, int sides, Material mat, float yaw = 0)
        {
            var go = new GameObject("Roof");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Cone(sides, false);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A gable roof: a triangular prism along x.</summary>
        static GameObject Gable(Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var go = new GameObject("Gable");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            var m = new Mesh { name = "Gable" };
            Vector3 a = new Vector3(-0.5f, 0, -0.5f), b = new Vector3(0.5f, 0, -0.5f), c = new Vector3(0.5f, 0, 0.5f), d = new Vector3(-0.5f, 0, 0.5f);
            Vector3 e = new Vector3(-0.5f, 1, 0), f = new Vector3(0.5f, 1, 0);
            var v = new List<Vector3> { a, e, f, b, d, c, f, e, a, d, e, b, f, c };
            var uv = new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(3, 1), new Vector2(3, 0), new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(1, 0) };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 11, 12, 13 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Light Glow(Transform parent, Vector3 at, Color color, float range, float intensity)
        {
            var l = new GameObject("Glow").AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.localPosition = at;
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>A banner on a pole: mint, the defenders' colour.</summary>
        static void Banner(Transform parent, Vector3 foot, float height, BaseRig rig)
        {
            var pole = Log(parent, foot, foot + Vector3.up * height, 0.06f, Mats.Textured("metal", U.Hex(0x4a4f56), smoothness: 0.4f, metallic: 0.5f));
            var cloth = Solid(parent, PrimitiveType.Cube, foot + new Vector3(0.6f, height - 0.8f, 0), new Vector3(1.1f, 1.4f, 0.04f), Mats.Lit(U.Hex(0x2fa985)));
            rig.Wave(cloth.transform);
            rig.Breakable(pole.transform, new Vector3(0, -height * 0.3f, 0), new Vector3(0, 0, 70));
            rig.Breakable(cloth.transform, new Vector3(0.4f, -height * 0.55f, 0), new Vector3(0, 0, 80));
        }

        static void Fire(Transform parent, Vector3 at, BaseRig rig, float size = 1f)
        {
            // An iron bowl on a stone footing, burning: the base's heart.
            var iron = Mats.Textured("metal", U.Hex(0x3a3d42), smoothness: 0.35f, metallic: 0.6f);
            Solid(parent, PrimitiveType.Cylinder, at + Vector3.up * 0.5f * size, new Vector3(0.5f, 0.5f, 0.5f) * size, iron);
            Solid(parent, PrimitiveType.Cylinder, at + Vector3.up * 1.05f * size, new Vector3(1.2f, 0.1f, 1.2f) * size, iron);
            Solid(parent, PrimitiveType.Sphere, at + Vector3.up * 1.2f * size, new Vector3(0.7f, 0.5f, 0.7f) * size, Mats.Lit(U.Hex(0xffa040), 3f));
            rig.Flame(at + Vector3.up * 1.25f * size, size, Glow(parent, at + Vector3.up * 2f * size, U.Hex(0xffa050), 9f * size, 1.6f));
        }

        // --- Grassland: a frontier fort --------------------------------------------------------------

        static void Fort(Transform yard, BaseRig rig)
        {
            var logs = Mats.Textured("wood", U.Hex(0x9a7a5a), new Vector2(1, 3));
            var dark = Mats.Textured("wood", U.Hex(0x6a5038), new Vector2(1, 3));
            var thatch = Mats.Textured("wood", U.Hex(0x8a7a48), new Vector2(3, 2), 0.05f);
            var rng = new System.Random(3);
            float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

            // The palisade: a ring of sharpened logs round the back, open to the road.
            for (float a = -105f; a <= 105f; a += 6.5f)
            {
                float rad = a * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Sin(rad) * 9.5f, 0, Mathf.Cos(rad) * 9.5f + 1f);
                float h = R(4.2f, 5f);
                var log = Log(yard, foot, foot + Vector3.up * h, 0.32f, rng.NextDouble() < 0.5 ? logs : dark);
                log.AddComponent<CapsuleCollider>();
                var tip = Roof(yard, foot + Vector3.up * h, new Vector3(0.64f, 0.7f, 0.64f), 6, logs);
                if (rng.NextDouble() < 0.3) rig.Breakable(tip.transform, new Vector3(0, -h + 0.3f, R(-1, 1)), new Vector3(R(-80, 80), 0, R(60, 90)));
            }
            // A walkway behind the palisade's top.
            for (int side = -1; side <= 1; side += 2)
                Box(yard, new Vector3(side * 7.2f, 3.2f, 5.5f), new Vector3(0.9f, 0.15f, 7f), "wood", U.Hex(0x7a5e42), euler: new Vector3(0, side * -32f, 0));

            // Two watchtowers flanking the yard.
            foreach (var side in new[] { -1f, 1f })
            {
                var c = new Vector3(side * 6.5f, 0, 3.5f);
                foreach (var (dx, dz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                    Log(yard, c + new Vector3(dx, 0, dz) * 1.1f, c + new Vector3(dx * 0.9f, 6f, dz * 0.9f), 0.17f, dark);
                Box(yard, c + Vector3.up * 6f, new Vector3(2.8f, 0.25f, 2.8f), "wood", U.Hex(0x7a5e42));
                foreach (var (dx, dz, ry) in new[] { (0f, -1.35f, 0f), (0f, 1.35f, 0f), (-1.35f, 0f, 90f), (1.35f, 0f, 90f) })
                    Box(yard, c + new Vector3(dx, 6.6f, dz), new Vector3(2.8f, 1f, 0.12f), "wood", U.Hex(0x86684a), euler: new Vector3(0, ry, 0), collider: false);
                foreach (var (dx, dz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                    Log(yard, c + new Vector3(dx, 6f, dz) * 1.05f, c + new Vector3(dx * 1.05f, 7.9f, dz * 1.05f), 0.08f, dark);
                var roof = Roof(yard, c + Vector3.up * 7.9f, new Vector3(4f, 2.2f, 4f), 4, thatch, 45f);
                rig.Breakable(roof.transform, new Vector3(side * 1.5f, -2.5f, 0.8f), new Vector3(side * 35f, 20f, side * 25f));
                rig.Smoke(c + Vector3.up * 8.5f);
                rig.Burn(c + Vector3.up * 6.4f);
            }

            // The lodge at the back, under a thatched gable.
            Box(yard, new Vector3(0, 1.8f, 8.5f), new Vector3(7f, 3.6f, 4.2f), "wood", U.Hex(0x8a6c4c), 1.5f);
            var gable = Gable(yard, new Vector3(0, 3.6f, 8.5f), new Vector3(7.8f, 2.4f, 5f), thatch);
            rig.Breakable(gable.transform, new Vector3(0.5f, -1.4f, 0), new Vector3(8f, 0, -12f));
            Box(yard, new Vector3(0, 1.1f, 6.38f), new Vector3(1.4f, 2.2f, 0.1f), "wood", U.Hex(0x3a2a1e), collider: false);
            Solid(yard, PrimitiveType.Cube, new Vector3(-2.2f, 2.2f, 6.37f), new Vector3(0.9f, 0.7f, 0.05f), Mats.Lit(U.Hex(0xffc070), 1.8f));
            Solid(yard, PrimitiveType.Cube, new Vector3(2.2f, 2.2f, 6.37f), new Vector3(0.9f, 0.7f, 0.05f), Mats.Lit(U.Hex(0xffc070), 1.8f));
            rig.Smoke(new Vector3(2.5f, 5f, 8.5f));
            rig.Burn(new Vector3(-1.5f, 4f, 8f));
            rig.Burn(new Vector3(2f, 3.5f, 7f));

            // Barrels, a cart of logs, and the signal fire in the yard.
            for (int i = 0; i < 4; i++)
                Solid(yard, PrimitiveType.Cylinder, new Vector3(-4.2f + (i % 2) * 0.8f, 0.5f, 6f + (i / 2) * 0.8f), new Vector3(0.7f, 0.5f, 0.7f), dark, collider: true);
            Fire(yard, new Vector3(0, 0, 4f), rig, 1.2f);
            Banner(yard, new Vector3(-3.2f, 0, 3f), 6f, rig);
            Banner(yard, new Vector3(3.2f, 0, 3f), 6f, rig);
        }

        // --- Quarry dirt: a dwarven forge-hold ----------------------------------------------------------

        static void ForgeHold(Transform yard, BaseRig rig)
        {
            var rockTint = U.Hex(0x8a8580);
            var brass = Mats.Textured("metal", U.Hex(0xc8963c), smoothness: 0.55f, metallic: 0.8f);
            var iron = Mats.Textured("metal", U.Hex(0x4a4e54), smoothness: 0.4f, metallic: 0.6f);
            var ember = Mats.Lit(U.Hex(0xff7a20), 2.4f);

            // A rock face with the hold carved into it: a cut-stone front set in the living rock.
            Box(yard, new Vector3(0, 6f, 9.5f), new Vector3(20f, 12f, 5f), "cliff", rockTint, 3f);
            Box(yard, new Vector3(-8.5f, 4f, 7f), new Vector3(5f, 8f, 4f), "cliff", rockTint * 0.9f, 3f, new Vector3(0, 12, 3));
            Box(yard, new Vector3(8.5f, 4.5f, 7f), new Vector3(5f, 9f, 4f), "cliff", rockTint * 0.92f, 3f, new Vector3(0, -10, -4));
            Box(yard, new Vector3(0, 4.2f, 6.6f), new Vector3(11f, 8.4f, 1f), "stone", U.Hex(0x9a8e80), 1.2f);
            // Steps up to the door.
            for (int i = 0; i < 3; i++)
                Box(yard, new Vector3(0, 0.15f + i * 0.3f, 5.2f + i * 0.4f), new Vector3(6f - i * 0.6f, 0.3f, 1.2f), "stone", U.Hex(0x8a7e72), 1f);

            // The great door: deep in its arch, bound in brass, one leaf standing ajar on the forge's glow.
            Box(yard, new Vector3(0, 2.8f, 6.15f), new Vector3(4.4f, 5.6f, 0.2f), "stone", U.Hex(0x1a1410), collider: false);
            Solid(yard, PrimitiveType.Cube, new Vector3(0, 2.6f, 6.1f), new Vector3(1.2f, 5f, 0.05f), Mats.Lit(U.Hex(0xff6a20), 1.6f));
            foreach (var side in new[] { -1f, 1f })
            {
                var leaf = Box(yard, new Vector3(side * 1.25f, 2.6f, 5.9f), new Vector3(1.9f, 5.2f, 0.25f), "wood", U.Hex(0x5a4030), 1.5f,
                    new Vector3(0, side * -14f, 0));
                for (int b = 0; b < 3; b++)
                    Solid(leaf.transform, PrimitiveType.Cube, new Vector3(0, -0.35f + b * 0.35f, -0.6f), new Vector3(1.05f, 0.05f, 0.4f), brass);
                rig.Breakable(leaf.transform, new Vector3(side * 0.6f, -1.6f, -1.5f), new Vector3(-75f, side * 30f, 0));
            }
            // The arch, with the sigil of the hold over it.
            Box(yard, new Vector3(0, 6.1f, 6.0f), new Vector3(5.6f, 1f, 1.2f), "stone", U.Hex(0xa89a8a), 1f);
            Solid(yard, PrimitiveType.Cylinder, new Vector3(0, 7.5f, 6.0f), new Vector3(2.2f, 0.12f, 2.2f), brass, new Vector3(90, 0, 0));
            Solid(yard, PrimitiveType.Cylinder, new Vector3(0, 7.5f, 5.9f), new Vector3(1.3f, 0.12f, 1.3f), Mats.Lit(U.Hex(0x5effc4), 2f), new Vector3(90, 0, 0));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                Solid(yard, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 1.25f, 7.5f + Mathf.Sin(a) * 1.25f, 5.92f), new Vector3(0.35f, 0.35f, 0.15f), brass, new Vector3(0, 0, i * 45f));
            }

            // Forge windows glowing either side, and the chimneys over all.
            foreach (var side in new[] { -1f, 1f })
            {
                for (int w = 0; w < 2; w++)
                {
                    Solid(yard, PrimitiveType.Cube, new Vector3(side * (3.6f + w * 1.4f), 4.2f, 6.08f), new Vector3(0.7f, 1.4f, 0.05f), ember);
                    Solid(yard, PrimitiveType.Cube, new Vector3(side * (3.6f + w * 1.4f), 4.2f, 6.05f), new Vector3(0.12f, 1.4f, 0.08f), iron);
                }
                var chimney = Solid(yard, PrimitiveType.Cylinder, new Vector3(side * 5.5f, 13f, 9f), new Vector3(1.3f, 3f, 1.3f),
                    Mats.Textured("stone", U.Hex(0x7a6e62), new Vector2(2, 3)));
                var cap = Solid(yard, PrimitiveType.Cylinder, new Vector3(side * 5.5f, 16.1f, 9f), new Vector3(1.7f, 0.15f, 1.7f), iron);
                rig.Breakable(cap.transform, new Vector3(side * 1.5f, -4f, -2f), new Vector3(60, 0, side * 40));
                rig.Chimney(new Vector3(side * 5.5f, 16.4f, 9f));
                rig.Burn(new Vector3(side * 4.2f, 5f, 6f));
                // Steam from a vent low in the rock.
                rig.Steam(new Vector3(side * 8f, 1.2f, 5.2f));
            }
            rig.Flame(new Vector3(0, 2.5f, 6f), 0.6f, Glow(yard, new Vector3(0, 3f, 4.5f), U.Hex(0xff7a30), 10f, 1.5f));

            // An anvil, ore carts and crates in the yard.
            Solid(yard, PrimitiveType.Cube, new Vector3(-3.2f, 0.45f, 3.8f), new Vector3(0.6f, 0.9f, 0.6f), iron, collider: true);
            Solid(yard, PrimitiveType.Cube, new Vector3(-3.2f, 1.0f, 3.8f), new Vector3(1.2f, 0.3f, 0.5f), iron);
            Box(yard, new Vector3(4.5f, 0.6f, 3.5f), new Vector3(1.6f, 1f, 1.1f), "metal", U.Hex(0x5a5e64), 1f, new Vector3(0, 20, 0));
            Solid(yard, PrimitiveType.Sphere, new Vector3(4.5f, 1.15f, 3.5f), new Vector3(1.3f, 0.5f, 0.8f), Mats.Textured("cliff", U.Hex(0x6a5a4a)));
            Box(yard, new Vector3(-5.5f, 0.6f, 5f), new Vector3(1.2f, 1.2f, 1.2f), "wood", Color.white, 1.2f);
            Box(yard, new Vector3(-5.2f, 1.5f, 5.1f), new Vector3(0.8f, 0.6f, 0.8f), "wood", new Color(0.8f, 0.75f, 0.7f), 1f, new Vector3(0, 25, 0));
        }

        // --- Snow: a stone keep ---------------------------------------------------------------------------

        static void Keep(Transform yard, BaseRig rig)
        {
            var stone = U.Hex(0x9a9690);
            var snow = Mats.Lit(U.Hex(0xf2f6fa));
            var iron = Mats.Textured("metal", U.Hex(0x3a3d42), smoothness: 0.35f, metallic: 0.6f);

            void Crenels(Vector3 from, Vector3 to, float y, bool cap)
            {
                float len = Vector3.Distance(from, to);
                int n = Mathf.Max(2, Mathf.RoundToInt(len / 1.2f));
                var dir = (to - from).normalized;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                for (int i = 0; i < n; i += 2)
                {
                    var p = Vector3.Lerp(from, to, (i + 0.5f) / n) + Vector3.up * y;
                    var c = Box(yard, p, new Vector3(0.7f, 0.9f, len / n), "stone", stone, 1f, new Vector3(0, yaw, 0), collider: false);
                    if (cap) Solid(c.transform, PrimitiveType.Cube, new Vector3(0, 0.55f, 0), new Vector3(1.1f, 0.18f, 1.1f), snow);
                    if (i % 4 == 0) rig.Breakable(c.transform, new Vector3(0.6f, -y + 0.4f, -0.8f), new Vector3(40, 30, 70));
                }
            }

            // The keep: a square tower, its walls battered at the foot, lit windows high up.
            Box(yard, new Vector3(0, 7f, 9f), new Vector3(7f, 14f, 7f), "stone", stone, 1.6f);
            Box(yard, new Vector3(0, 0.8f, 9f), new Vector3(7.8f, 1.6f, 7.8f), "stone", stone * 0.9f, 1.6f);
            Box(yard, new Vector3(0, 14.2f, 9f), new Vector3(7.6f, 0.4f, 7.6f), "stone", stone * 1.05f, 1.6f, collider: false);
            Solid(yard, PrimitiveType.Cube, new Vector3(0, 14.45f, 9f), new Vector3(7.5f, 0.12f, 7.5f), snow);
            Crenels(new Vector3(-3.6f, 0, 5.4f), new Vector3(3.6f, 0, 5.4f), 14.9f, true);
            Crenels(new Vector3(-3.6f, 0, 12.6f), new Vector3(3.6f, 0, 12.6f), 14.9f, true);
            Crenels(new Vector3(-3.6f, 0, 5.4f), new Vector3(-3.6f, 0, 12.6f), 14.9f, true);
            Crenels(new Vector3(3.6f, 0, 5.4f), new Vector3(3.6f, 0, 12.6f), 14.9f, true);
            foreach (var (x, y) in new[] { (-1.6f, 10.5f), (1.6f, 10.5f), (0f, 7f) })
            {
                Solid(yard, PrimitiveType.Cube, new Vector3(x, y, 5.47f), new Vector3(0.5f, 1.3f, 0.06f), Mats.Lit(U.Hex(0xffcf7a), 2f));
                Solid(yard, PrimitiveType.Cube, new Vector3(x, y + 0.75f, 5.45f), new Vector3(0.9f, 0.15f, 0.25f), snow);
            }
            // A banded door at the foot, and a banner on the roof.
            Box(yard, new Vector3(0, 1.7f, 5.47f), new Vector3(2f, 3.2f, 0.12f), "wood", U.Hex(0x5a4030), 1.2f, collider: false);
            for (int b = 0; b < 3; b++)
                Solid(yard, PrimitiveType.Cube, new Vector3(0, 0.6f + b * 1.1f, 5.4f), new Vector3(2.05f, 0.1f, 0.05f), iron);
            Banner(yard, new Vector3(0, 14.4f, 9f), 5f, rig);
            rig.Smoke(new Vector3(1.5f, 15f, 10f));
            rig.Burn(new Vector3(-2f, 14.6f, 7f));
            rig.Burn(new Vector3(2.5f, 9f, 5.6f));

            // The curtain wall, angled forward from the keep's shoulders, with towers at its ends.
            foreach (var side in new[] { -1f, 1f })
            {
                var inner = new Vector3(side * 3.6f, 0, 6.5f);
                var outer = new Vector3(side * 10f, 0, 3.5f);
                var mid = (inner + outer) / 2;
                float len = Vector3.Distance(inner, outer);
                float yaw = Mathf.Atan2(outer.x - inner.x, outer.z - inner.z) * Mathf.Rad2Deg;
                Box(yard, mid + Vector3.up * 2.75f, new Vector3(1.4f, 5.5f, len), "stone", stone * 0.95f, 1.4f, new Vector3(0, yaw, 0));
                Solid(yard, PrimitiveType.Cube, mid + Vector3.up * 5.55f, new Vector3(1.5f, 0.12f, len), snow, new Vector3(0, yaw, 0));
                Crenels(inner, outer, 6.1f, true);
                var tower = Solid(yard, PrimitiveType.Cylinder, outer + Vector3.up * 3.5f, new Vector3(3f, 3.5f, 3f),
                    Mats.Textured("stone", stone, new Vector2(4, 4)), collider: true);
                var roof = Roof(yard, outer + Vector3.up * 7f, new Vector3(3.6f, 3f, 3.6f), 10, Mats.Textured("stone", U.Hex(0x5a6070), new Vector2(3, 2)));
                Roof(yard, outer + Vector3.up * 8.3f, new Vector3(1.9f, 1.75f, 1.9f), 10, snow);
                rig.Breakable(roof.transform, new Vector3(side * 1.8f, -3f, -1f), new Vector3(30, 0, side * 50));
                rig.Smoke(outer + Vector3.up * 8f);
                rig.Burn(outer + Vector3.up * 5f);
                // Braziers before the gate, warm against the snow.
                Fire(yard, new Vector3(side * 3.2f, 0, 3f), rig, 0.9f);
            }
        }

        // --- Canyon: an adobe citadel ----------------------------------------------------------------------

        static void Citadel(Transform yard, BaseRig rig)
        {
            var clay = U.Hex(0xfff0dc);
            var clayDark = U.Hex(0xe8d0b0);
            var beam = Mats.Textured("wood", U.Hex(0x6a4a30), new Vector2(1, 2));
            var rng = new System.Random(5);
            float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();

            GameObject Block(Vector3 at, Vector3 size, Color tint)
            {
                // Plastered: the ground texture at a broad tiling reads as smoothed mud over brick.
                var b = Box(yard, at, size, "ground_canyon", tint, 4f, smooth: 0.05f);
                // Beam ends (vigas) along the top of each block's front.
                int n = Mathf.Max(2, Mathf.RoundToInt(size.x / 1.3f));
                for (int i = 0; i < n; i++)
                {
                    float x = at.x - size.x / 2 + (i + 0.5f) * size.x / n;
                    float y = at.y + size.y / 2 - 0.45f;
                    Log(yard, new Vector3(x, y, at.z - size.z / 2 + 0.2f), new Vector3(x, y + R(-0.04f, 0.04f), at.z - size.z / 2 - 0.6f), 0.12f, beam);
                }
                return b;
            }

            // Stepped blocks, each set back on the one below, as an adobe town climbs.
            Block(new Vector3(0, 2.2f, 8.5f), new Vector3(13f, 4.4f, 6f), clay);
            Block(new Vector3(-1.5f, 5.6f, 9.5f), new Vector3(8f, 2.4f, 4.5f), clayDark);
            var top = Block(new Vector3(-2.5f, 7.9f, 10f), new Vector3(4.2f, 2.2f, 3.5f), clay);
            rig.Breakable(top.transform, new Vector3(1f, -2.5f, -1f), new Vector3(10, 20, 18));
            Block(new Vector3(-7.5f, 1.6f, 6.5f), new Vector3(4f, 3.2f, 4f), clayDark);
            Block(new Vector3(7.2f, 1.4f, 6.2f), new Vector3(3.6f, 2.8f, 4f), clay);
            // Parapets round the roofs, scalloped.
            for (int i = 0; i < 6; i++)
            {
                var p = Box(yard, new Vector3(-5.8f + i * 2.3f, 4.75f, 5.6f), new Vector3(1.4f, 0.7f, 0.3f), "ground_canyon", clay, 4f, collider: false);
                if (i % 2 == 0) rig.Breakable(p.transform, new Vector3(0, -4.4f, -0.8f), new Vector3(80, 0, 20));
            }

            // The domed tower on the right, tallest thing in the canyon.
            var tower = Box(yard, new Vector3(4.5f, 5.5f, 10f), new Vector3(3.2f, 11f, 3.2f), "ground_canyon", clayDark, 4f);
            var dome = Solid(yard, PrimitiveType.Sphere, new Vector3(4.5f, 11f, 10f), new Vector3(3.4f, 3f, 3.4f), Mats.Textured("ground_canyon", U.Hex(0xe8c090), new Vector2(2, 2), 0.1f));
            var finial = Solid(yard, PrimitiveType.Sphere, new Vector3(4.5f, 12.7f, 10f), Vector3.one * 0.5f, Mats.Lit(U.Hex(0x5effc4), 2f));
            rig.Breakable(dome.transform, new Vector3(1.5f, -2.5f, -1f), new Vector3(30, 0, 40));
            rig.Breakable(finial.transform, new Vector3(2.5f, -11f, -3f), new Vector3(0, 0, 0));
            foreach (var y in new[] { 6.5f, 8.8f })
                Solid(yard, PrimitiveType.Cube, new Vector3(4.5f, y, 8.37f), new Vector3(0.45f, 1f, 0.06f), Mats.Lit(U.Hex(0xffc070), 1.8f));

            // A dark doorway under a striped awning, lanterns either side.
            Box(yard, new Vector3(0, 1.4f, 5.47f), new Vector3(2.2f, 2.8f, 0.1f), "stone", U.Hex(0x2a1c14), collider: false);
            Log(yard, new Vector3(-1.4f, 2.95f, 5.4f), new Vector3(1.4f, 2.95f, 5.4f), 0.14f, beam);
            for (int s = 0; s < 5; s++)
            {
                var stripe = Solid(yard, PrimitiveType.Cube, new Vector3(-1.6f + s * 0.8f, 3.25f, 4.6f), new Vector3(0.8f, 0.05f, 1.8f),
                    Mats.Lit(s % 2 == 0 ? U.Hex(0xb8402a) : U.Hex(0xe8d8b0)), new Vector3(-18, 0, 0));
                rig.Wave(stripe.transform);
            }
            foreach (var side in new[] { -1f, 1f })
            {
                Log(yard, new Vector3(side * 2.2f, 0, 4.4f), new Vector3(side * 2.2f, 2.9f, 4.4f), 0.08f, beam);
                var lamp = Solid(yard, PrimitiveType.Cube, new Vector3(side * 2.2f, 3.1f, 4.4f), new Vector3(0.35f, 0.45f, 0.35f), Mats.Lit(U.Hex(0xffb050), 2.5f));
                Glow(yard, new Vector3(side * 2.2f, 3.2f, 4f), U.Hex(0xffa050), 6f, 1.2f);
            }
            // Pots and a cloth-covered stall in the yard.
            for (int i = 0; i < 5; i++)
                Solid(yard, PrimitiveType.Sphere, new Vector3(-5f + (i % 3) * 0.7f, 0.4f, 4.2f + (i / 3) * 0.7f), new Vector3(0.65f, 0.8f, 0.65f),
                    Mats.Textured("ground_canyon", U.Hex(0xa86a42), smoothness: 0.2f), collider: true);
            Fire(yard, new Vector3(5f, 0, 3.6f), rig, 0.8f);
            rig.Smoke(new Vector3(-2.5f, 9.2f, 10f));
            rig.Smoke(new Vector3(4.5f, 12.5f, 10f));
            rig.Burn(new Vector3(-1f, 4.6f, 7f));
            rig.Burn(new Vector3(-7f, 3.4f, 6f));
        }
    }

    /// <summary>
    /// The base's moving parts: its fires and smoke, banners and awnings in the wind, the shield dome,
    /// and its damage, which follows the base's health. Smoke rises below two thirds; below a third the
    /// fires catch and the breakable parts fall, and a new round puts everything back.
    /// </summary>
    public sealed class BaseRig : MonoBehaviour
    {
        /// <summary>Every rig on the map: one, or two for a base built in halves.</summary>
        public static readonly List<BaseRig> All = new List<BaseRig>();

        public static void HitAll(bool shielded)
        {
            foreach (var r in All) if (r != null) r.Hit(shielded);
        }

        void OnDestroy() => All.Remove(this);

        sealed class Part { public Transform T; public Vector3 Pos, Euler, BrokenPos, BrokenEuler; public float Delay; }
        readonly List<Part> parts = new List<Part>();
        readonly List<Vector3> smoke = new List<Vector3>(), burn = new List<Vector3>(), chimneys = new List<Vector3>(), steam = new List<Vector3>();
        readonly List<(Vector3 at, float size, Light light)> flames = new List<(Vector3, float, Light)>();
        readonly List<Transform> waving = new List<Transform>();
        readonly List<Light> fireLights = new List<Light>();
        GameObject dome;
        Material domeMaterial;
        float domeFlash, ruin;

        public void Breakable(Transform t, Vector3 drop, Vector3 turn) => parts.Add(new Part
        {
            T = t, Pos = t.localPosition, Euler = t.localEulerAngles, BrokenPos = t.localPosition + drop, BrokenEuler = t.localEulerAngles + turn,
            Delay = Random.Range(0f, 0.6f),
        });

        public void Smoke(Vector3 local) => smoke.Add(local);
        public void Burn(Vector3 local) => burn.Add(local);
        public void Chimney(Vector3 local) => chimneys.Add(local);
        public void Steam(Vector3 local) => steam.Add(local);
        public void Wave(Transform t) => waving.Add(t);
        public void Flame(Vector3 local, float size, Light light) => flames.Add((local, size, light));

        public void Init()
        {
            All.Add(this);
            // Fires that catch as the base burns: lit, but out until they're needed.
            foreach (var b in burn)
            {
                var l = new GameObject("Burning").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.transform.localPosition = b + Vector3.up * 0.8f;
                l.color = U.Hex(0xff6a20);
                l.range = 8f;
                l.intensity = 0;
                l.shadows = LightShadows.None;
                fireLights.Add(l);
            }
            // The shield: a faint blue dome over the whole base.
            dome = Shapes.Prim(PrimitiveType.Sphere, transform, new Vector3(0, 0, 6f), new Vector3(24f, 20f, 24f), Mats.Unlit(Color.white));
            dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            domeMaterial = new Material(Mats.Unlit(Color.white));
            dome.GetComponent<Renderer>().sharedMaterial = domeMaterial;
            dome.SetActive(false);
        }

        /// <summary>A breach: the dome flares if a shield layer took it; otherwise the base shudders.</summary>
        public void Hit(bool shielded)
        {
            if (shielded) domeFlash = 1f;
            else Combat.Shake(transform.position, 0.25f, 30f);
        }

        Vector3 World(Vector3 local) => transform.TransformPoint(local);

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            float health = gm.MaxBaseHealth > 0 ? gm.BaseHealth / (float)gm.MaxBaseHealth : 1f;
            float t = Time.time;

            // The base's fires: always lit, flickering.
            foreach (var (at, size, light) in flames)
            {
                light.intensity = 1.3f + Mathf.PerlinNoise(at.x, t * 5f) * 0.9f;
                if (Random.value < 0.35f * size)
                    Fx.Puff(World(at) + Random.insideUnitSphere * 0.2f * size, Vector3.up * Random.Range(1f, 2f) * size, Random.Range(0.18f, 0.35f) * size,
                        new Color(1f, Random.Range(0.4f, 0.75f), 0.15f, 0.75f), 0.4f);
            }
            foreach (var c in chimneys)
                if (Random.value < 0.12f)
                    Fx.Puff(World(c), Vector3.up * Random.Range(1.2f, 2f) + Vector3.right * 0.4f, Random.Range(0.6f, 1f), new Color(0.35f, 0.33f, 0.32f, 0.45f), 2.5f);
            foreach (var s in steam)
                if (Mathf.Repeat(t + s.x, 5f) < 0.6f && Random.value < 0.5f)
                    Fx.Puff(World(s), Vector3.up * Random.Range(2f, 3f) + Random.insideUnitSphere * 0.4f, Random.Range(0.3f, 0.5f), new Color(0.9f, 0.92f, 0.95f, 0.4f), 1.2f);
            foreach (var w in waving)
                w.localRotation = Quaternion.Euler(w.localEulerAngles.x, w.localEulerAngles.y, Mathf.Sin(t * 2.3f + w.localPosition.x) * 6f);

            // Damage: smoke first, then fire and falling parts. A new round (health back up) restores it.
            if (health < 0.67f)
                foreach (var s in smoke)
                    if (Random.value < (0.67f - health) * 0.25f)
                        Fx.Puff(World(s), Vector3.up * Random.Range(1.5f, 2.5f) + Random.insideUnitSphere * 0.3f, Random.Range(0.7f, 1.2f),
                            new Color(0.12f, 0.11f, 0.1f, 0.55f), 3f);
            bool burning = health < 0.34f;
            for (int i = 0; i < burn.Count; i++)
            {
                fireLights[i].intensity = burning ? 1.4f + Mathf.PerlinNoise(i, t * 6f) * 1.2f : 0f;
                if (burning && Random.value < 0.45f)
                    Fx.Puff(World(burn[i]) + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(1.5f, 3f), Random.Range(0.3f, 0.6f),
                        new Color(1f, Random.Range(0.35f, 0.65f), 0.1f, 0.8f), 0.5f);
            }
            ruin = Mathf.MoveTowards(ruin, burning ? 1f : 0f, Time.deltaTime * (burning ? 0.8f : 3f));
            foreach (var p in parts)
            {
                float k = Mathf.Clamp01((ruin - p.Delay * 0.5f) / 0.5f);
                k = k * k;
                p.T.localPosition = Vector3.Lerp(p.Pos, p.BrokenPos, k);
                p.T.localRotation = Quaternion.Euler(Vector3.Lerp(p.Euler, p.BrokenEuler, k));
            }

            // The shield: faint while layers are up, flaring when one takes a breach.
            bool shields = gm.MaxShields > 0;
            if (dome.activeSelf != shields) dome.SetActive(shields);
            if (shields)
            {
                domeFlash = Mathf.MoveTowards(domeFlash, 0f, Time.deltaTime * 2.5f);
                float up = gm.Shields / (float)gm.MaxShields;
                float a = 0.02f * up + 0.3f * domeFlash + (gm.Phase == Phase.Combat ? 0.01f * Mathf.Sin(t * 3f) : 0f);
                domeMaterial.color = new Color(0.48f, 0.82f, 1f, Mathf.Max(0, a));
            }
        }
    }
}
