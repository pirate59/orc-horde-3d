using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>The light a map is fought under.</summary>
    public enum Mood { Day, Dusk, Night, Overcast }

    /// <summary>
    /// The sky and the light: a procedural sky dome (Shaders/Sky.shader), a sun
    /// or moon that casts the shadows, ambient light and fog to match, and the
    /// bloom that makes glowing things glow. Each map has its own mood, picked
    /// to suit it; the pause menu can override it.
    ///
    /// Night is moonlight: a cold blue key with long soft shadows, a deep blue
    /// haze, stars, and warm torches at the gates and the base so there is
    /// something to steer by.
    /// </summary>
    public static class Sky
    {
        const string OverrideKey = "orc-horde-fps.mood";

        /// <summary>Every campaign map's mood. Five nights in fourteen, spread so no two fall together.</summary>
        static Mood MoodFor(string mapId) => mapId switch
        {
            "canyon_approach" or "lane_yard" or "mesa_canyon" or "draw_green" => Mood.Dusk,
            "twin_gullies" or "basin_chute" or "yard_road" or "drift_trench" or "rimefall_terraces" => Mood.Night,
            "horseshoe_basin" or "hollow_fold" => Mood.Overcast,
            _ => Mood.Day,
        };

        /// <summary>What the pause menu has forced, or null to let each map choose.</summary>
        public static Mood? Override
        {
            get
            {
                int v = PlayerPrefs.GetInt(OverrideKey, -1);
                return v < 0 ? (Mood?)null : (Mood)v;
            }
            set
            {
                PlayerPrefs.SetInt(OverrideKey, value.HasValue ? (int)value.Value : -1);
                PlayerPrefs.Save();
            }
        }

        public static Mood Current { get; private set; }

        /// <summary>One mood's every setting.</summary>
        sealed class Look
        {
            public Vector3 SunAngles;
            public Color Sun, AmbientSky, AmbientEquator, AmbientGround, Fog, Zenith, Horizon, Below;
            public float SunIntensity, ShadowStrength, FogDensity, Curve;
            public Color Disc, Halo;
            public float DiscSize, HaloPower, HaloStrength, Moon, Stars;
            public Color CloudLit, CloudShade;
            public float CloudCover;
            public float BloomThreshold, BloomIntensity, Vignette;
            /// <summary>Ambient occlusion's strength, and the light shafts' strength and colour.</summary>
            public float Occlusion = 0.7f, Shafts;
            public Color ShaftColor = Color.white;
            public Color Tint = Color.white;
            public bool Torches;
        }

        static Look LookFor(Mood mood) => mood switch
        {
            Mood.Night => new Look
            {
                // The moon high in the south-east, so shadows run long across the roads.
                SunAngles = new Vector3(30, 140, 0), Sun = U.Hex(0x8aa8ff), SunIntensity = 0.78f, ShadowStrength = 0.88f,
                AmbientSky = U.Hex(0x26386a), AmbientEquator = U.Hex(0x182644), AmbientGround = U.Hex(0x0a0e1a),
                Fog = U.Hex(0x13213e), FogDensity = 0.014f,
                Zenith = U.Hex(0x040916), Horizon = U.Hex(0x22386a), Below = U.Hex(0x0a0e18), Curve = 0.4f,
                Disc = U.Hex(0xeef3ff), DiscSize = 0.04f, Halo = U.Hex(0x5a84e0), HaloPower = 40, HaloStrength = 0.5f,
                Moon = 1, Stars = 1,
                CloudLit = U.Hex(0x4a5d86), CloudShade = U.Hex(0x0e1526), CloudCover = 0.28f,
                BloomThreshold = 0.8f, BloomIntensity = 1.0f, Vignette = 1.1f, Tint = new Color(0.92f, 0.98f, 1.1f),
                Torches = true, Occlusion = 0.55f, Shafts = 0.5f, ShaftColor = U.Hex(0x8aa8ff),
            },
            Mood.Dusk => new Look
            {
                SunAngles = new Vector3(11, -62, 0), Sun = U.Hex(0xffa868), SunIntensity = 1.15f, ShadowStrength = 0.8f,
                AmbientSky = U.Hex(0x6a5a80), AmbientEquator = U.Hex(0x8a5a48), AmbientGround = U.Hex(0x2a1e1a),
                Fog = U.Hex(0xb8705c), FogDensity = 0.009f,
                Zenith = U.Hex(0x23305e), Horizon = U.Hex(0xe0875a), Below = U.Hex(0x3a2622), Curve = 0.55f,
                Disc = U.Hex(0xffe0a8), DiscSize = 0.05f, Halo = U.Hex(0xff8a40), HaloPower = 14, HaloStrength = 0.65f,
                CloudLit = U.Hex(0xffb080), CloudShade = U.Hex(0x4a3458), CloudCover = 0.5f,
                BloomThreshold = 0.85f, BloomIntensity = 0.6f, Vignette = 0.8f, Tint = new Color(1.05f, 0.98f, 0.92f),
                Torches = true, Occlusion = 0.7f, Shafts = 0.55f, ShaftColor = U.Hex(0xffc090),
            },
            Mood.Overcast => new Look
            {
                SunAngles = new Vector3(58, -30, 0), Sun = U.Hex(0xdfe4ea), SunIntensity = 0.8f, ShadowStrength = 0.45f,
                AmbientSky = U.Hex(0x8e96a2), AmbientEquator = U.Hex(0x737a82), AmbientGround = U.Hex(0x3a3c40),
                Fog = U.Hex(0x98a2ad), FogDensity = 0.013f,
                Zenith = U.Hex(0x6c7886), Horizon = U.Hex(0xa3adb8), Below = U.Hex(0x4a4e54), Curve = 0.8f,
                Disc = U.Hex(0xf2f2ee), DiscSize = 0.03f, Halo = U.Hex(0xe8ecf0), HaloPower = 8, HaloStrength = 0.25f,
                CloudLit = U.Hex(0xc8ced6), CloudShade = U.Hex(0x737c87), CloudCover = 0.88f,
                BloomThreshold = 0.95f, BloomIntensity = 0.45f, Vignette = 0.7f,
                Occlusion = 0.85f, Shafts = 0.1f, ShaftColor = U.Hex(0xe0e4e8),
            },
            _ => new Look
            {
                SunAngles = new Vector3(48, -40, 0), Sun = U.Hex(0xfff0d6), SunIntensity = 1.25f, ShadowStrength = 0.75f,
                AmbientSky = U.Hex(0x8ea6c8), AmbientEquator = U.Hex(0x7a7e80), AmbientGround = U.Hex(0x3a352e),
                Fog = U.Hex(0xb4c6d8), FogDensity = 0.0065f,
                Zenith = U.Hex(0x3d70c0), Horizon = U.Hex(0xb8cde2), Below = U.Hex(0x5a5c5e), Curve = 0.5f,
                Disc = U.Hex(0xfffbe8), DiscSize = 0.028f, Halo = U.Hex(0xfff0c8), HaloPower = 160, HaloStrength = 0.9f,
                CloudLit = U.Hex(0xffffff), CloudShade = U.Hex(0x98a4b4), CloudCover = 0.42f,
                BloomThreshold = 1f, BloomIntensity = 0.45f, Vignette = 0.6f,
                Occlusion = 0.7f, Shafts = 0.35f, ShaftColor = U.Hex(0xfff4e0),
            },
        };

        /// <summary>Nudges a mood towards the biome: colder air over snow, dustier over canyon.</summary>
        static void Biome(Look l, string biome, Mood mood)
        {
            if (biome == "snow" && mood != Mood.Night)
            {
                l.Fog = Color.Lerp(l.Fog, U.Hex(0xcad8ea), 0.3f);
                l.Horizon = Color.Lerp(l.Horizon, U.Hex(0xcad8ea), 0.3f);
            }
            if (biome == "canyon" && mood == Mood.Day)
            {
                l.Fog = Color.Lerp(l.Fog, U.Hex(0xd29a6c), 0.3f);
                l.Horizon = Color.Lerp(l.Horizon, U.Hex(0xd29a6c), 0.3f);
            }
            if (biome == "snow" && mood == Mood.Night) l.Tint = new Color(0.9f, 0.98f, 1.12f);
        }

        static GameObject root;
        static Material skyMaterial;

        /// <summary>Sets the scene's light, sky and fog for a map. Safe to call again: the old light goes first.</summary>
        public static void Apply(MapData map, Mood? force = null)
        {
            Current = force ?? Override ?? MoodFor(map.id);
            var l = LookFor(Current);
            Biome(l, map.biome, Current);
            if (root != null) Object.Destroy(root);
            root = new GameObject("Sky");

            var sun = new GameObject(Current == Mood.Night ? "Moon" : "Sun").AddComponent<Light>();
            sun.transform.SetParent(root.transform, false);
            sun.type = LightType.Directional;
            sun.color = l.Sun;
            sun.intensity = l.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = l.ShadowStrength;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(l.SunAngles);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.AmbientSky;
            RenderSettings.ambientEquatorColor = l.AmbientEquator;
            RenderSettings.ambientGroundColor = l.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = l.Fog;
            RenderSettings.fogDensity = l.FogDensity;

            if (skyMaterial == null) skyMaterial = new Material(Shader.Find("OrcHorde/Sky"));
            var m = skyMaterial;
            m.SetColor("_Zenith", l.Zenith);
            // The horizon is the fog, so distant ground melts into the sky without a line.
            m.SetColor("_Horizon", Color.Lerp(l.Horizon, l.Fog, 0.5f));
            m.SetColor("_Ground", l.Below);
            m.SetFloat("_Curve", l.Curve);
            m.SetVector("_SunDir", -sun.transform.forward);
            m.SetColor("_SunColor", l.Disc * (Current == Mood.Night ? 1.05f : 2.2f));
            m.SetFloat("_SunSize", l.DiscSize);
            m.SetColor("_HaloColor", l.Halo);
            m.SetFloat("_HaloPower", l.HaloPower);
            m.SetFloat("_HaloStrength", l.HaloStrength);
            m.SetFloat("_Moon", l.Moon);
            m.SetFloat("_Stars", l.Stars);
            m.SetColor("_CloudColor", l.CloudLit);
            m.SetColor("_CloudShade", l.CloudShade);
            m.SetFloat("_CloudCover", l.CloudCover);
            RenderSettings.skybox = m;

            // Shadows crisp near the player and reaching across most of a map.
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 90f;
            QualitySettings.shadowCascade4Split = new Vector3(0.05f, 0.15f, 0.4f);

            PostFx.Settings = new PostFx.Grade
            {
                Threshold = l.BloomThreshold, Intensity = l.BloomIntensity, Vignette = l.Vignette, Tint = l.Tint,
                Occlusion = l.Occlusion, ShaftStrength = l.Shafts, ShaftColor = l.ShaftColor,
            };

            if (l.Torches) PlaceTorches(map, Current == Mood.Night ? 1f : 0.6f);
            Motes.Create(root.transform, map.biome, Current);
        }

        /// <summary>A pair of torches at every gate and four round the base: warm light against the cold.</summary>
        static void PlaceTorches(MapData map, float strength)
        {
            foreach (var s in map.spawns)
            {
                var gate = map.World(s.x, s.y);
                var across = Vector3.Cross(Vector3.up, map.Inward(gate));
                foreach (var side in new[] { -1f, 1f }) Torch.Create(root.transform, gate + map.Inward(gate) * 2f + across * side * 3.2f, strength);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4;
                Torch.Create(root.transform, map.Base + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 4.5f, strength);
            }
        }

        /// <summary>A post with a flame on it, and a warm light that flickers.</summary>
        sealed class Torch : MonoBehaviour
        {
            Light glow;
            float seed, strength;

            public static void Create(Transform parent, Vector3 at, float strength)
            {
                at = Relief.OnGround(at);
                var go = new GameObject("Torch");
                go.transform.SetParent(parent, false);
                go.transform.position = at;
                Shapes.Prim(PrimitiveType.Cylinder, go.transform, Vector3.up * 0.9f, new Vector3(0.12f, 0.9f, 0.12f), Mats.Lit(U.Hex(0x3a2a1e)));
                Shapes.Prim(PrimitiveType.Cylinder, go.transform, Vector3.up * 1.82f, new Vector3(0.22f, 0.06f, 0.22f), Mats.Lit(U.Hex(0x2a2c30)));
                Shapes.Prim(PrimitiveType.Sphere, go.transform, Vector3.up * 1.98f, new Vector3(0.2f, 0.32f, 0.2f), Mats.Lit(U.Hex(0xffa040), 3f));
                var t = go.AddComponent<Torch>();
                t.strength = strength;
                t.seed = Random.value * 10f;
                t.glow = new GameObject("Flame").AddComponent<Light>();
                t.glow.transform.SetParent(go.transform, false);
                t.glow.transform.localPosition = Vector3.up * 2.2f;
                t.glow.color = U.Hex(0xffa050);
                t.glow.range = 10f;
                t.glow.shadows = LightShadows.None;
            }

            void Update()
            {
                float f = Mathf.PerlinNoise(seed, Time.time * 6f);
                glow.intensity = strength * (1.4f + f * 0.8f);
                if (Random.value < 0.08f)
                    Fx.Puff(transform.position + Vector3.up * 2.1f, Vector3.up * Random.Range(0.6f, 1.2f), 0.12f,
                        new Color(1f, Random.Range(0.45f, 0.7f), 0.15f, 0.7f), 0.35f);
            }
        }
    }
}
