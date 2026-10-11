using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// What each orc looks like, after Orc Horde's sprite briefs: a build, a
    /// palette, and the armour and weapon it carries. Built once per type into a
    /// shared skinned mesh.
    /// </summary>
    public static class OrcDesigns
    {
        public enum Helm { None, Closed, Horned, Hood, Crown, RamHorns, Goggles }
        public enum Shoulders { None, Wraps, Plate, Spiked }
        public enum Weapon { Axe, Daggers, MaceAndShield, Hammer, Maul, RuneHammer, Bomb, Staff, Sack, Fists, TowerShield, WarAxe, Totem }

        public sealed class Design
        {
            public OrcBuild Build = OrcBuild.Standard;
            public Color Skin = new Color(0.3f, 0.42f, 0.18f), Leather = new Color(0.45f, 0.24f, 0.14f),
                Cloth = new Color(0.35f, 0.28f, 0.2f), Metal = new Color(0.5f, 0.52f, 0.56f),
                Accent = new Color(0.55f, 0.2f, 0.15f), Bone = new Color(0.86f, 0.82f, 0.68f),
                Eye = new Color(1f, 0.35f, 0.1f), Glow = new Color(1f, 0.3f, 0.1f);
            public Helm Helm;
            public Shoulders Shoulders;
            public bool ChestPlate, Bracers, Greaves, Robe, Banner, Necklace;
            public float Tusks = 1f;
            public Weapon Weapon;
        }

        static readonly Dictionary<EnemyDef, (Mesh, Vector3[], Design)> cache = new Dictionary<EnemyDef, (Mesh, Vector3[], Design)>();

        /// <summary>The shared mesh, bone layout and design for a type.</summary>
        public static (Mesh mesh, Vector3[] layout, Design design) Get(EnemyDef def)
        {
            if (cache.TryGetValue(def, out var hit)) return hit;
            var d = For(def);
            var layout = OrcRig.Layout(d.Build);
            var mesh = Build(def.Id, d, layout);
            cache[def] = (mesh, layout, d);
            return cache[def];
        }

        static Color C(uint rgb) => U.Hex(rgb);

        static Design For(EnemyDef def)
        {
            var d = new Design();
            switch (def.Id)
            {
                case "fodder":
                    d.Shoulders = Shoulders.Wraps; d.Weapon = Weapon.Axe; d.Accent = C(0x8a3b22);
                    break;
                case "runner":
                    d.Build = new OrcBuild { Bulk = 0.82f, Hunch = 0.8f, ArmLength = 1.05f, LegLength = 1.12f, HeadSize = 0.95f };
                    d.Skin = C(0x6b8c3c); d.Cloth = C(0xb48a2e); d.Leather = C(0x6b4a28);
                    d.Helm = Helm.Hood; d.Shoulders = Shoulders.Wraps; d.Accent = C(0xb48a2e); d.Weapon = Weapon.Daggers;
                    break;
                case "armoured":
                    d.Build = new OrcBuild { Bulk = 1.25f, Hunch = 0.3f, ArmLength = 1, LegLength = 0.95f, HeadSize = 1 };
                    d.Metal = C(0x6f7f94); d.Leather = C(0x5a3a22); d.Accent = C(0x6f7f94);
                    d.Helm = Helm.Closed; d.Shoulders = Shoulders.Plate; d.ChestPlate = d.Bracers = d.Greaves = true;
                    d.Weapon = Weapon.MaceAndShield;
                    break;
                case "siege_elite":
                    d.Build = new OrcBuild { Bulk = 1.35f, Hunch = 0.45f, ArmLength = 1.08f, LegLength = 1, HeadSize = 1.05f };
                    d.Leather = C(0x4a2a4a); d.Cloth = C(0x3a2238); d.Accent = C(0x7a3f8a);
                    d.Shoulders = Shoulders.Spiked; d.Bracers = true; d.Weapon = Weapon.Hammer; d.Tusks = 1.2f;
                    break;
                case "mini_boss":
                    d.Build = new OrcBuild { Bulk = 1.5f, Hunch = 0.5f, ArmLength = 1.1f, LegLength = 0.95f, HeadSize = 1.05f };
                    d.Skin = C(0x46632a); d.Metal = C(0x55303f); d.Accent = C(0xb5479b); d.Leather = C(0x3a2228);
                    d.Shoulders = Shoulders.Spiked; d.ChestPlate = d.Bracers = d.Greaves = true; d.Weapon = Weapon.Maul; d.Tusks = 1.5f;
                    break;
                case "siege_boss":
                    d.Build = new OrcBuild { Bulk = 1.6f, Hunch = 0.4f, ArmLength = 1.1f, LegLength = 1, HeadSize = 1.05f };
                    d.Skin = C(0x2f5a32); d.Metal = C(0x7a1c1c); d.Leather = C(0x1e1818); d.Accent = C(0x1e1818);
                    d.Helm = Helm.Horned; d.Shoulders = Shoulders.Spiked; d.ChestPlate = d.Bracers = d.Greaves = true;
                    d.Weapon = Weapon.RuneHammer; d.Tusks = 1.6f; d.Glow = C(0xff4a1a);
                    break;
                case "sapper":
                    d.Build = new OrcBuild { Bulk = 0.95f, Hunch = 0.55f, ArmLength = 1, LegLength = 0.95f, HeadSize = 1 };
                    d.Leather = C(0x6b4a28); d.Accent = C(0xf07a2a);
                    d.Helm = Helm.Goggles; d.Shoulders = Shoulders.Wraps; d.Weapon = Weapon.Bomb;
                    break;
                case "shaman":
                    d.Build = new OrcBuild { Bulk = 0.95f, Hunch = 0.6f, ArmLength = 1, LegLength = 0.95f, HeadSize = 1.05f };
                    d.Skin = C(0x6a8a48); d.Cloth = C(0x2e3a2a); d.Accent = C(0x4fbf7a); d.Eye = C(0x5aff8a); d.Glow = C(0x4fff8a);
                    d.Robe = d.Necklace = true; d.Helm = Helm.Hood; d.Weapon = Weapon.Staff;
                    break;
                case "splitter":
                    d.Build = new OrcBuild { Bulk = 1.3f, Hunch = 0.5f, ArmLength = 0.95f, LegLength = 0.85f, HeadSize = 0.95f, Belly = 1f };
                    d.Skin = C(0x7a8a4a); d.Cloth = C(0x5a3a6a); d.Accent = C(0x9d6fd6);
                    d.Shoulders = Shoulders.Wraps; d.Weapon = Weapon.Sack;
                    break;
                case "ram_chief":
                    d.Build = new OrcBuild { Bulk = 1.6f, Hunch = 0.65f, ArmLength = 1.05f, LegLength = 1, HeadSize = 1.1f };
                    d.Metal = C(0xa04a24); d.Accent = C(0xe0683a); d.Leather = C(0x3a2418);
                    d.Helm = Helm.RamHorns; d.Shoulders = Shoulders.Spiked; d.ChestPlate = d.Bracers = d.Greaves = true;
                    d.Weapon = Weapon.Fists; d.Tusks = 1.4f;
                    break;
                case "shieldwall":
                    d.Build = new OrcBuild { Bulk = 1.6f, Hunch = 0.3f, ArmLength = 1, LegLength = 0.95f, HeadSize = 1 };
                    d.Metal = C(0x3f6f9a); d.Accent = C(0x8fd0ff); d.Leather = C(0x2a2a30);
                    d.Helm = Helm.Closed; d.Shoulders = Shoulders.Plate; d.ChestPlate = d.Bracers = d.Greaves = true;
                    d.Weapon = Weapon.TowerShield;
                    break;
                case "warlord":
                    d.Build = new OrcBuild { Bulk = 1.6f, Hunch = 0.35f, ArmLength = 1.08f, LegLength = 1, HeadSize = 1.05f };
                    d.Metal = C(0x2a2a2e); d.Accent = C(0xc9a227); d.Leather = C(0x3a1e14); d.Cloth = C(0x8a1c1c);
                    d.Helm = Helm.Crown; d.Shoulders = Shoulders.Spiked; d.ChestPlate = d.Bracers = d.Greaves = d.Banner = true;
                    d.Weapon = Weapon.WarAxe; d.Tusks = 1.5f;
                    break;
                case "blood_totem":
                    d.Build = new OrcBuild { Bulk = 1.5f, Hunch = 0.55f, ArmLength = 1.05f, LegLength = 0.95f, HeadSize = 1.05f };
                    d.Skin = C(0x4a5a2a); d.Metal = C(0x6a1c2a); d.Cloth = C(0x3a1018); d.Accent = C(0xc23b5b); d.Glow = C(0xff2a4a);
                    d.Helm = Helm.Horned; d.Shoulders = Shoulders.Plate; d.Necklace = d.Bracers = true; d.Weapon = Weapon.Totem;
                    break;
                default:
                    d.Skin = Color.Lerp(def.Color, d.Skin, 0.5f);
                    d.Weapon = Weapon.Axe;
                    break;
            }
            return d;
        }

        // ------------------------------------------------------------------ build

        static Mesh Build(string name, Design d, Vector3[] p)
        {
            var m = new OrcMesh();
            float w = d.Build.Bulk, hs = d.Build.HeadSize;
            Vector3 P(OrcBone b) => p[(int)b];
            int B(OrcBone b) => (int)b;
            var skinDark = d.Skin * 0.72f;
            skinDark.a = 1;

            // Torso.
            m.Paint(B(OrcBone.Hips), d.Leather);
            m.Ellipsoid(P(OrcBone.Hips) + new Vector3(0, 0.02f, 0), new Vector3(0.17f * w, 0.12f, 0.13f * w), Quaternion.identity);
            m.Paint(B(OrcBone.Hips), d.Leather * 0.85f).Band(P(OrcBone.Hips) + new Vector3(0, 0.06f, 0), Vector3.up, 0.172f * w, 0.06f);
            m.Paint(B(OrcBone.Hips), d.Metal, 0.5f).Box(P(OrcBone.Hips) + new Vector3(0, 0.06f, 0.17f * w), new Vector3(0.07f, 0.06f, 0.02f), Quaternion.identity);
            m.Paint(B(OrcBone.Hips), d.Cloth);
            m.Box(P(OrcBone.Hips) + new Vector3(0, -0.13f, 0.12f * w), new Vector3(0.18f * w, 0.24f, 0.02f), Quaternion.Euler(-6, 0, 0));
            m.Box(P(OrcBone.Hips) + new Vector3(0, -0.12f, -0.11f * w), new Vector3(0.22f * w, 0.22f, 0.02f), Quaternion.Euler(6, 0, 0));

            float belly = d.Build.Belly;
            m.Paint(B(OrcBone.Spine), d.Skin);
            m.Ellipsoid(P(OrcBone.Spine) + new Vector3(0, 0.03f, 0.02f + belly * 0.1f),
                new Vector3(0.16f * w + belly * 0.1f, 0.15f + belly * 0.05f, 0.14f * w + belly * 0.16f), Quaternion.identity);

            m.Paint(B(OrcBone.Chest), d.Skin);
            m.Ellipsoid(P(OrcBone.Chest) + new Vector3(0, 0.06f, 0), new Vector3(0.23f * w, 0.18f, 0.16f * w), Quaternion.identity);
            foreach (var s in new[] { -1f, 1f })
                m.Ellipsoid(P(OrcBone.Chest) + new Vector3(s * 0.09f * w, 0.08f, 0.11f * w), new Vector3(0.1f * w, 0.08f, 0.06f), Quaternion.identity, 10, 6);
            m.Ellipsoid(P(OrcBone.Chest) + new Vector3(0, 0.17f, -0.02f), new Vector3(0.18f * w, 0.07f, 0.11f * w), Quaternion.identity, 12, 6);

            if (d.ChestPlate)
            {
                m.Paint(B(OrcBone.Chest), d.Metal, 0.5f);
                m.Ellipsoid(P(OrcBone.Chest) + new Vector3(0, 0.05f, 0.015f), new Vector3(0.245f * w, 0.19f, 0.175f * w), Quaternion.identity, 16, 8, 0.15f, 0.92f);
                m.Paint(B(OrcBone.Chest), d.Accent, 0.4f);
                m.Box(P(OrcBone.Chest) + new Vector3(0, 0.06f, 0.18f * w), new Vector3(0.05f, 0.22f, 0.02f), Quaternion.identity);
                Rivets(m, P(OrcBone.Chest) + new Vector3(0, 0.06f, 0.18f * w), 0.16f * w, 0.12f, d.Metal * 1.3f, B(OrcBone.Chest));
            }

            m.Paint(B(OrcBone.Neck), d.Skin).Capsule(P(OrcBone.Neck) + new Vector3(0, -0.04f, -0.01f), P(OrcBone.Head), 0.075f * w, 0.07f * w, 10, 2);
            if (d.Necklace)
            {
                m.Paint(B(OrcBone.Neck), d.Bone).Band(P(OrcBone.Neck) + new Vector3(0, -0.02f, 0.01f), new Vector3(0, 1, 0.4f), 0.095f * w, 0.025f, 12);
                for (int i = -2; i <= 2; i++)
                    m.Cone(P(OrcBone.Chest) + new Vector3(i * 0.04f, 0.2f, 0.13f * w), P(OrcBone.Chest) + new Vector3(i * 0.045f, 0.13f, 0.15f * w), 0.012f, 6);
            }

            Head(m, d, P(OrcBone.Head), P(OrcBone.Jaw), hs, skinDark);
            foreach (var s in new[] { -1f, 1f }) Arm(m, d, p, s, w);
            foreach (var s in new[] { -1f, 1f }) Leg(m, d, p, s, w);
            Gear(m, d, p, w);

            var layoutPoses = OrcRig.BindPoses(p);
            return m.Build("Orc " + name, layoutPoses);
        }

        static void Rivets(OrcMesh m, Vector3 center, float spreadX, float spreadY, Color color, int bone)
        {
            m.Paint(bone, color, 0.55f);
            foreach (var x in new[] { -1f, 1f })
            foreach (var y in new[] { -1f, 1f })
                m.Sphere(center + new Vector3(x * spreadX * 0.8f, y * spreadY * 0.8f, 0.005f), 0.012f, 6, 4);
        }

        static void Head(OrcMesh m, Design d, Vector3 head, Vector3 jaw, float hs, Color skinDark)
        {
            int H = (int)OrcBone.Head, J = (int)OrcBone.Jaw;
            Vector3 At(float x, float y, float z) => head + new Vector3(x, y, z) * hs;

            m.Paint(H, d.Skin).Ellipsoid(At(0, 0.06f, -0.01f), new Vector3(0.125f, 0.115f, 0.13f) * hs, Quaternion.identity, 16, 12);
            m.Paint(H, skinDark).Ellipsoid(At(0, 0.085f, 0.09f), new Vector3(0.12f, 0.035f, 0.055f) * hs, Quaternion.Euler(8, 0, 0), 12, 6);
            m.Paint(H, skinDark).Ellipsoid(At(0, 0.035f, 0.125f), new Vector3(0.035f, 0.032f, 0.04f) * hs, Quaternion.identity, 8, 6);
            foreach (var s in new[] { -1f, 1f })
            {
                m.Paint(H, d.Eye, 0.4f, 2.5f).Sphere(At(s * 0.045f, 0.062f, 0.112f), 0.019f * hs, 8, 5);
                m.Paint(H, d.Skin).Cone(At(s * 0.105f, 0.07f, -0.01f), At(s * 0.23f, 0.13f, -0.07f), 0.04f * hs, 8);
                m.Paint(H, skinDark).Ellipsoid(At(s * 0.085f, 0.02f, 0.08f), new Vector3(0.045f, 0.04f, 0.04f) * hs, Quaternion.identity, 8, 5);
            }

            // The jaw: heavy and underslung, tusks up from the lower teeth.
            m.Paint(J, d.Skin).Ellipsoid(jaw + new Vector3(0, -0.02f, 0.035f) * hs, new Vector3(0.105f, 0.06f, 0.09f) * hs, Quaternion.identity, 12, 8);
            m.Paint(J, new Color(0.25f, 0.06f, 0.05f)).Box(jaw + new Vector3(0, 0.025f, 0.11f) * hs, new Vector3(0.1f, 0.014f, 0.02f) * hs, Quaternion.identity);
            foreach (var s in new[] { -1f, 1f })
                m.Paint(J, d.Bone, 0.35f).Cone(jaw + new Vector3(s * 0.05f, 0.0f, 0.1f) * hs,
                    jaw + new Vector3(s * 0.065f, 0.07f * d.Tusks + 0.01f, 0.12f) * hs, 0.017f * hs * d.Tusks, 7);

            switch (d.Helm)
            {
                case Helm.Closed:
                    m.Paint(H, d.Metal, 0.5f).Ellipsoid(At(0, 0.08f, -0.005f), new Vector3(0.142f, 0.135f, 0.148f) * hs, Quaternion.identity, 16, 10, 0.32f, 1f);
                    m.Paint(H, d.Metal * 0.9f, 0.5f).Box(At(0, 0.03f, 0.13f), new Vector3(0.17f, 0.12f, 0.03f) * hs, Quaternion.Euler(-8, 0, 0));
                    m.Paint(H, new Color(0.05f, 0.05f, 0.06f)).Box(At(0, 0.062f, 0.147f), new Vector3(0.13f, 0.018f, 0.01f) * hs, Quaternion.identity);
                    m.Paint(H, d.Metal * 1.2f, 0.55f).Box(At(0, 0.17f, 0.02f), new Vector3(0.025f, 0.05f, 0.24f) * hs, Quaternion.identity);
                    break;
                case Helm.Horned:
                    m.Paint(H, d.Metal, 0.5f).Ellipsoid(At(0, 0.08f, -0.005f), new Vector3(0.142f, 0.135f, 0.148f) * hs, Quaternion.identity, 16, 10, 0.42f, 1f);
                    foreach (var s in new[] { -1f, 1f })
                        m.Paint(H, d.Bone * 0.85f, 0.3f).Horn(At(s * 0.11f, 0.13f, 0), new Vector3(s, 0.4f, 0.1f), new Vector3(0, 1, 0.3f), 0.32f * hs, 0.045f * hs);
                    break;
                case Helm.RamHorns:
                    m.Paint(H, d.Metal, 0.5f).Ellipsoid(At(0, 0.08f, 0.01f), new Vector3(0.145f, 0.13f, 0.155f) * hs, Quaternion.identity, 16, 10, 0.35f, 1f);
                    foreach (var s in new[] { -1f, 1f })
                        m.Paint(H, d.Bone * 0.8f, 0.25f).Horn(At(s * 0.1f, 0.14f, -0.02f), new Vector3(s, 0.2f, -0.5f), new Vector3(0, -1, 0.6f), 0.42f * hs, 0.06f * hs, 7);
                    break;
                case Helm.Hood:
                    m.Paint(H, d.Cloth).Ellipsoid(At(0, 0.075f, -0.025f), new Vector3(0.15f, 0.145f, 0.155f) * hs, Quaternion.identity, 16, 10, 0.3f, 1f);
                    m.Paint(H, d.Cloth * 0.85f).Box(At(0, -0.03f, -0.12f), new Vector3(0.22f, 0.2f, 0.03f) * hs, Quaternion.Euler(18, 0, 0));
                    break;
                case Helm.Crown:
                    m.Paint(H, d.Accent, 0.6f, 0.15f).Band(At(0, 0.13f, -0.01f), Vector3.up, 0.13f * hs, 0.04f * hs, 16);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2;
                        var c = At(Mathf.Cos(a) * 0.125f, 0.15f, -0.01f + Mathf.Sin(a) * 0.125f);
                        m.Cone(c, c + Vector3.up * 0.08f * hs, 0.02f * hs, 6);
                    }
                    break;
                case Helm.Goggles:
                    m.Paint(H, d.Leather).Band(At(0, 0.07f, -0.005f), Vector3.up, 0.132f * hs, 0.03f * hs, 16);
                    foreach (var s in new[] { -1f, 1f })
                        m.Paint(H, d.Accent, 0.6f, 1.2f).Capsule(At(s * 0.045f, 0.065f, 0.11f), At(s * 0.045f, 0.065f, 0.14f), 0.03f * hs, 0.03f * hs, 10, 2);
                    break;
            }
        }

        static void Arm(OrcMesh m, Design d, Vector3[] p, float s, float w)
        {
            var (clav, upper, fore, hand) = s < 0
                ? (OrcBone.ClavicleL, OrcBone.UpperArmL, OrcBone.ForearmL, OrcBone.HandL)
                : (OrcBone.ClavicleR, OrcBone.UpperArmR, OrcBone.ForearmR, OrcBone.HandR);
            Vector3 P(OrcBone b) => p[(int)b];
            int U = (int)upper, F = (int)fore, Hd = (int)hand;

            m.Paint(U, d.Skin).Sphere(P(upper), 0.09f * w, 12, 8);
            m.Capsule(P(upper), P(fore), 0.075f * w, 0.06f * w, 12, 3);
            m.Paint(F, d.Skin).Sphere(P(fore), 0.06f * w, 10, 6);
            m.Capsule(P(fore), P(hand), 0.066f * w, 0.05f * w, 12, 3);
            if (d.Bracers)
                m.Paint(F, d.Metal, 0.5f).Capsule(Vector3.Lerp(P(fore), P(hand), 0.35f), Vector3.Lerp(P(fore), P(hand), 0.85f), 0.07f * w, 0.058f * w, 12, 1);
            else
                m.Paint(F, d.Leather).Band(Vector3.Lerp(P(fore), P(hand), 0.75f), P(hand) - P(fore), 0.056f * w, 0.06f, 12);

            // A big, knuckly hand: palm, four curled fingers, a thumb, black claws.
            var h = P(hand);
            m.Paint(Hd, d.Skin).Ellipsoid(h + new Vector3(0, -0.045f, 0.015f), new Vector3(0.046f * w, 0.055f, 0.036f), Quaternion.identity, 10, 7);
            for (int i = 0; i < 4; i++)
            {
                float x = (-0.027f + i * 0.018f) * w;
                var a = h + new Vector3(x, -0.085f, 0.03f);
                var b = h + new Vector3(x, -0.12f, 0.06f);
                m.Paint(Hd, d.Skin).Capsule(a, b, 0.0125f * w, 0.011f * w, 6, 2);
                m.Paint(Hd, new Color(0.12f, 0.1f, 0.08f), 0.4f).Cone(b, b + new Vector3(0, -0.015f, 0.02f), 0.008f * w, 5);
            }
            m.Paint(Hd, d.Skin).Capsule(h + new Vector3(-s * 0.035f * w, -0.045f, 0.035f), h + new Vector3(-s * 0.045f * w, -0.08f, 0.07f), 0.013f * w, 0.011f * w, 6, 2);

            switch (d.Shoulders)
            {
                case Shoulders.Wraps:
                    m.Paint(U, d.Accent).Ellipsoid(P(upper) + new Vector3(s * 0.01f, 0.02f, 0), new Vector3(0.1f * w, 0.085f, 0.1f * w), Quaternion.Euler(0, 0, -s * 20), 12, 8, 0.4f, 1f);
                    m.Paint(U, d.Leather).Band(Vector3.Lerp(P(upper), P(fore), 0.3f), P(fore) - P(upper), 0.074f * w, 0.04f, 10);
                    break;
                case Shoulders.Plate:
                case Shoulders.Spiked:
                    var dome = P(upper) + new Vector3(s * 0.025f, 0.035f, 0);
                    var tilt = Quaternion.Euler(0, 0, -s * 28);
                    m.Paint(U, d.Metal, 0.5f).Ellipsoid(dome, new Vector3(0.15f * w, 0.12f, 0.14f * w), tilt, 16, 10, 0.45f, 1f);
                    m.Paint(U, d.Metal * 0.8f, 0.5f).Ellipsoid(dome + new Vector3(s * 0.01f, -0.045f, 0), new Vector3(0.14f * w, 0.1f, 0.13f * w), tilt, 14, 6, 0.42f, 0.62f);
                    m.Paint(U, d.Metal * 1.3f, 0.6f);
                    for (int i = 0; i < 3; i++)
                        m.Sphere(dome + tilt * new Vector3((i - 1) * 0.06f * w, 0.115f, 0.06f * w), 0.012f, 6, 4);
                    if (d.Shoulders == Shoulders.Spiked)
                        for (int i = 0; i < 3; i++)
                        {
                            var basePt = dome + tilt * new Vector3(0, 0.1f, (i - 1) * 0.06f * w);
                            m.Paint(U, d.Bone * 0.9f, 0.35f).Cone(basePt, basePt + tilt * new Vector3(s * 0.04f, 0.13f, 0), 0.025f * w, 7);
                        }
                    break;
            }
        }

        static void Leg(OrcMesh m, Design d, Vector3[] p, float s, float w)
        {
            var (thigh, shin, foot) = s < 0 ? (OrcBone.ThighL, OrcBone.ShinL, OrcBone.FootL) : (OrcBone.ThighR, OrcBone.ShinR, OrcBone.FootR);
            Vector3 P(OrcBone b) => p[(int)b];
            int T = (int)thigh, S = (int)shin, Fo = (int)foot;

            m.Paint(T, d.Cloth).Capsule(P(thigh), P(shin), 0.095f * w, 0.068f * w, 12, 3);
            m.Paint(S, d.Skin).Sphere(P(shin), 0.066f * w, 10, 6);
            m.Capsule(P(shin), P(foot) + new Vector3(0, 0.06f, 0), 0.064f * w, 0.05f * w, 12, 3);
            if (d.Greaves)
            {
                m.Paint(S, d.Metal, 0.5f).Capsule(Vector3.Lerp(P(shin), P(foot), 0.12f) + new Vector3(0, 0, 0.015f),
                    Vector3.Lerp(P(shin), P(foot), 0.75f) + new Vector3(0, 0, 0.012f), 0.07f * w, 0.058f * w, 12, 1);
                m.Paint(S, d.Metal * 1.1f, 0.55f).Ellipsoid(P(shin) + new Vector3(0, 0, 0.04f), new Vector3(0.06f, 0.06f, 0.04f) * w, Quaternion.identity, 10, 6, 0.2f, 1f);
            }
            else
                foreach (var t in new[] { 0.35f, 0.6f })
                    m.Paint(S, d.Leather).Band(Vector3.Lerp(P(shin), P(foot), t), P(foot) - P(shin), 0.062f * w, 0.035f, 10);

            m.Paint(Fo, d.Leather * 0.8f, 0.25f).Ellipsoid(P(foot) + new Vector3(0, 0.005f, 0.055f), new Vector3(0.065f * w, 0.055f, 0.125f), Quaternion.identity, 12, 8);
            m.Paint(Fo, d.Leather * 0.6f).Box(P(foot) + new Vector3(0, -0.04f, 0.05f), new Vector3(0.12f * w, 0.025f, 0.24f), Quaternion.identity);
        }

        static void Gear(OrcMesh m, Design d, Vector3[] p, float w)
        {
            Vector3 P(OrcBone b) => p[(int)b];
            int R = (int)OrcBone.HandR, L = (int)OrcBone.HandL, FL = (int)OrcBone.ForearmL, Ch = (int)OrcBone.Chest;
            var grip = P(OrcBone.HandR) + new Vector3(0, -0.07f, 0.03f);
            var gripL = P(OrcBone.HandL) + new Vector3(0, -0.07f, 0.03f);
            var fwd = new Vector3(0, -0.25f, 1).normalized;
            var wood = new Color(0.42f, 0.28f, 0.16f);

            void Haft(int bone, Vector3 from, Vector3 dir, float length, float radius)
            {
                m.Paint(bone, wood).Capsule(from - dir * 0.08f, from + dir * length, radius, radius * 0.9f, 8, 1);
                m.Paint(bone, d.Metal * 0.8f, 0.5f).Band(from + dir * (length * 0.8f), dir, radius * 1.3f, 0.03f, 8);
            }

            switch (d.Weapon)
            {
                case Weapon.Axe:
                {
                    Haft(R, grip, fwd, 0.5f, 0.017f);
                    var head = grip + fwd * 0.45f;
                    m.Paint(R, d.Metal, 0.55f).Box(head + Vector3.up * 0.07f, new Vector3(0.016f, 0.14f, 0.1f), Quaternion.LookRotation(fwd));
                    m.Paint(R, d.Metal * 1.2f, 0.65f).Box(head + Vector3.up * 0.14f, new Vector3(0.008f, 0.03f, 0.13f), Quaternion.LookRotation(fwd));
                    break;
                }
                case Weapon.Daggers:
                    foreach (var (bone, at) in new[] { (R, grip), (L, gripL) })
                    {
                        m.Paint(bone, d.Leather).Capsule(at - fwd * 0.04f, at + fwd * 0.05f, 0.014f, 0.014f, 6, 1);
                        m.Paint(bone, d.Metal * 1.2f, 0.65f).Box(at + fwd * 0.16f, new Vector3(0.01f, 0.03f, 0.2f), Quaternion.LookRotation(fwd));
                    }
                    break;
                case Weapon.MaceAndShield:
                {
                    Haft(R, grip, fwd, 0.42f, 0.018f);
                    var ball = grip + fwd * 0.46f;
                    m.Paint(R, d.Metal * 0.9f, 0.5f).Sphere(ball, 0.065f, 10, 7);
                    // Spikes spread evenly over the ball (golden-angle spiral), the same every time.
                    for (int i = 0; i < 10; i++)
                    {
                        float y = 1 - (i + 0.5f) / 10 * 2, r = Mathf.Sqrt(1 - y * y), a = i * 2.39996f;
                        var dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                        m.Cone(ball + dir * 0.055f, ball + dir * 0.12f, 0.018f, 6);
                    }
                    Shield(m, d, FL, P(OrcBone.ForearmL) + new Vector3(-0.1f * w, -0.12f, 0.06f), new Vector3(0.05f, 0.44f, 0.34f) * w, false);
                    break;
                }
                case Weapon.Hammer:
                case Weapon.Maul:
                case Weapon.RuneHammer:
                {
                    float big = d.Weapon == Weapon.Hammer ? 1f : 1.35f;
                    var dir = new Vector3(0, -0.5f, 1).normalized;
                    Haft(R, grip, dir, 0.8f * big, 0.024f * big);
                    var head = grip + dir * 0.8f * big;
                    var rot = Quaternion.LookRotation(dir);
                    m.Paint(R, d.Metal * 0.75f, 0.45f).Box(head, new Vector3(0.28f, 0.15f, 0.15f) * big, rot);
                    m.Paint(R, d.Metal * 1.15f, 0.55f);
                    foreach (var x in new[] { -0.1f, 0.1f }) m.Band(head + rot * new Vector3(x * big, 0, 0), rot * Vector3.right, 0.11f * big, 0.03f, 4);
                    if (d.Weapon != Weapon.Hammer)
                        foreach (var x in new[] { -1f, 1f })
                            m.Paint(R, d.Bone * 0.9f, 0.4f).Cone(head + rot * new Vector3(x * 0.14f * big, 0, 0), head + rot * new Vector3(x * 0.24f * big, 0, 0), 0.05f * big, 7);
                    if (d.Weapon == Weapon.RuneHammer)
                        m.Paint(R, d.Glow, 0.3f, 3f).Box(head + rot * new Vector3(0, 0, 0.078f * big), new Vector3(0.18f, 0.03f, 0.005f) * big, rot);
                    break;
                }
                case Weapon.Bomb:
                {
                    var back = P(OrcBone.Chest) + new Vector3(0, 0.05f, -0.2f * w);
                    m.Paint(Ch, new Color(0.15f, 0.15f, 0.17f), 0.5f).Sphere(back, 0.15f, 12, 9);
                    m.Paint(Ch, d.Leather).Band(back, Vector3.right, 0.152f, 0.04f, 14);
                    m.Paint(Ch, wood).Capsule(back + Vector3.up * 0.14f, back + new Vector3(0.03f, 0.26f, 0), 0.01f, 0.008f, 6, 1);
                    m.Paint(Ch, d.Accent, 0.2f, 3.5f).Sphere(back + new Vector3(0.03f, 0.27f, 0), 0.025f, 6, 4);
                    m.Paint(R, d.Accent * 0.8f, 0.2f, 1.5f).Capsule(grip, grip + fwd * 0.18f, 0.02f, 0.03f, 8, 2);
                    break;
                }
                case Weapon.Staff:
                {
                    var bottom = grip + new Vector3(0, -0.75f, 0.06f);
                    var top = grip + new Vector3(0, 0.75f, 0.1f);
                    m.Paint(R, wood * 0.8f).Capsule(bottom, top, 0.02f, 0.018f, 8, 1);
                    m.Paint(R, d.Bone, 0.3f);
                    for (int i = 0; i < 3; i++) m.Cone(top + Vector3.down * 0.02f, top + new Vector3(Mathf.Cos(i * 2.1f) * 0.08f, 0.1f, Mathf.Sin(i * 2.1f) * 0.08f), 0.012f, 5);
                    m.Paint(R, d.Glow, 0.3f, 3f).Sphere(top + Vector3.up * 0.05f, 0.05f, 10, 7);
                    break;
                }
                case Weapon.Sack:
                {
                    var back = P(OrcBone.Chest) + new Vector3(0, -0.02f, -0.22f * w);
                    m.Paint(Ch, d.Cloth).Ellipsoid(back, new Vector3(0.2f, 0.26f, 0.16f) * w, Quaternion.Euler(10, 0, 0), 14, 10);
                    m.Paint(Ch, d.Leather).Band(back + Vector3.up * 0.2f * w, Vector3.up, 0.08f * w, 0.04f, 10);
                    break;
                }
                case Weapon.Fists:
                    foreach (var (bone, at) in new[] { (R, P(OrcBone.HandR)), (L, P(OrcBone.HandL)) })
                    {
                        m.Paint(bone, d.Metal, 0.5f).Ellipsoid(at + new Vector3(0, -0.07f, 0.03f), new Vector3(0.06f, 0.05f, 0.05f) * w, Quaternion.identity, 10, 6);
                        for (int i = 0; i < 3; i++)
                            m.Paint(bone, d.Bone * 0.9f, 0.4f).Cone(at + new Vector3((i - 1) * 0.025f * w, -0.1f, 0.06f), at + new Vector3((i - 1) * 0.03f * w, -0.12f, 0.14f), 0.013f * w, 5);
                    }
                    break;
                case Weapon.TowerShield:
                    // Braced against the chest rather than the arm, so it stays upright, a wall in front of it.
                    Shield(m, d, Ch, P(OrcBone.Chest) + new Vector3(-0.08f * w, -0.2f, 0.3f * w), new Vector3(0.56f, 0.95f, 0.05f) * w, true);
                    Haft(R, grip, fwd, 0.35f, 0.018f);
                    m.Paint(R, d.Metal * 1.2f, 0.6f).Box(grip + fwd * 0.42f, new Vector3(0.012f, 0.04f, 0.18f), Quaternion.LookRotation(fwd));
                    break;
                case Weapon.WarAxe:
                {
                    var dir = new Vector3(0, -0.4f, 1).normalized;
                    Haft(R, grip, dir, 0.85f, 0.026f);
                    var head = grip + dir * 0.75f;
                    var rot = Quaternion.LookRotation(dir);
                    foreach (var y in new[] { -1f, 1f })
                        m.Paint(R, d.Metal * 1.1f, 0.55f).Box(head + rot * new Vector3(0, y * 0.12f, 0), new Vector3(0.02f, 0.2f, 0.22f), rot);
                    m.Paint(R, d.Accent, 0.6f, 0.2f).Band(head, dir, 0.04f, 0.08f, 8);
                    break;
                }
                case Weapon.Totem:
                {
                    var bottom = grip + new Vector3(0, -0.8f, 0.05f);
                    var top = grip + new Vector3(0, 1.0f, 0.1f);
                    m.Paint(R, wood * 0.6f).Capsule(bottom, top, 0.03f, 0.028f, 8, 1);
                    for (int i = 0; i < 3; i++)
                    {
                        var c = Vector3.Lerp(grip, top, 0.45f + i * 0.25f);
                        m.Paint(R, d.Bone, 0.3f).Ellipsoid(c, new Vector3(0.06f, 0.07f, 0.065f), Quaternion.identity, 10, 7);
                        foreach (var s in new[] { -1f, 1f }) m.Paint(R, d.Glow, 0.2f, 3f).Sphere(c + new Vector3(s * 0.022f, 0.01f, 0.055f), 0.012f, 6, 4);
                    }
                    m.Paint(R, d.Glow, 0.2f, 2.5f).Band(Vector3.Lerp(grip, top, 0.3f), top - grip, 0.034f, 0.12f, 10);
                    break;
                }
            }

            if (d.Banner)
            {
                var poleBase = P(OrcBone.Chest) + new Vector3(0.08f, -0.1f, -0.17f * w);
                var poleTop = poleBase + new Vector3(0.02f, 1.05f, -0.03f);
                m.Paint(Ch, wood).Capsule(poleBase, poleTop, 0.018f, 0.016f, 8, 1);
                m.Paint(Ch, d.Cloth).Box(poleTop + new Vector3(0.18f, -0.22f, 0), new Vector3(0.34f, 0.42f, 0.012f), Quaternion.identity);
                m.Paint(Ch, d.Accent, 0.5f, 0.3f).Box(poleTop + new Vector3(0.18f, -0.2f, 0.007f), new Vector3(0.12f, 0.12f, 0.004f), Quaternion.Euler(0, 0, 45));
            }
        }

        static void Shield(OrcMesh m, Design d, int bone, Vector3 center, Vector3 size, bool tower)
        {
            if (tower)
            {
                m.Paint(bone, d.Metal, 0.45f).Box(center, size, Quaternion.identity);
                m.Paint(bone, d.Accent, 0.4f, 0.4f).Box(center + new Vector3(0, 0, size.z / 2 + 0.004f), new Vector3(size.x * 0.18f, size.y * 0.9f, 0.006f), Quaternion.identity);
                m.Paint(bone, d.Metal * 1.3f, 0.6f);
                foreach (var x in new[] { -0.42f, 0.42f })
                foreach (var y in new[] { -0.44f, 0f, 0.44f })
                    m.Sphere(center + new Vector3(x * size.x, y * size.y, size.z / 2 + 0.005f), 0.016f, 6, 4);
                return;
            }
            // A kite shield worn on the outside of the forearm, facing out and a little forward.
            var rot = Quaternion.Euler(0, -70, 0);
            m.Paint(bone, d.Metal, 0.45f).Ellipsoid(center, new Vector3(size.z, size.y, size.x) * 0.5f, rot, 14, 10);
            m.Paint(bone, d.Metal * 1.3f, 0.6f).Sphere(center + rot * new Vector3(0, 0, size.x * 0.28f), 0.045f, 8, 6);
        }
    }
}
