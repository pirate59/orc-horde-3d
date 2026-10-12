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

            Sky.Apply(map);
            // Towers wear the biome too: snow on their tops, dust or mud about their bases.
            OrcBiomes.Apply(TowerModels.Material, map.biome, 0.8f);
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
            Sfx.Loop("wind_" + (map.biome ?? "dirt"), 0.3f);
            SmokeTest.AttachIfRequested();
        }
    }
}
