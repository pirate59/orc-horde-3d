using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Unattended checks of a real build. Errors land in the player log; results in &lt;dir&gt;/summary.txt.
    ///
    /// <c>-smoketest &lt;dir&gt; -smokemaps all|id,id</c> tours maps: on each it places a
    /// layout, photographs the map from above and the fight from the player, and
    /// plays the first round at speed.
    ///
    /// <c>-smoketest &lt;dir&gt; -smokecampaign &lt;maps&gt; [-smokefrom &lt;map&gt;]</c> plays the
    /// campaign from a fresh save (or, with -smokefrom, from the test save where
    /// an earlier run left it) as a towers-only player would: every round in order, spending all
    /// its currency between rounds and retrying a lost round a few times, since
    /// losses pay too. No one shoots, so it is a floor on what a player can do.
    /// </summary>
    public sealed class SmokeTest : MonoBehaviour
    {
        const float TourSpeed = 4f, CampaignSpeed = 6f, RealSecondsPerRound = 120f;
        const int MaxAttempts = 6;
        /// <summary>The bot gunner: a modest shot, not an aimbot.</summary>
        const float BotAccuracy = 0.55f, BotHeadshotShare = 0.3f, BotSightRange = 45f;

        static Queue<string> queue;
        static readonly List<float> frameMs = new List<float>();
        static int peakAlive;

        /// <summary>Mean and 95th-percentile real frame time over the last round, with the most orcs alive at once.</summary>
        static string Perf()
        {
            if (frameMs.Count == 0) return "";
            var sorted = new List<float>(frameMs);
            sorted.Sort();
            float mean = 0;
            foreach (var f in sorted) mean += f;
            mean /= sorted.Count;
            return $"frame={mean:0.0}/{sorted[(int)(sorted.Count * 0.95f)]:0.0}ms peak={peakAlive}";
        }
        static string outDir;
        static bool campaign;
        static int campaignMaps;
        static bool buildTest;
        static int buildStage, expectTowers, expectWalls;
        static bool initialised;
        static bool gunner = true;

        public static void AttachIfRequested()
        {
            if (!initialised)
            {
                initialised = true;
                var args = System.Environment.GetCommandLineArgs();
                var catalog = Session.Catalog;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-smoketest") outDir = args[i + 1];
                    if (args[i] == "-smokebuild")
                    {
                        buildTest = true;
                        queue = new Queue<string>(new[] { args[i + 1] });
                    }
                    if (args[i] == "-smokemaps")
                        queue = new Queue<string>(args[i + 1] == "all"
                            ? System.Array.ConvertAll(catalog.maps, m => m.id)
                            : args[i + 1].Split(','));
                    if (args[i] == "-smokenogun") gunner = false;
                    if (args[i] == "-smokecampaign")
                    {
                        campaign = true;
                        campaignMaps = int.Parse(args[i + 1]);
                        // -smokefrom picks up the test save where an earlier run left it, at the named map.
                        int from = System.Array.IndexOf(args, "-smokefrom");
                        int start = from >= 0 && from + 1 < args.Length ? System.Array.IndexOf(catalog.campaign, args[from + 1]) : -1;
                        queue = new Queue<string>(System.Array.FindAll(catalog.campaign, id => catalog.CampaignIndex(id) >= Mathf.Max(start, 0)));
                        if (start < 0)
                        {
                            Progression.Reset();
                            Session.ResetUnlocks();
                        }
                    }
                }
                if (System.Array.IndexOf(args, "-smokenogun") >= 0) gunner = false;
                if (outDir == null) return;
                Directory.CreateDirectory(outDir);
                // The map already built is the first one tested.
                if (queue != null && queue.Count > 0 && queue.Peek() != Session.CurrentId) { Session.Play(queue.Peek()); return; }
            }
            if (outDir != null) new GameObject("SmokeTest").AddComponent<SmokeTest>();
        }

        IEnumerator Start()
        {
            yield return null;
            if (buildTest)
            {
                // Never let a broken scenario hold the process open.
                Invoke(nameof(GiveUp), 60f);
                yield return BuildScenario();
                if (buildStage == 1) { Session.Play(GameManager.I.Map.id); yield break; }
                Log("done");
                Application.Quit();
                yield break;
            }
            yield return campaign ? Campaign() : Tour();

            queue?.Dequeue();
            bool more = queue != null && queue.Count > 0 && (!campaign || --campaignMaps > 0);
            if (more && (!campaign || GameManager.I.Phase == Phase.MapHeld)) { Session.Play(queue.Peek()); yield break; }
            Log("done");
            Application.Quit();
        }

        IEnumerator Tour()
        {
            var gm = GameManager.I;
            var map = gm.Map;
            int placed = PlaceLayout(gm);
            yield return Overhead(map);
            gm.ShopOpen = true;
            yield return null;
            Shot($"{map.id}-skills");
            yield return null;
            gm.ShopOpen = false;
            float t = 0;
            yield return PlayRound(gm, TourSpeed, true, s => t = s);
            Log($"{map.id,-18} towers={placed} phase={gm.Phase} base={gm.BaseHealth}/{gm.MaxBaseHealth} " +
                $"kills={gm.Kills} alive={Enemy.All.Count} spawned={gm.SpawnProgress:P0} links={Links.All.Count}/{map.links.Length} " +
                $"crossings={Enemy.LinkCrossings} real={t:0}s");
        }

        IEnumerator Campaign()
        {
            var gm = GameManager.I;
            var map = gm.Map;
            while (true)
            {
                for (int attempt = 1; ; attempt++)
                {
                    int bought = SpendEverything();
                    int placed = PlaceLayout(gm);
                    int before = Progression.Currency;
                    float t = 0;
                    yield return PlayRound(gm, CampaignSpeed, attempt == 1 && gm.RoundIndex == 0, s => t = s);
                    bool won = gm.Phase == Phase.Won || gm.Phase == Phase.MapHeld;
                    var reward = gm.LastReward;
                    Log($"{map.id,-18} r{gm.RoundIndex + 1} try{attempt} {(won ? "WON " : gm.Phase == Phase.Lost ? "lost" : "TIME")} " +
                        $"base={gm.BaseHealth}/{gm.MaxBaseHealth} kills={gm.Kills} earned={(reward?.Total ?? 0),6} " +
                        $"bank={Progression.Currency,8} bought={bought,2} towers={placed,2} skills={SkillLevels(),3} tough=x{gm.Toughness:0.0} {Perf()} stranded={Enemy.Stranded} real={t:0}s" +
                        (gm.Phase == Phase.Lost ? $"  [{gm.LastLossReason}]" : ""));
                    if (gm.Phase == Phase.Combat) LogStall(gm);
                    if (won) break;
                    if (attempt >= MaxAttempts || gm.Phase == Phase.Combat) { Log($"stuck on {map.id} round {gm.RoundIndex + 1}"); yield break; }
                    gm.Continue(); // back to build for a retry
                    yield return null;
                }
                if (gm.Phase == Phase.MapHeld) yield break;
                gm.Continue(); // on to the next round
                yield return null;
            }
        }

        /// <summary>Starts the round and runs it at speed until it ends or the real-time cap.</summary>
        IEnumerator PlayRound(GameManager gm, float speed, bool photograph, System.Action<float> realSeconds)
        {
            var player = PlayerController.I;
            var map = gm.Map;
            gm.StartRound();
            Time.timeScale = speed;
            float t = 0;
            bool shot = !photograph;
            var bot = campaign && gunner ? new BotGunner() : null;
            frameMs.Clear();
            peakAlive = 0;
            while (gm.Phase == Phase.Combat && t < RealSecondsPerRound)
            {
                t += Time.unscaledDeltaTime;
                frameMs.Add(Time.unscaledDeltaTime * 1000f);
                peakAlive = Mathf.Max(peakAlive, Enemy.All.Count);
                bot?.Tick();
                if (!shot && Enemy.All.Count > 0)
                {
                    // Face the orc nearest the base once one is close enough to see.
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
                        Time.timeScale = speed;
                    }
                }
                yield return null;
            }
            Time.timeScale = 1f;
            realSeconds(t);
        }

        /// <summary>
        /// Stands in for a player at the start position: shoots the nearest orc it
        /// can see at the rifle's real rate, hitting a little over half the time,
        /// reloads, and drops the Mortar and Bombing Run on the thickest crowd.
        /// A boss in sight takes priority for both, as a player soon learns: armour
        /// shrugs off tower fire, and a headshot is the hardest hit in the game.
        /// Against a shielded boss it flanks: its shots come from the boss's side,
        /// as a player stepping off the road to get round the shield would.
        /// Shots go through the same damage path as the player's own.
        /// </summary>
        sealed class BotGunner
        {
            float nextShot, nextThink;
            int ammo = PlayerWeapon.MagazineSize;

            public void Tick()
            {
                var player = PlayerController.I;
                if (!PlayerHealth.I.Alive) return;
                var eye = player.Cam.transform.position;

                while (Time.time >= nextShot)
                {
                    nextShot = Mathf.Max(nextShot, Time.time - 0.5f) + PlayerWeapon.FireInterval / Progression.RifleFireRate;
                    if (ammo == 0) { ammo = PlayerWeapon.MagazineSize; nextShot += PlayerWeapon.ReloadSeconds; break; }
                    var target = Nearest(eye, bossesOnly: true) ?? Nearest(eye, bossesOnly: false);
                    if (target == null) break;
                    ammo--;
                    var look = target.AimPoint - eye;
                    player.transform.rotation = Quaternion.LookRotation(new Vector3(look.x, 0, look.z));
                    var from = target.Def.ShieldArcDegrees > 0 ? target.transform.position + target.transform.right * 6f : eye;
                    if (Random.value < BotAccuracy) target.TakeDamage(PlayerWeapon.Damage * Progression.RifleDamage, Random.value < BotHeadshotShare, from);
                }

                if (Time.time < nextThink) return;
                nextThink = Time.time + 0.5f;
                var boss = Nearest(eye, bossesOnly: true);
                if (boss != null && PlayerAbilities.I.Fire(Abilities.Mortar, boss.transform.position + boss.Velocity * 0.9f, Vector3.forward)) return;
                var crowd = Crowd(Abilities.Mortar.Radius, out int count);
                if (count >= 5)
                {
                    var forward = GameManager.I.Map.Base - crowd;
                    forward.y = 0;
                    var abilities = PlayerAbilities.I;
                    if (!abilities.Fire(Abilities.Mortar, crowd, forward.normalized) && count >= 8)
                        abilities.Fire(Abilities.BombingRun, crowd - forward.normalized * 4f, forward.normalized);
                }
            }

            static Enemy Nearest(Vector3 eye, bool bossesOnly)
            {
                Enemy best = null;
                float bestDist = BotSightRange;
                foreach (var e in Enemy.All)
                {
                    if (!e.Targetable || (bossesOnly && !e.Def.ShowsHealthBar)) continue;
                    float d = Vector3.Distance(eye, e.AimPoint);
                    if (d >= bestDist) continue;
                    if (Physics.Linecast(eye, e.AimPoint, Layers.RockMask | Layers.WallMask, QueryTriggerInteraction.Ignore)) continue;
                    bestDist = d;
                    best = e;
                }
                return best;
            }

            /// <summary>The orc with the most others within <paramref name="radius"/>, among those in sight range.</summary>
            static Vector3 Crowd(float radius, out int count)
            {
                var eye = PlayerController.I.Cam.transform.position;
                count = 0;
                var at = Vector3.zero;
                foreach (var e in Enemy.All)
                {
                    if (!e.Targetable || Vector3.Distance(eye, e.transform.position) > BotSightRange) continue;
                    int n = 0;
                    foreach (var o in Enemy.All)
                        if ((o.transform.position - e.transform.position).sqrMagnitude < radius * radius) n++;
                    if (n > count) { count = n; at = e.transform.position; }
                }
                return at;
            }
        }

        /// <summary>
        /// Buys until nothing is affordable, cheapest first, with unlocks and
        /// capacity favoured: more guns before better guns, as most players go.
        /// </summary>
        static int SpendEverything()
        {
            int bought = 0;
            while (true)
            {
                SkillNode best = null;
                float bestScore = float.MaxValue;
                foreach (var n in Progression.Offered)
                {
                    if (!Progression.CanBuy(n)) continue;
                    var next = Progression.NextLevel(n);
                    bool more = System.Array.Exists(next.effects, e => e.kind == "unlock_tower" || e.kind == "tower_capacity_delta");
                    float score = next.cost * (more ? 0.5f : 1f);
                    if (score < bestScore) { bestScore = score; best = n; }
                }
                if (best == null || !Progression.Buy(best)) return bought;
                bought++;
            }
        }

        /// <summary>
        /// The build phase through its own API, from the tactical view: towers on
        /// the tower ground by the path, a barricade line across the first route,
        /// spots that must be refused, then a reload to check the layout comes back.
        /// </summary>
        IEnumerator BuildScenario()
        {
            var gm = GameManager.I;
            var pc = PlayerController.I;
            var map = gm.Map;

            if (buildStage == 1)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                bool ok = Tower.All.Count == expectTowers && Barricade.All.Count == expectWalls;
                Log($"build: after reload {Tower.All.Count}/{expectTowers} towers, {Barricade.All.Count}/{expectWalls} barricades restored -> {(ok ? "PASS" : "FAIL")}");
                Log($"build: routes {Routes()}");
                pc.SetTactical(false);
                yield return null;
                Shot($"{map.id}-build-onfoot");

                // Then fight it, to see the horde deal with the line: around it, or through it.
                CancelInvoke(nameof(GiveUp));
                Invoke(nameof(GiveUp), 150f);
                float t = 0;
                yield return PlayRound(gm, TourSpeed, false, s => t = s);
                int standing = 0;
                foreach (var b in Barricade.All) if (b.Standing) standing++;
                Log($"build: fought round 1 -> {gm.Phase} base={gm.BaseHealth}/{gm.MaxBaseHealth} kills={gm.Kills} alive={Enemy.All.Count} " +
                    $"barricades standing {standing}/{Barricade.All.Count} stranded={Enemy.Stranded} real={t:0}s");
                if (gm.Phase == Phase.Combat) LogStall(gm);
                buildStage = 2;
                yield break;
            }

            BuildController.ClearLayout();
            pc.SetTactical(true);
            pc.FocusTactical(map.Base, 60f);
            yield return new WaitForSecondsRealtime(0.6f);
            Log($"build: tactical={pc.Tactical} routes before {Routes()}");
            Shot($"{map.id}-build-empty");

            // Towers through the API, on tower ground beside the path near the base.
            var grid = gm.Grid;
            var cells = new List<Vector2Int>();
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
                if (grid.Kind(c, r) == HexGrid.TowerGround)
                    for (int s = 0; s < 6; s++)
                        if (HexGrid.IsPath(grid.Kind(HexGrid.Across(c, r, s)))) { cells.Add(new Vector2Int(c, r)); break; }
            cells.Sort((a, b) => (grid.Center(a) - map.Base).sqrMagnitude.CompareTo((grid.Center(b) - map.Base).sqrMagnitude));
            int towers = 0;
            for (int i = 2; i < cells.Count && towers < 6; i += 3)
                if (BuildController.PlaceTower(Towers.MachineGun, cells[i]) != null) towers++;

            // Refusals: a tower on the path, a second tower on a taken cell, a barricade on tower ground.
            var pathCell = grid.CellAt(map.Base + (map.Base - map.World(map.spawns[0].x, map.spawns[0].y)).normalized * -8f);
            string onPath = BuildController.TowerProblem(Towers.MachineGun, pathCell);
            string taken = Tower.All.Count > 0 ? BuildController.TowerProblem(Towers.MachineGun, Tower.All[0].Cell) : "n/a";
            string wallOnGround = BuildController.BarricadeProblem(cells[0], 0, true);
            Log($"build: refused tower-on-path=[{onPath}] tower-on-tower=[{taken}] wall-on-ground=[{wallOnGround}]");

            // A barricade line across the first route, at the middle of its longest straight.
            yield return new WaitForSecondsRealtime(0.5f);
            int walls = 0;
            var route = RoutePreview.I.Routes.Count > 0 ? RoutePreview.I.Routes[0] : null;
            if (route != null)
            {
                var pts = route.PointsCopy();
                int longest = 1;
                for (int i = 2; i < pts.Count; i++)
                    if (Vector3.Distance(pts[i - 1], pts[i]) > Vector3.Distance(pts[longest - 1], pts[longest])) longest = i;
                var at = (pts[longest - 1] + pts[longest]) / 2;
                var ahead = pts[longest] - pts[longest - 1];
                ahead.y = 0;
                var across = Vector3.Cross(Vector3.up, ahead.normalized);
                // The grid axis that runs most nearly across the road, then barricades end to end along it.
                int axis = 0;
                for (int a = 1; a < HexGrid.AxisSides.Length; a++)
                    if (Mathf.Abs(Vector3.Dot(grid.AxisDirection(a), across)) > Mathf.Abs(Vector3.Dot(grid.AxisDirection(axis), across))) axis = a;
                var mid = grid.CellAt(at);
                for (int k = -3; k <= 3; k++)
                    if (BuildController.PlaceBarricade(HexGrid.Step(mid, axis, k * Walls.Barricade.Cells), axis, ignorePlayer: true) != null) walls++;
                pc.FocusTactical(at, 30f);
            }
            yield return new WaitForSecondsRealtime(1.2f);
            Log($"build: placed {towers} towers, {walls} barricades; routes after {Routes()}");
            Shot($"{map.id}-build-line");

            expectTowers = Tower.All.Count;
            expectWalls = Barricade.All.Count;
            buildStage = 1;
        }

        void GiveUp()
        {
            Log("build: TIMED OUT");
            Application.Quit();
        }

        static string Routes()
        {
            var parts = new List<string>();
            foreach (var r in RoutePreview.I.Routes) parts.Add($"{r.Gate.name}:{r.Length:0}m{(r.Sealed ? ":SEALED" : "")}");
            return string.Join(" ", parts);
        }

        static Vector3 PointAlong(List<Vector3> pts, float distance)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (distance <= seg) return Vector3.Lerp(pts[i - 1], pts[i], seg > 0 ? distance / seg : 0);
                distance -= seg;
            }
            return pts[pts.Count - 1];
        }

        /// <summary>When a round runs out of real time, what the orcs still alive are doing, and where.</summary>
        static void LogStall(GameManager gm)
        {
            var states = new Dictionary<string, int>();
            var places = new Dictionary<Vector2Int, int>();
            foreach (var e in Enemy.All)
            {
                states[e.DebugState] = states.TryGetValue(e.DebugState, out var n) ? n + 1 : 1;
                var p = e.transform.position;
                var key = new Vector2Int(Mathf.RoundToInt(p.x / 5) * 5, Mathf.RoundToInt(p.z / 5) * 5);
                places[key] = places.TryGetValue(key, out var m) ? m + 1 : 1;
            }
            var top = new List<KeyValuePair<Vector2Int, int>>(places);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            string where = string.Join(" ", top.GetRange(0, Mathf.Min(5, top.Count)).ConvertAll(kv => $"({kv.Key.x},{kv.Key.y})x{kv.Value}"));
            string links = string.Join(" ", Links.All.ConvertAll(l => $"{l.Data.id}:entry({l.Entry.x:0},{l.Entry.z:0})->exit({l.Exit.x:0},{l.Exit.z:0})"));
            Log($"  stall: {string.Join(" ", new List<string>(System.Linq.Enumerable.Select(states, kv => kv.Key + "=" + kv.Value)))} | at {where} | {links}");
            for (int i = 0; i < Mathf.Min(4, Enemy.All.Count); i++) Log("    " + Enemy.All[i].DebugDetail);
            Shot($"{gm.Map.id}-stall");
        }

        static int SkillLevels()
        {
            int total = 0;
            foreach (var n in Progression.Offered) total += Progression.Level(n.id);
            return total;
        }

        /// <summary>
        /// A layout a first-time player might build: every tower allowance spent on
        /// the tower ground next to the path, nearest the base first, spread out.
        /// Rebuilt from scratch, so it always matches the current limits.
        /// </summary>
        static int PlaceLayout(GameManager gm)
        {
            BuildController.ClearLayout();
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
                for (int i = 0; i < Progression.Tower(def).Limit && next < cells.Count; i++, next += 3, placed++)
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
