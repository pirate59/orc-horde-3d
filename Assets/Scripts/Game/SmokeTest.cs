using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Unattended check of a real build. Run the player with
    /// <c>-smoketest &lt;dir&gt; [-smokemaps all|id,id]</c> and on each map it places a
    /// layout, photographs the map from above and the fight from the player,
    /// plays the first round at speed, writes a line to summary.txt, then moves
    /// to the next map. Errors land in the player log.
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        const float Speed = 4f, RealSecondsPerMap = 50f;

        static Queue<string> queue;
        static string outDir;

        public static void AttachIfRequested()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-smoketest") outDir ??= args[i + 1];
                if (args[i] == "-smokemaps" && queue == null)
                {
                    var catalog = Session.Catalog;
                    queue = new Queue<string>(args[i + 1] == "all"
                        ? System.Array.ConvertAll(catalog.maps, m => m.id)
                        : args[i + 1].Split(','));
                    // The map already built is the first one tested.
                    if (queue.Count > 0 && queue.Peek() != Session.CurrentId) { Session.Play(queue.Peek()); return; }
                }
            }
            if (outDir != null) new GameObject("SmokeTest").AddComponent<SmokeTest>();
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            var gm = GameManager.I;
            var map = gm.Map;
            yield return null;

            int placed = PlaceLayout(gm);
            var player = PlayerController.I;
            yield return Overhead(map);

            gm.StartRound();
            Time.timeScale = Speed;
            float t = 0;
            bool shot = false;
            while (gm.Phase == Phase.Combat && t < RealSecondsPerMap)
            {
                t += Time.unscaledDeltaTime;
                if (!shot && Enemy.All.Count > 0)
                {
                    // Stand a little up the path from the base and face the nearest orc.
                    var nearest = Enemy.All[0];
                    foreach (var e in Enemy.All)
                        if ((e.transform.position - map.Base).sqrMagnitude < (nearest.transform.position - map.Base).sqrMagnitude) nearest = e;
                    if ((nearest.transform.position - map.Base).magnitude < 35f)
                    {
                        shot = true;
                        Time.timeScale = 1f;
                        var look = nearest.transform.position - player.transform.position;
                        look.y = 0;
                        player.transform.rotation = Quaternion.LookRotation(look);
                        yield return null;
                        Shot($"{map.id}-fight");
                        yield return null;
                        Time.timeScale = Speed;
                    }
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Log($"{map.id,-18} towers={placed} phase={gm.Phase} base={gm.BaseHealth}/{gm.Round.startingHealth} " +
                $"kills={gm.Kills} alive={Enemy.All.Count} spawned={gm.SpawnProgress:P0} links={Links.All.Count}/{map.links.Length} " +
                $"crossings={Enemy.LinkCrossings} real={t:0}s");

            queue?.Dequeue();
            if (queue != null && queue.Count > 0) { Session.Play(queue.Peek()); yield break; }
            Log("done");
            Application.Quit();
        }

        /// <summary>
        /// A layout a first-time player might build: every tower allowance spent on
        /// the tower ground next to the path, nearest the base first, spread out.
        /// </summary>
        static int PlaceLayout(GameManager gm)
        {
            var grid = gm.Grid;
            var cells = new List<Vector2Int>();
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
            {
                if (grid.Kind(c, r) != HexGrid.TowerGround) continue;
                for (int s = 0; s < 6; s++)
                    if (HexGrid.IsPath(grid.Kind(HexGrid.Across(c, r, s)))) { cells.Add(new Vector2Int(c, r)); break; }
            }
            cells.Sort((a, b) => (grid.Center(a) - gm.Map.Base).sqrMagnitude.CompareTo((grid.Center(b) - gm.Map.Base).sqrMagnitude));

            int placed = 0, next = 2;
            foreach (var def in Towers.All)
                for (int i = 0; i < def.Limit && next < cells.Count; i++, next += 4, placed++)
                    Tower.Create(def, grid.Center(cells[next]) + Vector3.up * MapBuilder.TowerGroundHeight);
            return placed;
        }

        /// <summary>The whole map from straight above, with the player's camera stood down for the frame.</summary>
        IEnumerator Overhead(MapData map)
        {
            var player = PlayerController.I.Cam;
            var cam = new GameObject("Overhead").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(map.Depth, map.Width / cam.aspect) / 2 + 2;
            cam.transform.SetPositionAndRotation(new Vector3(map.Width / 2, 80, map.Depth / 2), Quaternion.Euler(90, 0, 0));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.farClipPlane = 200;
            player.enabled = false;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            yield return null;
            Shot($"{map.id}-overhead");
            yield return null;
            RenderSettings.fog = fog;
            player.enabled = true;
            Destroy(cam.gameObject);
        }

        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));

        static void Log(string line)
        {
            Debug.Log("[SmokeTest] " + line);
            File.AppendAllText(Path.Combine(outDir, "summary.txt"), line + "\n");
        }
    }
}
