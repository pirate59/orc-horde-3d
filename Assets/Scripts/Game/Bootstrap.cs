using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Builds the whole game at runtime from content: map, lighting, navmesh,
    /// player and HUD. The scene holds nothing but this.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        void Awake()
        {
            var map = Session.Current;
            var grid = new HexGrid(map);

            SetUpLighting(map);
            var world = new GameObject("World").transform;
            MapBuilder.Build(map, grid, world);
            MapBuilder.BakeNavMesh(map);
            Links.Connect(map);

            var gm = new GameObject("Game").AddComponent<GameManager>();
            var (start, yaw) = MapBuilder.PlayerStart(map, grid);
            PlayerController.Create(start, yaw);
            gm.Init(map, grid);
            gm.gameObject.AddComponent<Hud>();
            gm.gameObject.AddComponent<RoutePreview>();
            Layouts.Restore(map.id);
            SmokeTest.AttachIfRequested();
        }

        /// <summary>A dusk light, tinted by the map's biome: cold over snow, warm over canyon.</summary>
        static void SetUpLighting(MapData map)
        {
            var (sunColor, sky) = map.biome switch
            {
                "snow" => (U.Hex(0xdfe8ff), U.Hex(0x3a4250)),
                "canyon" => (U.Hex(0xffc28a), U.Hex(0x3a2e2a)),
                "grassland" => (U.Hex(0xfff0c8), U.Hex(0x2c3530)),
                _ => (U.Hex(0xffd2a8), U.Hex(0x2a2f3a)),
            };
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = sunColor;
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38, -50, 0);

            var dusk = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = U.Hex(0x5a6478);
            RenderSettings.ambientEquatorColor = U.Hex(0x4a4440);
            RenderSettings.ambientGroundColor = U.Hex(0x221e1a);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = dusk;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.skybox = null;
        }
    }
}
