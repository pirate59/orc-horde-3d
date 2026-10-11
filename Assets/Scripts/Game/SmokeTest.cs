using System.Collections;
using System.IO;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Headless-ish check of a real build: run the player with <c>-smoketest &lt;dir&gt;</c>
    /// and it places a layout, plays the first rounds at speed, writes
    /// screenshots and a summary, then quits. Errors land in Player.log.
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        string outDir;

        public static void AttachIfRequested()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-smoketest")
                    new GameObject("SmokeTest").AddComponent<SmokeTest>().outDir = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            var gm = GameManager.I;
            var map = gm.Map;
            yield return null;

            // A line like a first-time player's: guns on both shoulders of the first straight, one barricade.
            var road = map.Road[0];
            float north = road.yMax + map.ShoulderDepth / 2, south = road.yMin - map.ShoulderDepth / 2;
            Tower.Create(Towers.MachineGun, new Vector3(14, MapBuilder.ShoulderHeight, north));
            Tower.Create(Towers.MachineGun, new Vector3(24, MapBuilder.ShoulderHeight, south));
            Tower.Create(Towers.Cannon, new Vector3(20, MapBuilder.ShoulderHeight, north));
            Tower.Create(Towers.ArcTower, new Vector3(28.5f, MapBuilder.ShoulderHeight, 34f));
            Tower.Create(Towers.FlameThrower, new Vector3(28.5f, MapBuilder.ShoulderHeight, 45f));
            Tower.Create(Towers.Laser, new Vector3(50, MapBuilder.ShoulderHeight, 32f));
            Barricade.Create(Walls.Barricade, new Vector3(20, 0, road.center.y - 2.5f), Quaternion.Euler(0, 90, 0));

            var player = PlayerController.I;
            player.Teleport(new Vector3(40f, 0, 26f));
            player.transform.rotation = Quaternion.Euler(0, -90, 0);
            Shot("01-build");

            for (int round = 0; round < 2; round++)
            {
                gm.StartRound();
                Time.timeScale = 3f;
                float t = 0;
                bool midShot = false;
                while (gm.Phase == Phase.Combat && t < 120f)
                {
                    t += Time.unscaledDeltaTime;
                    if (!midShot && Enemy.All.Count > 12)
                    {
                        midShot = true;
                        Time.timeScale = 1f;
                        yield return null;
                        Shot($"0{round + 2}-round{round + 1}-fight");
                        Time.timeScale = 3f;
                    }
                    yield return null;
                }
                Time.timeScale = 1f;
                Log($"round {round + 1}: phase={gm.Phase} base={gm.BaseHealth}/{gm.Round.StartingHealth} kills={gm.Kills} " +
                    $"alive={Enemy.All.Count} barricadeHp={Barricade.All[0].Health:0} realSeconds={t:0.0}");
                if (gm.Phase != Phase.Won) break;
                gm.Continue();
                yield return null;
            }

            Log("done");
            Application.Quit();
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));

        void Log(string line)
        {
            Debug.Log("[SmokeTest] " + line);
            File.AppendAllText(Path.Combine(outDir, "summary.txt"), line + "\n");
        }
    }
}
