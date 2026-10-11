using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>The moving parts of a tower, for its firing to drive.</summary>
    public sealed class TowerRig
    {
        /// <summary>Turns to face the target.</summary>
        public Transform Yaw;
        /// <summary>Tilts the weapon: up to lob a shell, down onto a close orc. Null for towers that fire all around.</summary>
        public Transform Pitch;
        /// <summary>The Machine Gun's barrel cluster, spinning about its own axis.</summary>
        public Transform Spinner;
        /// <summary>The Cannon's barrel, sliding back on a shot.</summary>
        public Transform Recoil;
        /// <summary>The Arc Tower's toroid, turning slowly.</summary>
        public Transform Coil;
        /// <summary>Where shots, beams, bolts and flame leave.</summary>
        public Transform Muzzle;
        /// <summary>Where spent cases fly out, for the Machine Gun.</summary>
        public Transform Ejector;
        /// <summary>The part that glows: the Laser's crystal, the Arc's crystal, the Flame Thrower's pilot light.</summary>
        public Renderer Glow;
        public Color GlowColor;
    }

    /// <summary>
    /// The five towers as built things, each shaped by how it fights (after
    /// Orc Horde's tower briefs): a rotary gun with a belt feed, a squat cannon
    /// on trunnions that elevates to lob, a crystal emitter that charges, a coil
    /// that throws arcs all around, a fuel-fed nozzle with a pilot flame.
    ///
    /// Built from the same solids as the orcs; meshes are shared per tower type.
    /// </summary>
    public static class TowerModels
    {
        static Material material;
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        static readonly Color Iron = new Color(0.2f, 0.21f, 0.23f), Steel = new Color(0.55f, 0.57f, 0.6f),
            Brass = new Color(0.78f, 0.6f, 0.28f), Copper = new Color(0.74f, 0.4f, 0.22f), Bronze = new Color(0.62f, 0.42f, 0.2f),
            Stone = new Color(0.46f, 0.44f, 0.41f), Dark = new Color(0.06f, 0.06f, 0.07f);

        /// <summary>Metal-detailed props in the orc shader: vertex colour, glow and finish per vertex.</summary>
        public static Material Material
        {
            get
            {
                if (material != null) return material;
                material = new Material(Shader.Find("OrcHorde/Orc"));
                material.SetTexture("_Detail", Mats.Tex("metal"));
                material.SetTexture("_DetailNormal", Mats.Tex("metal_n"));
                material.SetFloat("_DetailStrength", 0.4f);
                return material;
            }
        }

        static Transform Node(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        /// <summary>One mesh part under a node, built once per key and shared.</summary>
        static Renderer Part(Transform parent, string key, Action<OrcMesh> draw, bool shadows = true)
        {
            if (!meshes.TryGetValue(key, out var mesh))
            {
                var m = new OrcMesh();
                draw(m);
                mesh = meshes[key] = m.Build(key);
            }
            var go = new GameObject(key);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        public static TowerRig Build(TowerDef def, Transform root)
        {
            var rig = new TowerRig();
            float r = def.Footprint;
            Part(root, "base-" + def.Id, m => Base(m, r, def.Color));
            rig.Yaw = Node(root, "Yaw", new Vector3(0, 0.42f, 0));
            switch (def.Id)
            {
                case "machine_gun": MachineGun(rig); break;
                case "cannon": Cannon(rig); break;
                case "laser": Laser(rig); break;
                case "arc_tower": Arc(rig); break;
                default: Flame(rig); break;
            }
            // The gun and the emitter are slender; scaled up so they hold their own on the plinth.
            if (def.Id == "machine_gun") rig.Yaw.localScale = Vector3.one * 1.25f;
            if (def.Id == "laser") rig.Yaw.localScale = Vector3.one * 1.15f;
            return rig;
        }

        /// <summary>A stone plinth with an iron rim and bolts, and a turntable on top, trimmed in the tower's colour.</summary>
        static void Base(OrcMesh m, float r, Color accent)
        {
            m.Paint(0, Stone, 0.1f).Cylinder(Vector3.zero, new Vector3(0, 0.22f, 0), r * 0.95f, 8);
            m.Paint(0, Iron, 0.45f).Cylinder(new Vector3(0, 0.2f, 0), new Vector3(0, 0.3f, 0), r * 0.82f, 24);
            m.Paint(0, accent * 0.8f, 0.3f).Torus(new Vector3(0, 0.3f, 0), Vector3.up, r * 0.78f, 0.03f, 32, 6);
            m.Paint(0, Steel, 0.55f);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2;
                m.Sphere(new Vector3(Mathf.Cos(a) * r * 0.88f, 0.23f, Mathf.Sin(a) * r * 0.88f), 0.035f, 6, 4);
            }
            m.Paint(0, Iron * 1.3f, 0.5f).Cylinder(new Vector3(0, 0.3f, 0), new Vector3(0, 0.42f, 0), r * 0.55f, 24);
        }

        // -------------------------------------------------------------- machine gun

        /// <summary>
        /// Rotary machine gun: three barrels in a slotted cooling jacket that spin
        /// up as it fires, fed by a brass belt from the ammunition box on its left,
        /// behind a riveted mantlet, worked by spade grips at the rear.
        /// </summary>
        static void MachineGun(TowerRig rig)
        {
            var olive = new Color(0.36f, 0.38f, 0.2f);
            Part(rig.Yaw, "mg-mount", m =>
            {
                m.Paint(0, Iron, 0.45f).Cylinder(Vector3.zero, new Vector3(0, 0.45f, 0), 0.15f, 12);
                m.Paint(0, olive, 0.3f).Box(new Vector3(0, 0.62f, 0.3f), new Vector3(0.78f, 0.5f, 0.05f), Quaternion.Euler(-12, 0, 0));
                m.Paint(0, olive * 0.85f, 0.3f);
                foreach (var s in new[] { -1f, 1f })
                    m.Box(new Vector3(s * 0.4f, 0.6f, 0.18f), new Vector3(0.05f, 0.42f, 0.28f), Quaternion.Euler(-8, s * -20, 0));
                m.Paint(0, Brass, 0.55f);
                foreach (var x in new[] { -0.32f, 0f, 0.32f })
                foreach (var y in new[] { 0.45f, 0.78f })
                    m.Sphere(new Vector3(x, y, 0.33f + (y - 0.6f) * -0.2f), 0.02f, 6, 4);
                // Ammunition box on the left, with its lid catch.
                m.Paint(0, olive, 0.25f).Box(new Vector3(-0.42f, 0.38f, -0.05f), new Vector3(0.22f, 0.3f, 0.36f), Quaternion.identity);
                m.Paint(0, Brass, 0.5f).Box(new Vector3(-0.42f, 0.54f, -0.05f), new Vector3(0.23f, 0.03f, 0.37f), Quaternion.identity);
                m.Paint(0, Steel, 0.5f).Box(new Vector3(-0.3f, 0.4f, -0.05f), new Vector3(0.02f, 0.08f, 0.05f), Quaternion.identity);
            });
            rig.Pitch = Node(rig.Yaw, "Pitch", new Vector3(0, 0.6f, 0));
            Part(rig.Pitch, "mg-receiver", m =>
            {
                m.Paint(0, Iron, 0.5f).Box(new Vector3(0, 0, -0.05f), new Vector3(0.24f, 0.22f, 0.5f), Quaternion.identity);
                m.Paint(0, olive, 0.3f).Box(new Vector3(0, 0.13f, -0.05f), new Vector3(0.2f, 0.05f, 0.44f), Quaternion.identity);
                // The belt: brass rounds in links, from the box up and over into the feed.
                m.Paint(0, Brass, 0.6f);
                for (int i = 0; i < 8; i++)
                {
                    float t = i / 7f;
                    var p = Vector3.Lerp(new Vector3(-0.38f, -0.06f, -0.05f), new Vector3(-0.13f, 0.02f, 0.02f), t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.08f;
                    m.Capsule(p + new Vector3(0, 0, -0.04f), p + new Vector3(0, 0, 0.04f), 0.014f, 0.01f, 6, 1);
                }
                // Spade grips and the trigger bar at the back.
                m.Paint(0, Iron, 0.45f);
                foreach (var s in new[] { -1f, 1f })
                {
                    m.Capsule(new Vector3(s * 0.09f, 0, -0.3f), new Vector3(s * 0.12f, -0.02f, -0.45f), 0.018f, 0.018f, 6, 1);
                    m.Paint(0, new Color(0.3f, 0.2f, 0.12f), 0.2f).Capsule(new Vector3(s * 0.12f, -0.08f, -0.46f), new Vector3(s * 0.12f, 0.05f, -0.46f), 0.025f, 0.025f, 8, 2);
                    m.Paint(0, Iron, 0.45f);
                }
                m.Capsule(new Vector3(-0.1f, 0.06f, -0.45f), new Vector3(0.1f, 0.06f, -0.45f), 0.012f, 0.012f, 6, 1);
            });
            rig.Spinner = Node(rig.Pitch, "Barrels", new Vector3(0, 0, 0.2f));
            Part(rig.Spinner, "mg-barrels", m =>
            {
                m.Paint(0, Iron * 0.8f, 0.55f);
                for (int i = 0; i < 3; i++)
                {
                    float a = i / 3f * Mathf.PI * 2;
                    var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.055f;
                    m.Cylinder(o, o + new Vector3(0, 0, 0.82f), 0.024f, 8);
                }
                // The slotted cooling jacket: olive rings with gaps between them.
                m.Paint(0, olive, 0.3f);
                for (int i = 0; i < 6; i++)
                    m.Cylinder(new Vector3(0, 0, 0.04f + i * 0.09f), new Vector3(0, 0, 0.1f + i * 0.09f), 0.11f, 16);
                m.Paint(0, Brass, 0.55f).Cylinder(new Vector3(0, 0, 0.6f), new Vector3(0, 0, 0.63f), 0.1f, 16);
                m.Torus(new Vector3(0, 0, 0.82f), Vector3.forward, 0.08f, 0.015f, 16, 6);
            });
            rig.Muzzle = Node(rig.Spinner, "Muzzle", new Vector3(0, 0, 0.86f));
            rig.Ejector = Node(rig.Pitch, "Ejector", new Vector3(0.14f, 0, -0.02f));
        }

        // -------------------------------------------------------------- cannon

        /// <summary>
        /// Squat iron cannon on trunnions between riveted cheeks: one thick barrel
        /// with bronze bands, which elevates to lob its shell over walls and slides
        /// back in its cradle on every shot.
        /// </summary>
        static void Cannon(TowerRig rig)
        {
            var blue = new Color(0.32f, 0.42f, 0.52f);
            Part(rig.Yaw, "cannon-carriage", m =>
            {
                m.Paint(0, blue, 0.3f).Cylinder(Vector3.zero, new Vector3(0, 0.12f, 0), 0.6f, 24);
                foreach (var s in new[] { -1f, 1f })
                {
                    m.Paint(0, blue, 0.3f).Box(new Vector3(s * 0.27f, 0.36f, -0.02f), new Vector3(0.09f, 0.5f, 0.62f), Quaternion.identity);
                    m.Paint(0, Bronze, 0.5f).Box(new Vector3(s * 0.27f, 0.62f, -0.02f), new Vector3(0.1f, 0.03f, 0.64f), Quaternion.identity);
                    m.Paint(0, Steel, 0.55f);
                    foreach (var z in new[] { -0.25f, 0f, 0.22f })
                        m.Sphere(new Vector3(s * 0.32f, 0.3f, z), 0.022f, 6, 4);
                }
                m.Paint(0, Iron, 0.4f).Box(new Vector3(0, 0.15f, -0.38f), new Vector3(0.5f, 0.1f, 0.18f), Quaternion.identity);
            });
            rig.Pitch = Node(rig.Yaw, "Pitch", new Vector3(0, 0.55f, 0));
            Part(rig.Pitch, "cannon-trunnions", m =>
            {
                m.Paint(0, Bronze, 0.5f);
                foreach (var s in new[] { -1f, 1f }) m.Cylinder(new Vector3(s * 0.18f, 0, 0), new Vector3(s * 0.25f, 0, 0), 0.07f, 12);
            });
            rig.Recoil = Node(rig.Pitch, "Barrel", Vector3.zero);
            Part(rig.Recoil, "cannon-barrel", m =>
            {
                m.Paint(0, Iron, 0.45f).Capsule(new Vector3(0, 0, -0.32f), new Vector3(0, 0, 0.68f), 0.2f, 0.15f, 18, 4);
                m.Paint(0, Iron * 1.2f, 0.5f).Sphere(new Vector3(0, 0, -0.5f), 0.08f, 10, 6);
                m.Paint(0, Bronze, 0.55f);
                foreach (var z in new[] { -0.1f, 0.25f, 0.58f }) m.Torus(new Vector3(0, 0, z), Vector3.forward, 0.18f - z * 0.04f, 0.025f, 20, 6);
                m.Paint(0, Iron * 1.1f, 0.45f).Cylinder(new Vector3(0, 0, 0.7f), new Vector3(0, 0, 0.84f), 0.19f, 18);
                m.Paint(0, Dark).Cylinder(new Vector3(0, 0, 0.84f), new Vector3(0, 0, 0.845f), 0.11f, 16);
            });
            rig.Muzzle = Node(rig.Recoil, "Muzzle", new Vector3(0, 0, 0.9f));
        }

        // -------------------------------------------------------------- laser

        /// <summary>
        /// Crystal emitter: an emerald focusing crystal caged in a long steel body
        /// with brass collars and lens rings, on a finned coolant column. The
        /// crystal brightens as it charges and flares when the beam goes.
        /// </summary>
        static void Laser(TowerRig rig)
        {
            var teal = new Color(0.17f, 0.45f, 0.42f);
            var emerald = new Color(0.3f, 1f, 0.6f);
            Part(rig.Yaw, "laser-column", m =>
            {
                m.Paint(0, teal, 0.35f).Cylinder(Vector3.zero, new Vector3(0, 0.55f, 0), 0.13f, 16);
                m.Paint(0, Steel, 0.5f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * 360f;
                    var q = Quaternion.Euler(0, a, 0);
                    m.Box(q * new Vector3(0, 0.28f, 0.18f), new Vector3(0.015f, 0.42f, 0.12f), q);
                }
                m.Paint(0, Brass, 0.55f).Torus(new Vector3(0, 0.56f, 0), Vector3.up, 0.13f, 0.025f, 16, 6);
                foreach (var s in new[] { -1f, 1f })
                    m.Paint(0, teal * 0.9f, 0.35f).Box(new Vector3(s * 0.14f, 0.7f, 0), new Vector3(0.04f, 0.3f, 0.16f), Quaternion.identity);
            });
            rig.Pitch = Node(rig.Yaw, "Pitch", new Vector3(0, 0.78f, 0));
            Part(rig.Pitch, "laser-emitter", m =>
            {
                m.Paint(0, Steel, 0.6f).Cylinder(new Vector3(0, 0, -0.45f), new Vector3(0, 0, -0.18f), 0.12f, 18);
                m.Cylinder(new Vector3(0, 0, 0.12f), new Vector3(0, 0, 0.42f), 0.1f, 18);
                m.Paint(0, Brass, 0.6f);
                foreach (var z in new[] { -0.18f, 0.12f }) m.Torus(new Vector3(0, 0, z), Vector3.forward, 0.12f, 0.025f, 18, 6);
                // The cage the crystal sits in, between the two halves of the body.
                m.Paint(0, Brass * 0.9f, 0.55f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i / 4f * Mathf.PI * 2 + Mathf.PI / 4;
                    var o = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.11f;
                    m.Capsule(o + new Vector3(0, 0, -0.18f), o + new Vector3(0, 0, 0.12f), 0.012f, 0.012f, 6, 1);
                }
                // Lens rings stepping down toward the aperture.
                for (int i = 0; i < 3; i++) m.Torus(new Vector3(0, 0, 0.5f + i * 0.11f), Vector3.forward, 0.09f - i * 0.015f, 0.018f, 16, 6);
                m.Paint(0, Steel * 0.8f, 0.55f).Capsule(new Vector3(0, -0.12f, -0.3f), new Vector3(0, -0.12f, 0.3f), 0.02f, 0.02f, 6, 1);
            });
            rig.Glow = Part(rig.Pitch, "laser-crystal", m =>
            {
                m.Paint(0, emerald, 0.8f, 1f).Ellipsoid(new Vector3(0, 0, -0.03f), new Vector3(0.07f, 0.07f, 0.17f), Quaternion.Euler(0, 0, 45), 6, 6);
                m.Ellipsoid(new Vector3(0, 0, 0.78f), new Vector3(0.05f, 0.05f, 0.012f), Quaternion.identity, 12, 4);
            }, shadows: false);
            rig.GlowColor = emerald;
            rig.Muzzle = Node(rig.Pitch, "Muzzle", new Vector3(0, 0, 0.82f));
        }

        // -------------------------------------------------------------- arc

        /// <summary>
        /// A coil, not a gun: copper windings up a stone-and-iron column to an
        /// amethyst crystal, three copper prongs around it, a toroid turning on top.
        /// It fires all around at once, so it has nothing to aim.
        /// </summary>
        static void Arc(TowerRig rig)
        {
            var violet = new Color(0.4f, 0.36f, 0.48f);
            var amethyst = new Color(0.72f, 0.45f, 1f);
            Part(rig.Yaw, "arc-coil", m =>
            {
                m.Paint(0, violet, 0.3f).Cylinder(Vector3.zero, new Vector3(0, 1.05f, 0), 0.13f, 16);
                m.Paint(0, Copper, 0.6f);
                for (int i = 0; i < 9; i++) m.Torus(new Vector3(0, 0.25f + i * 0.085f, 0), Vector3.up, 0.15f, 0.022f, 18, 6);
                m.Paint(0, Iron, 0.45f).Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.12f, 0), 0.32f, 18);
                for (int i = 0; i < 3; i++)
                {
                    float a = i / 3f * Mathf.PI * 2;
                    var basePt = new Vector3(Mathf.Cos(a) * 0.42f, 0.08f, Mathf.Sin(a) * 0.42f);
                    var mid = new Vector3(Mathf.Cos(a) * 0.4f, 0.75f, Mathf.Sin(a) * 0.4f);
                    var tip = new Vector3(Mathf.Cos(a) * 0.24f, 1.2f, Mathf.Sin(a) * 0.24f);
                    m.Paint(0, violet * 0.8f, 0.3f).Box(basePt + Vector3.up * 0.04f, new Vector3(0.14f, 0.1f, 0.14f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0));
                    m.Paint(0, Copper, 0.6f).Capsule(basePt, mid, 0.03f, 0.026f, 8, 1);
                    m.Capsule(mid, tip, 0.026f, 0.018f, 8, 1);
                    m.Sphere(tip, 0.04f, 8, 6);
                }
            });
            rig.Coil = Node(rig.Yaw, "Toroid", new Vector3(0, 1.38f, 0));
            Part(rig.Coil, "arc-toroid", m =>
            {
                m.Paint(0, Steel, 0.65f).Torus(Vector3.zero, Vector3.up, 0.26f, 0.065f, 28, 10);
                m.Paint(0, Copper, 0.6f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i / 4f * Mathf.PI * 2;
                    m.Capsule(Vector3.zero, new Vector3(Mathf.Cos(a) * 0.22f, 0, Mathf.Sin(a) * 0.22f), 0.015f, 0.015f, 6, 1);
                }
            });
            rig.Glow = Part(rig.Yaw, "arc-crystal", m =>
                m.Paint(0, amethyst, 0.8f, 1f).Ellipsoid(new Vector3(0, 1.18f, 0), new Vector3(0.11f, 0.2f, 0.11f), Quaternion.Euler(0, 30, 0), 6, 6),
                shadows: false);
            rig.GlowColor = amethyst;
            rig.Muzzle = Node(rig.Yaw, "Muzzle", new Vector3(0, 1.2f, 0));
        }

        // -------------------------------------------------------------- flame

        /// <summary>
        /// Two brass fuel tanks with a pressure gauge, copper feed pipes, and a
        /// short wide nozzle with a pilot flame burning beneath its lip.
        /// </summary>
        static void Flame(TowerRig rig)
        {
            var burnt = new Color(0.62f, 0.3f, 0.14f);
            var pilot = new Color(1f, 0.55f, 0.15f);
            Part(rig.Yaw, "flame-tanks", m =>
            {
                foreach (var s in new[] { -1f, 1f })
                {
                    var x = s * 0.3f;
                    m.Paint(0, Brass, 0.55f).Cylinder(new Vector3(x, 0.05f, -0.1f), new Vector3(x, 0.7f, -0.1f), 0.15f, 18);
                    m.Sphere(new Vector3(x, 0.7f, -0.1f), 0.15f, 14, 8);
                    m.Paint(0, burnt, 0.35f);
                    foreach (var y in new[] { 0.2f, 0.5f }) m.Torus(new Vector3(x, y, -0.1f), Vector3.up, 0.152f, 0.018f, 18, 6);
                    // Feed pipes, up off the tank tops and in to the back of the nozzle.
                    m.Paint(0, Copper, 0.6f).Capsule(new Vector3(x, 0.82f, -0.1f), new Vector3(x * 0.5f, 0.92f, -0.12f), 0.025f, 0.025f, 8, 1);
                    m.Capsule(new Vector3(x * 0.5f, 0.92f, -0.12f), new Vector3(0, 0.88f, -0.1f), 0.025f, 0.025f, 8, 1);
                }
                // A pressure gauge on the left tank, its needle in the red.
                m.Paint(0, Steel, 0.6f).Cylinder(new Vector3(-0.3f, 0.45f, 0.05f), new Vector3(-0.3f, 0.45f, 0.08f), 0.06f, 16);
                m.Paint(0, new Color(0.9f, 0.88f, 0.8f), 0.2f).Cylinder(new Vector3(-0.3f, 0.45f, 0.08f), new Vector3(-0.3f, 0.45f, 0.082f), 0.05f, 16);
                m.Paint(0, new Color(0.8f, 0.1f, 0.05f)).Box(new Vector3(-0.285f, 0.465f, 0.085f), new Vector3(0.006f, 0.04f, 0.004f), Quaternion.Euler(0, 0, -40));
                m.Paint(0, burnt, 0.35f).Cylinder(new Vector3(0, 0, -0.1f), new Vector3(0, 0.8f, -0.1f), 0.07f, 12);
            });
            rig.Pitch = Node(rig.Yaw, "Pitch", new Vector3(0, 0.82f, 0.02f));
            Part(rig.Pitch, "flame-nozzle", m =>
            {
                m.Paint(0, burnt, 0.35f).Cylinder(new Vector3(0, 0, -0.15f), new Vector3(0, 0, 0.3f), 0.1f, 18);
                m.Paint(0, Copper, 0.6f).Torus(new Vector3(0, 0, 0.0f), Vector3.forward, 0.1f, 0.02f, 16, 6);
                // The flared mouth, blackened inside.
                m.Paint(0, Iron, 0.4f).Capsule(new Vector3(0, 0, 0.3f), new Vector3(0, 0, 0.55f), 0.085f, 0.15f, 18, 1);
                m.Paint(0, Dark).Cylinder(new Vector3(0, 0, 0.6f), new Vector3(0, 0, 0.605f), 0.12f, 16);
                m.Paint(0, Iron, 0.4f).Capsule(new Vector3(0, -0.1f, 0.3f), new Vector3(0, -0.13f, 0.55f), 0.012f, 0.012f, 6, 1);
            });
            rig.Glow = Part(rig.Pitch, "flame-pilot", m =>
                m.Paint(0, pilot, 0.2f, 1f).Ellipsoid(new Vector3(0, -0.11f, 0.6f), new Vector3(0.03f, 0.045f, 0.03f), Quaternion.identity, 8, 6),
                shadows: false);
            rig.GlowColor = pilot;
            rig.Muzzle = Node(rig.Pitch, "Muzzle", new Vector3(0, 0, 0.62f));
        }
    }
}
