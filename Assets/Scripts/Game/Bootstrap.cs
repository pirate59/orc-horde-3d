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
            var map = Maps.QuarryRoad();

            SetUpLighting();
            var world = new GameObject("World").transform;
            MapBuilder.Build(map, world);
            MapBuilder.BakeNavMesh(map);

            var gm = new GameObject("Game").AddComponent<GameManager>();
            PlayerController.Create(map.PlayerStart, map.PlayerYaw);
            gm.Init(map);
            gm.gameObject.AddComponent<Hud>();
            SmokeTest.AttachIfRequested();
        }

        static void SetUpLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = U.Hex(0xffd2a8);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38, -50, 0);

            var dusk = U.Hex(0x2a2f3a);
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
