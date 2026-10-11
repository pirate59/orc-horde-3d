using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// How each orc dresses for the map it marches on. Every biome changes three
    /// things over the base design:
    /// <list type="bullet">
    /// <item>its palette (warm wool in the snow, sun-bleached cloth in the canyon, ...);</item>
    /// <item>its gear (fur mantles and hats, sand scarves, leaf camouflage, miners' lanterns);</item>
    /// <item>the shader's overlays: what settles on its shoulders (snow, dust, moss, stone dust)
    /// and what cakes its legs (snow, red dust, mud).</item>
    /// </list>
    /// Towers take the same overlays, so a snowy map's guns wear snow too.
    /// </summary>
    public static class OrcBiomes
    {
        public struct Overlay
        {
            public Color Cover, Grime;
            public float CoverAmount, CoverSmooth, GrimeAmount, GrimeHeight;
            /// <summary>Cold enough to see an orc's breath.</summary>
            public bool Breath;
        }

        public static Overlay For(string biome) => biome switch
        {
            "snow" => new Overlay
            {
                Cover = new Color(0.95f, 0.97f, 1f), CoverAmount = 0.5f, CoverSmooth = 0.25f,
                Grime = new Color(0.86f, 0.89f, 0.95f), GrimeAmount = 0.65f, GrimeHeight = 0.3f, Breath = true,
            },
            "canyon" => new Overlay
            {
                Cover = new Color(0.8f, 0.62f, 0.42f), CoverAmount = 0.28f, CoverSmooth = 0.05f,
                Grime = new Color(0.68f, 0.42f, 0.28f), GrimeAmount = 0.7f, GrimeHeight = 0.45f,
            },
            "grassland" => new Overlay
            {
                Cover = new Color(0.3f, 0.42f, 0.16f), CoverAmount = 0.18f, CoverSmooth = 0.05f,
                Grime = new Color(0.24f, 0.2f, 0.12f), GrimeAmount = 0.6f, GrimeHeight = 0.32f,
            },
            _ => new Overlay
            {
                Cover = new Color(0.55f, 0.53f, 0.5f), CoverAmount = 0.25f, CoverSmooth = 0.05f,
                Grime = new Color(0.22f, 0.16f, 0.11f), GrimeAmount = 0.75f, GrimeHeight = 0.5f,
            },
        };

        /// <summary>Sets a material's overlays for a biome; <paramref name="strength"/> scales them (towers wear less).</summary>
        public static void Apply(Material m, string biome, float strength = 1f)
        {
            var o = For(biome);
            m.SetColor("_CoverColor", o.Cover);
            m.SetFloat("_CoverAmount", o.CoverAmount * strength);
            m.SetFloat("_CoverSmooth", o.CoverSmooth);
            m.SetColor("_GrimeColor", o.Grime);
            m.SetFloat("_GrimeAmount", o.GrimeAmount * strength);
            m.SetFloat("_GrimeHeight", o.GrimeHeight);
        }

        /// <summary>Shifts a design's colours toward what the biome's orcs wear.</summary>
        public static void Palette(OrcDesigns.Design d, string biome)
        {
            switch (biome)
            {
                case "snow":
                    d.Cloth = Color.Lerp(d.Cloth, new Color(0.42f, 0.16f, 0.12f), 0.45f);
                    d.Leather *= 0.85f;
                    d.Skin = Color.Lerp(d.Skin, new Color(0.33f, 0.45f, 0.38f), 0.3f);
                    break;
                case "canyon":
                    d.Cloth = Color.Lerp(d.Cloth, new Color(0.76f, 0.62f, 0.42f), 0.45f);
                    d.Leather = Color.Lerp(d.Leather, new Color(0.6f, 0.42f, 0.26f), 0.3f);
                    d.Metal = Color.Lerp(d.Metal, new Color(0.62f, 0.52f, 0.42f), 0.15f);
                    break;
                case "grassland":
                    d.Cloth = Color.Lerp(d.Cloth, new Color(0.25f, 0.35f, 0.18f), 0.35f);
                    break;
                default:
                    d.Leather *= 0.75f;
                    d.Cloth = Color.Lerp(d.Cloth, new Color(0.3f, 0.26f, 0.22f), 0.3f);
                    break;
            }
            d.Cloth.a = d.Leather.a = d.Skin.a = d.Metal.a = 1;
        }

        static bool BareHead(OrcDesigns.Design d) => d.Helm == OrcDesigns.Helm.None || d.Helm == OrcDesigns.Helm.Goggles;

        /// <summary>Adds the biome's gear on top of a built orc.</summary>
        public static void Dress(OrcMesh m, OrcDesigns.Design d, Vector3[] p, string biome)
        {
            Vector3 P(OrcBone b) => p[(int)b];
            int B(OrcBone b) => (int)b;
            float w = d.Build.Bulk, hs = d.Build.HeadSize;
            var head = P(OrcBone.Head);

            switch (biome)
            {
                case "snow":
                {
                    var fur = new Color(0.45f, 0.33f, 0.22f);
                    var trim = new Color(0.8f, 0.74f, 0.64f);
                    // A fur mantle over the shoulders, and a thick collar.
                    m.Paint(B(OrcBone.Chest), fur, 0.05f)
                        .Ellipsoid(P(OrcBone.Chest) + new Vector3(0, 0.15f, -0.02f), new Vector3(0.29f * w, 0.13f, 0.21f * w), Quaternion.identity, 18, 10, 0.4f, 1f);
                    m.Paint(B(OrcBone.Neck), trim, 0.05f).Torus(P(OrcBone.Neck) + new Vector3(0, -0.03f, 0.01f), new Vector3(0, 1, 0.25f), 0.1f * w, 0.045f * w, 18, 8);
                    if (BareHead(d))
                    {
                        m.Paint(B(OrcBone.Head), fur, 0.05f).Ellipsoid(head + new Vector3(0, 0.1f, -0.01f) * hs, new Vector3(0.148f, 0.11f, 0.152f) * hs, Quaternion.identity, 16, 8, 0.45f, 1f);
                        m.Paint(B(OrcBone.Head), trim, 0.05f).Torus(head + new Vector3(0, 0.1f, -0.01f) * hs, Vector3.up, 0.145f * hs, 0.03f * hs, 18, 6);
                    }
                    // Fur cuffs at the boots and the elbows.
                    foreach (var (shin, foot) in new[] { (OrcBone.ShinL, OrcBone.FootL), (OrcBone.ShinR, OrcBone.FootR) })
                        m.Paint(B(shin), trim, 0.05f).Torus(Vector3.Lerp(P(shin), P(foot), 0.72f), Vector3.up, 0.068f * w, 0.035f * w, 14, 6);
                    foreach (var (fore, hand) in new[] { (OrcBone.ForearmL, OrcBone.HandL), (OrcBone.ForearmR, OrcBone.HandR) })
                        m.Paint(B(fore), fur, 0.05f).Torus(Vector3.Lerp(P(fore), P(hand), 0.2f), P(hand) - P(fore), 0.07f * w, 0.03f * w, 14, 6);
                    break;
                }
                case "canyon":
                {
                    var sand = new Color(0.78f, 0.64f, 0.44f);
                    // A scarf about the neck with its tail over the back.
                    m.Paint(B(OrcBone.Neck), sand, 0.05f).Band(P(OrcBone.Neck) + new Vector3(0, -0.03f, 0.01f), Vector3.up, 0.1f * w, 0.08f, 16);
                    m.Paint(B(OrcBone.Chest), sand * 0.92f, 0.05f).Box(P(OrcBone.Chest) + new Vector3(0.06f, 0.06f, -0.17f * w), new Vector3(0.08f, 0.26f, 0.015f), Quaternion.Euler(14, 0, 10));
                    if (BareHead(d))
                    {
                        m.Paint(B(OrcBone.Head), sand, 0.05f);
                        m.Torus(head + new Vector3(0, 0.1f, -0.01f) * hs, new Vector3(0, 1, 0.15f), 0.13f * hs, 0.04f * hs, 18, 6);
                        m.Torus(head + new Vector3(0, 0.145f, -0.02f) * hs, new Vector3(0.1f, 1, -0.1f), 0.11f * hs, 0.04f * hs, 18, 6);
                        m.Box(head + new Vector3(0.03f, 0.02f, -0.15f) * hs, new Vector3(0.06f, 0.16f, 0.012f) * hs, Quaternion.Euler(20, 0, 8));
                    }
                    if (d.Shoulders == OrcDesigns.Shoulders.None || d.Shoulders == OrcDesigns.Shoulders.Wraps)
                        foreach (var upper in new[] { OrcBone.UpperArmL, OrcBone.UpperArmR })
                            m.Paint(B(upper), sand * 0.85f, 0.05f).Ellipsoid(P(upper) + new Vector3(0, 0.04f, 0), new Vector3(0.11f * w, 0.07f, 0.11f * w), Quaternion.identity, 12, 6, 0.5f, 1f);
                    break;
                }
                case "grassland":
                {
                    // Leaf camouflage tucked into the straps, deterministic per type.
                    var leaves = new[] { new Color(0.28f, 0.42f, 0.16f), new Color(0.38f, 0.5f, 0.2f), new Color(0.22f, 0.32f, 0.12f) };
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 2.39996f;
                        bool back = i % 3 != 0;
                        var bone = i % 4 == 0 ? OrcBone.UpperArmL : i % 4 == 1 ? OrcBone.UpperArmR : OrcBone.Chest;
                        var basePt = bone == OrcBone.Chest
                            ? P(OrcBone.Chest) + new Vector3(Mathf.Cos(a) * 0.18f * w, 0.12f + Mathf.Sin(a * 0.7f) * 0.06f, (back ? -0.16f : 0.14f) * w)
                            : P(bone) + new Vector3(Mathf.Cos(a) * 0.05f, 0.08f, Mathf.Sin(a) * 0.05f);
                        m.Paint(B(bone), leaves[i % 3], 0.2f).Ellipsoid(basePt, new Vector3(0.045f, 0.01f, 0.085f), Quaternion.Euler(a * 30f % 70 - 35, a * 57f % 360, a * 20f % 40 - 20), 8, 4);
                    }
                    var vine = new Color(0.2f, 0.32f, 0.12f);
                    foreach (var (fore, hand) in new[] { (OrcBone.ForearmL, OrcBone.HandL), (OrcBone.ForearmR, OrcBone.HandR) })
                        m.Paint(B(fore), vine, 0.1f).Torus(Vector3.Lerp(P(fore), P(hand), 0.45f), P(hand) - P(fore), 0.066f * w, 0.012f, 14, 5);
                    if (d.Helm != OrcDesigns.Helm.Closed && d.Helm != OrcDesigns.Helm.Horned && d.Helm != OrcDesigns.Helm.RamHorns)
                    {
                        m.Paint(B(OrcBone.Head), new Color(0.75f, 0.2f, 0.12f), 0.1f).Cone(head + new Vector3(0.04f, 0.15f, -0.08f) * hs, head + new Vector3(0.09f, 0.32f, -0.2f) * hs, 0.018f * hs, 6);
                        m.Paint(B(OrcBone.Head), new Color(0.9f, 0.88f, 0.8f), 0.1f).Cone(head + new Vector3(0.0f, 0.15f, -0.09f) * hs, head + new Vector3(0.02f, 0.3f, -0.23f) * hs, 0.016f * hs, 6);
                    }
                    break;
                }
                default:
                {
                    // Quarry work: a lantern on the belt, a candle cap on a bare head, a rope over the back.
                    var iron = new Color(0.2f, 0.2f, 0.22f);
                    var lamp = P(OrcBone.Hips) + new Vector3(0.17f * w, -0.03f, 0.08f * w);
                    m.Paint(B(OrcBone.Hips), iron, 0.4f).Box(lamp, new Vector3(0.065f, 0.085f, 0.065f), Quaternion.identity);
                    m.Paint(B(OrcBone.Hips), new Color(1f, 0.78f, 0.35f), 0.3f, 2.5f).Box(lamp, new Vector3(0.05f, 0.06f, 0.07f), Quaternion.identity);
                    m.Paint(B(OrcBone.Chest), new Color(0.55f, 0.45f, 0.3f), 0.05f).Torus(P(OrcBone.Chest) + new Vector3(0, 0.05f, -0.2f * w), Vector3.forward, 0.09f, 0.025f, 16, 6);
                    if (d.Helm == OrcDesigns.Helm.None)
                    {
                        m.Paint(B(OrcBone.Head), d.Leather, 0.15f).Ellipsoid(head + new Vector3(0, 0.1f, -0.01f) * hs, new Vector3(0.14f, 0.09f, 0.15f) * hs, Quaternion.identity, 16, 8, 0.5f, 1f);
                        m.Paint(B(OrcBone.Head), new Color(0.9f, 0.86f, 0.72f), 0.2f).Capsule(head + new Vector3(0, 0.19f, 0.07f) * hs, head + new Vector3(0, 0.25f, 0.08f) * hs, 0.014f * hs, 0.013f * hs, 6, 1);
                        m.Paint(B(OrcBone.Head), new Color(1f, 0.7f, 0.25f), 0.2f, 3f).Ellipsoid(head + new Vector3(0, 0.275f, 0.08f) * hs, new Vector3(0.012f, 0.022f, 0.012f) * hs, Quaternion.identity, 6, 4);
                    }
                    break;
                }
            }
        }
    }
}
