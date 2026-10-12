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
        static bool buildTest, gallery, stress, armory, relief, abilitiesTest, skyTest, baseTest;
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
                    if (args[i] == "-smokearmory")
                    {
                        armory = true;
                        queue = new Queue<string>(new[] { args[i + 1] });
                    }
                    if (args[i] == "-smokestress")
                    {
                        stress = true;
                        queue = new Queue<string>(new[] { args[i + 1] });
                    }
                    if (args[i] == "-smokegallery")
                    {
                        gallery = true;
                        queue = new Queue<string>(new[] { args[i + 1] });
                    }
                    if (args[i] == "-smokebuild")
                    {
                        buildTest = true;
                        queue = new Queue<string>(new[] { args[i + 1] });
                    }
                    if (args[i] == "-smokeabilities")
                    {
                        abilitiesTest = true;
                        queue = new Queue<string>(new[] { args[i + 1] });
                    }
                    if (args[i] == "-smokebase")
                    {
                        baseTest = true;
                        queue = new Queue<string>(args[i + 1].Split(','));
                    }
                    if (args[i] == "-smokesky")
                    {
                        skyTest = true;
                        queue = new Queue<string>(args[i + 1].Split(','));
                    }
                    if (args[i] == "-smokerelief")
                    {
                        relief = true;
                        queue = new Queue<string>(args[i + 1].Split(','));
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
                if (System.Array.IndexOf(args, "-smokenocaves") >= 0) Landscape.CavesOn = false;
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
            if (armory)
            {
                Invoke(nameof(GiveUp), 120f);
                yield return Armory();
                Log("done");
                Application.Quit();
                yield break;
            }
            if (abilitiesTest)
            {
                Invoke(nameof(GiveUp), 150f);
                yield return AbilitiesTest();
                Log("done");
                Application.Quit();
                yield break;
            }
            if (stress)
            {
                Invoke(nameof(GiveUp), 90f);
                yield return Stress();
                Log("done");
                Application.Quit();
                yield break;
            }
            if (gallery)
            {
                Invoke(nameof(GiveUp), 90f);
                yield return Gallery();
                Log("done");
                Application.Quit();
                yield break;
            }
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
            yield return campaign ? Campaign() : relief ? ReliefShots() : skyTest ? SkyShots() : baseTest ? BaseShots() : Tour();

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

        /// <summary>
        /// Every orc type lined up in front of the player, walking in place:
        /// photographed wide and close, mid-swing, and falling.
        /// </summary>
        IEnumerator Gallery()
        {
            var gm = GameManager.I;
            var pc = PlayerController.I;
            BuildController.ClearLayout();
            gm.HelpOpen = false;
            var start = pc.transform.position;
            var fwd = pc.transform.forward;
            var right = pc.transform.right;

            var small = new[] { Enemies.Fodder, Enemies.Runner, Enemies.Armoured, Enemies.Sapper, Enemies.Shaman, Enemies.Splitter, Enemies.SiegeElite };
            var big = new[] { Enemies.SiegeBreaker, Enemies.SiegeBoss, Enemies.RamChief, Enemies.Shieldwall, Enemies.Warlord, Enemies.BloodTotem };

            foreach (var (row, name, distance, spacing) in new[] { (small, "small", 7f, 1.6f), (big, "bosses", 12f, 3.4f) })
            {
                var line = new List<Enemy>();
                for (int i = 0; i < row.Length; i++)
                {
                    var at = start + fwd * distance + right * (i - (row.Length - 1) / 2f) * spacing;
                    var e = Enemy.Spawn(row[i], at, 0f);
                    if (e == null) { Log($"gallery: could not place {row[i].Name}"); continue; }
                    e.transform.position = at;
                    e.Puppet(-fwd, walk: true);
                    line.Add(e);
                }
                pc.transform.rotation = Quaternion.LookRotation(fwd);
                yield return new WaitForSeconds(0.4f);
                yield return Snap($"gallery-{name}-walk-a");
                yield return new WaitForSeconds(0.27f);
                yield return Snap($"gallery-{name}-walk-b");

                // Three-quarter view of the line, from the side.
                foreach (var e in line) e.Puppet(right, walk: true);
                yield return new WaitForSeconds(0.35f);
                Log($"gallery: {name} side {line[0].Animator.DebugState}");
                yield return Snap($"gallery-{name}-side");

                // Swinging.
                foreach (var e in line) { e.Puppet(-fwd, walk: false); e.Animator.Attacking = true; }
                yield return new WaitForSeconds(0.45f);
                Log($"gallery: {name} attack {line[0].Animator.DebugState}");
                yield return Snap($"gallery-{name}-attack");

                foreach (var e in line) e.DieForGallery();
                yield return new WaitForSeconds(0.4f);
                yield return Snap($"gallery-{name}-fall");
                yield return new WaitForSeconds(1.2f);
                yield return Snap($"gallery-{name}-down");
                yield return new WaitForSeconds(3f);
            }

            // A close look at two faces.
            foreach (var def in new[] { Enemies.Fodder, Enemies.Armoured })
            {
                var e = Enemy.Spawn(def, start + fwd * 2.2f, 0f);
                Log($"gallery: close {def.Id} spawned={e != null} alive={Enemy.All.Count} at={(e != null ? e.transform.position.ToString() : "-")} player={pc.transform.position}");
                e.transform.position = start + fwd * 2.2f;
                e.Puppet(-fwd, walk: false);
                pc.transform.rotation = Quaternion.LookRotation(fwd);
                yield return new WaitForSeconds(0.5f);
                yield return Snap($"gallery-close-{def.Id}");
                e.Die(killed: false);
                yield return null;
            }
            Log($"gallery: done on {gm.Map.biome}, vertices fodder={OrcDesigns.Get(Enemies.Fodder, gm.Map.biome).mesh.vertexCount} boss={OrcDesigns.Get(Enemies.SiegeBoss, gm.Map.biome).mesh.vertexCount}");
        }

        /// <summary>
        /// The towers and the rifle at work: all five towers on tower ground,
        /// immortal orcs walking in place in front of them so every tower fires,
        /// photographed wide and one by one, then the rifle idle, firing and
        /// halfway through a reload.
        /// </summary>
        IEnumerator Armory()
        {
            var gm = GameManager.I;
            var pc = PlayerController.I;
            var grid = gm.Grid;
            BuildController.ClearLayout();
            Hud.Hidden = true;
            Tower.AlwaysArmed = true;
            var start = pc.transform.position;
            var fwd = pc.transform.forward;
            var right = pc.transform.right;
            var focus = start + fwd * 12f;

            // Tower ground cells near the focus, spread apart, nearest first.
            var cells = new List<Vector2Int>();
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
                if (grid.Kind(c, r) == HexGrid.TowerGround) cells.Add(new Vector2Int(c, r));
            cells.Sort((a, b) => (grid.Center(a) - focus).sqrMagnitude.CompareTo((grid.Center(b) - focus).sqrMagnitude));
            var towers = new List<Tower>();
            foreach (var def in Towers.All)
            {
                foreach (var cell in cells)
                {
                    var at = BuildController.TowerPosition(cell);
                    bool clear = true;
                    foreach (var t in towers) if ((t.transform.position - at).magnitude < 4.5f) clear = false;
                    if (!clear) continue;
                    towers.Add(Tower.Create(def, at));
                    break;
                }
            }

            // Targets: orcs walking in place on the path among the towers.
            var targets = new List<Enemy>();
            var kinds = new[] { Enemies.Fodder, Enemies.Armoured, Enemies.Runner, Enemies.SiegeElite, Enemies.Fodder, Enemies.Shaman };
            for (int i = 0; i < 10; i++)
            {
                var near = focus + right * Random.Range(-6f, 6f) + fwd * Random.Range(-3f, 5f);
                var e = Enemy.Spawn(kinds[i % kinds.Length], near, 0.5f);
                if (e == null) continue;
                e.Puppet(-fwd + right * Random.Range(-0.3f, 0.3f), walk: true);
                targets.Add(e);
            }
            Log($"armory: towers={towers.Count} targets={targets.Count}");

            pc.Teleport(start - fwd * 2f);
            pc.SetView(Quaternion.LookRotation(fwd).eulerAngles.y, 8f);
            yield return new WaitForSeconds(1.6f);
            yield return Snap("armory-wide");

            foreach (var t in towers)
            {
                // Stand off to one side of the tower, a little above it, looking at it.
                var toward = (focus - t.transform.position);
                toward.y = 0;
                var side = Vector3.Cross(Vector3.up, toward.normalized);
                var eye = t.transform.position - toward.normalized * 2.4f + side * 2.6f;
                eye.y = 0;
                pc.Teleport(eye);
                var look = t.transform.position + Vector3.up * 1.1f - (eye + Vector3.up * 1.65f);
                pc.SetView(Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y,
                    -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
                yield return new WaitForSeconds(Mathf.Max(0.6f, t.Stats.FireInterval * 0.85f));
                yield return Snap($"armory-{t.Def.Id}");
            }

            // The rifle: idle, mid-burst, and with the magazine out.
            Tower.AlwaysArmed = false;
            pc.Teleport(start);
            pc.SetView(Quaternion.LookRotation(fwd).eulerAngles.y, 4f);
            yield return new WaitForSeconds(0.5f);
            yield return Snap("armory-rifle-idle");
            Hud.Hidden = false;
            PlayerWeapon.I.DebugAim(true);
            yield return new WaitForSeconds(0.4f);
            Log($"armory: aim={PlayerWeapon.I.Aim:F2} fov={pc.Cam.fieldOfView:F1}");
            yield return Snap("armory-rifle-ads");
            for (int i = 0; i < 3; i++) { PlayerWeapon.I.DebugFire(); yield return new WaitForSeconds(0.1f); }
            PlayerWeapon.I.DebugFire();
            yield return Snap("armory-rifle-ads-fire");
            PlayerWeapon.I.DebugAim(false);
            yield return new WaitForSeconds(0.4f);
            Hud.Hidden = true;
            for (int i = 0; i < 4; i++) { PlayerWeapon.I.DebugFire(); yield return new WaitForSeconds(0.1f); }
            PlayerWeapon.I.DebugFire();
            yield return Snap("armory-rifle-fire");
            yield return new WaitForSeconds(0.4f);
            PlayerWeapon.I.StartReload();
            yield return new WaitForSeconds(0.6f);
            yield return Snap("armory-rifle-reload");
            Hud.Hidden = false;
        }

        /// <summary>
        /// The worst case for rendering and AI: a full 110 orcs of every kind
        /// walking the map at normal speed, no towers, frame times measured.
        /// </summary>
        IEnumerator Stress()
        {
            var gm = GameManager.I;
            BuildController.ClearLayout();
            gm.HelpOpen = false;
            var kinds = new[] { Enemies.Fodder, Enemies.Fodder, Enemies.Fodder, Enemies.Runner, Enemies.Armoured, Enemies.Shaman, Enemies.SiegeElite, Enemies.Splitter };
            for (int i = 0; i < 110; i++)
            {
                var gate = gm.Map.spawns[i % gm.Map.spawns.Length];
                Enemy.Spawn(kinds[i % kinds.Length], gm.Map.World(gate.x, gate.y), 6f);
                if (i % 10 == 9) yield return null;
            }
            yield return new WaitForSeconds(4f);
            var pc = PlayerController.I;
            frameMs.Clear();
            peakAlive = 0;
            float t = 0;
            bool shot = false;
            while (t < 12f)
            {
                t += Time.unscaledDeltaTime;
                frameMs.Add(Time.unscaledDeltaTime * 1000f);
                peakAlive = Mathf.Max(peakAlive, Enemy.All.Count);
                if (!shot && t > 6f && Enemy.All.Count > 0)
                {
                    shot = true;
                    var look = Enemy.All[0].transform.position - pc.transform.position;
                    look.y = 0;
                    pc.transform.rotation = Quaternion.LookRotation(look);
                    yield return Snap($"{gm.Map.id}-stress");
                }
                yield return null;
            }
            Log($"stress: {gm.Map.id} {Perf()} vertices={Enemy.All.Count * OrcDesigns.Get(Enemies.Fodder, gm.Map.biome).mesh.vertexCount} (approx)");
        }

        /// <summary>A screenshot, waiting for it to be taken: the capture happens at the end of the frame, so anything changed before then would be in it.</summary>
        static IEnumerator Snap(string name)
        {
            Shot(name);
            yield return new WaitForEndOfFrame();
            yield return null;
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
            BuildController.Quiet = true;
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
                    Tower.Create(def, Relief.TowerSpot(cells[next]));
            return placed;
        }

        /// <summary>
        /// The lie of the land: towers placed, the horde let in for a while, then
        /// frozen and photographed from an oblique view of the whole map, and up and
        /// down its steepest stretch of road.
        /// </summary>
        IEnumerator ReliefShots()
        {
            var gm = GameManager.I;
            var map = gm.Map;
            var grid = gm.Grid;
            int placed = PlaceLayout(gm);

            // The road's highest and lowest cells, and its steepest triangle.
            Vector2Int top = default, low = default;
            float topH = float.MinValue, lowH = float.MaxValue, steepest = 0, ledgeMin = float.MaxValue, ledgeMax = float.MinValue;
            var corners = grid.CornerOffsets();
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
            {
                var cell = new Vector2Int(c, r);
                var centre = grid.Center(cell);
                if (grid.Kind(cell) == HexGrid.TowerGround)
                {
                    float lift = Relief.TowerGround(cell) - Relief.CellPeak(cell);
                    ledgeMin = Mathf.Min(ledgeMin, lift);
                    ledgeMax = Mathf.Max(ledgeMax, lift);
                }
                if (!HexGrid.IsPath(grid.Kind(cell))) continue;
                float h = Relief.Ground(centre);
                if (h > topH) { topH = h; top = cell; }
                if (h < lowH) { lowH = h; low = cell; }
                for (int i = 0; i < 6; i++)
                {
                    var a = Relief.OnGround(centre);
                    var b = Relief.OnGround(centre + corners[i]);
                    var d = Relief.OnGround(centre + corners[(i + 1) % 6]);
                    float slope = Vector3.Angle(Vector3.Cross(b - a, d - a).normalized * Mathf.Sign(Vector3.Cross(b - a, d - a).y), Vector3.up);
                    steepest = Mathf.Max(steepest, slope);
                }
            }

            float least = float.MaxValue, most = float.MinValue, sum = 0;
            foreach (var tw in Tower.All) { least = Mathf.Min(least, tw.RangeFactor); most = Mathf.Max(most, tw.RangeFactor); sum += tw.RangeFactor; }

            gm.StartRound();
            Time.timeScale = 3f;
            float t = 0, sample = 0;
            int inReach = 0, hidden = 0;
            while (gm.Phase == Phase.Combat && t < 25f && Enemy.All.Count < 45)
            {
                t += Time.unscaledDeltaTime;
                if ((sample -= Time.unscaledDeltaTime) <= 0)
                {
                    sample = 0.5f;
                    foreach (var tw in Tower.All)
                    foreach (var e in Enemy.All)
                    {
                        if ((e.transform.position - tw.transform.position).sqrMagnitude > tw.Range * tw.Range) continue;
                        inReach++;
                        if (tw.HiddenByLand(e)) hidden++;
                    }
                }
                yield return null;
            }
            Time.timeScale = 0f;
            Hud.Hidden = true;

            var routes = RoutePreview.I != null ? RoutePreview.I.Routes : new List<RoutePreview.Route>();
            int sealedRoutes = routes.FindAll(r => r.Sealed).Count;
            Log($"{map.id,-18} towers={placed} road={lowH:0.0}..{topH:0.0}m steepest={steepest:0}° ledges=+{ledgeMin:0.00}..+{ledgeMax:0.00}m " +
                $"orcs={Enemy.All.Count} routes={routes.Count} sealed={sealedRoutes} stranded={Enemy.Stranded}");
            if (Tower.All.Count > 0)
                Log($"{map.id,-18} height range x{least:0.00}..x{most:0.00} mean x{sum / Tower.All.Count:0.00}  " +
                    $"orcs hidden by the land: {hidden}/{inReach} ({(inReach > 0 ? hidden / (float)inReach : 0):P0}) of tower-orc pairs in reach");
            // Every buildable spot beside the road, not just where the bot built: what height
            // would give a tower there, and how much of the road in reach the land hides from it.
            float spotLeast = float.MaxValue, spotMost = float.MinValue, spotSum = 0;
            int spots = 0, roadSeen = 0, roadHidden = 0, boosted = 0;
            float reachBase = Towers.MachineGun.Range;
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
            {
                var cell = new Vector2Int(c, r);
                if (grid.Kind(cell) != HexGrid.TowerGround) continue;
                bool byRoad = false;
                for (int i = 0; i < 6; i++) byRoad |= HexGrid.IsPath(grid.Kind(HexGrid.Across(c, r, i)));
                if (!byRoad) continue;
                float f = Relief.RangeFactor(cell, reachBase);
                spotLeast = Mathf.Min(spotLeast, f); spotMost = Mathf.Max(spotMost, f); spotSum += f; spots++;
                if (f >= 1.1f) boosted++;
                var eye = Relief.TowerSpot(cell) + Vector3.up * 1.2f;
                int reach = Mathf.CeilToInt(reachBase * f / (grid.Apothem * 1.5f)) + 1;
                for (int dc = -reach; dc <= reach; dc++)
                for (int dr = -reach; dr <= reach; dr++)
                {
                    var road = new Vector2Int(c + dc, r + dr);
                    if (!HexGrid.IsPath(grid.Kind(road))) continue;
                    var chest = Relief.RoadSpot(road) + Vector3.up * 1.1f;
                    if ((chest - eye).sqrMagnitude > reachBase * f * reachBase * f) continue;
                    roadSeen++;
                    if (Physics.Linecast(eye, chest, Layers.TerrainMask, QueryTriggerInteraction.Ignore)) roadHidden++;
                }
            }
            if (spots > 0)
                Log($"{map.id,-18} spots={spots} height range x{spotLeast:0.00}..x{spotMost:0.00} mean x{spotSum / spots:0.00} " +
                    $"+10%-or-better={boosted / (float)spots:P0}  road in reach hidden by land={(roadSeen > 0 ? roadHidden / (float)roadSeen : 0):P0}");
            foreach (var tw in Tower.All) tw.ShowRange(true);

            // Whole map, from high on its south side.
            var mid = new Vector3(map.Width / 2, 0, map.Depth / 2);
            float span = Mathf.Max(map.Width, map.Depth);
            yield return Photo($"{map.id}-relief-wide", mid + new Vector3(0, span * 0.42f, -span * 0.62f), mid, 50f);

            // The steepest stretch: the nearest orc-filled road below the crest, looking up at it, then back down.
            var crest = Relief.OnGround(grid.Center(top));
            Vector3 foot = crest;
            float best = float.MinValue;
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
            {
                var cell = new Vector2Int(c, r);
                if (!HexGrid.IsPath(grid.Kind(cell))) continue;
                var p = Relief.OnGround(grid.Center(cell));
                float run = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(crest.x, 0, crest.z));
                if (run < 10f || run > 26f) continue;
                float score = (crest.y - p.y) - run * 0.02f;
                if (score > best) { best = score; foot = p; }
            }
            var up = (crest - foot);
            var flat = new Vector3(up.x, 0, up.z).normalized;
            var side = Vector3.Cross(Vector3.up, flat);
            yield return Photo($"{map.id}-relief-climb", foot + Vector3.up * 1.7f, crest + Vector3.up * 1.2f, 62f);
            yield return Photo($"{map.id}-relief-descent", crest + Vector3.up * 2.2f, foot, 62f);
            // The same stretch from above and off to one side, so the slope shows in profile.
            var middle = (crest + foot) / 2;
            yield return Photo($"{map.id}-relief-profile", middle + side * 26f + Vector3.up * 14f, middle, 55f);

            // From a high ledge beside the road, as the player stands to build.
            Vector2Int ledge = default;
            float ledgeTop = float.MinValue;
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
            {
                var cell = new Vector2Int(c, r);
                if (grid.Kind(cell) != HexGrid.TowerGround) continue;
                bool byRoad = false;
                for (int i = 0; i < 6; i++) byRoad |= HexGrid.IsPath(grid.Kind(HexGrid.Across(c, r, i)));
                float lift = Relief.TowerGround(cell) - Relief.CellPeak(cell);
                if (byRoad && lift > ledgeTop) { ledgeTop = lift; ledge = cell; }
            }
            var stand = Relief.TowerSpot(ledge);
            Vector3 view = stand + Vector3.forward * 10f;
            float nearest = float.MaxValue;
            foreach (var e in Enemy.All)
            {
                float d = Vector3.Distance(e.transform.position, stand);
                if (d > 6f && d < nearest) { nearest = d; view = e.transform.position; }
            }
            yield return Photo($"{map.id}-relief-ledge", stand + (stand - view).normalized * 3f + Vector3.up * 2.2f, view, 62f);

            Hud.Hidden = false;
            Time.timeScale = 1f;
        }

        /// <summary>
        /// The skill web, fresh and well grown, then every ability fired into a
        /// live horde, each logged by what it did and photographed doing it.
        /// </summary>
        IEnumerator AbilitiesTest()
        {
            var gm = GameManager.I;
            var map = gm.Map;

            // The web on a fresh save: the root and what it opens, the next ring as outlines.
            Progression.Reset();
            gm.ShopOpen = true;
            yield return null;
            yield return Snap("web-fresh");
            // Grown: a mid-campaign purse spent as the bot spends, every ability, Valor and Glory in hand.
            Progression.GrantForTest(400000, 12, 5);
            SpendEverything();
            Progression.GrantAbilitiesForTest();
            Progression.GrantForTest(0, 4, 2);
            yield return null;
            yield return Snap("web-grown");
            gm.ShopOpen = false;
            yield return null;

            int towers = PlaceLayout(gm);
            // A barricade on the road for Emergency Repairs to patch: the first spot, walking out from the base, that takes one.
            var grid = gm.Grid;
            var roads = new List<Vector2Int>();
            for (int c = 0; c < grid.Columns; c++)
            for (int r = grid.FirstRow; r <= grid.LastRow; r++)
                if (HexGrid.IsPath(grid.Kind(c, r))) roads.Add(new Vector2Int(c, r));
            roads.Sort((p, q) => (grid.Center(p) - map.Base).sqrMagnitude.CompareTo((grid.Center(q) - map.Base).sqrMagnitude));
            Barricade wall = null;
            foreach (var cell in roads)
            {
                if ((grid.Center(cell) - map.Base).magnitude < 14f) continue;
                for (int axis = 0; axis < 3 && wall == null; axis++)
                    if (BuildController.BarricadeProblem(cell, axis, ignorePlayer: true) == null)
                        wall = BuildController.PlaceBarricade(cell, axis, ignorePlayer: true);
                if (wall != null) break;
            }
            Log($"abilities: unlocked {string.Join(", ", System.Array.ConvertAll(System.Array.FindAll(Abilities.All, Progression.AbilityUnlocked), a => a.Name))}; loadout {Progression.Loadout[0].Name} / {Progression.Loadout[1].Name}; towers={towers} shields={gm.MaxShields}");
            gm.StartRound();
            Time.timeScale = 2f;
            float t = 0;
            while (gm.Phase == Phase.Combat && Enemy.All.Count < 30 && t < 40f) { t += Time.unscaledDeltaTime; yield return null; }

            var ab = PlayerAbilities.I;
            foreach (var a in Abilities.All)
            {
                if (gm.Phase != Phase.Combat) { Log($"abilities: round ended before {a.Name}"); break; }
                // Into the thick of the horde, on the road.
                Vector3 centre = Vector3.zero;
                foreach (var e in Enemy.All) centre += e.transform.position;
                centre /= Mathf.Max(1, Enemy.All.Count);
                var target = Enemy.All.Count > 0 ? Enemy.All[0].transform.position : map.Base;
                foreach (var e in Enemy.All)
                    if ((e.transform.position - centre).sqrMagnitude < (target - centre).sqrMagnitude) target = e.transform.position;
                var forward = (map.Base - target);
                forward.y = 0;
                forward.Normalize();
                if (a == Abilities.EmergencyRepairs && Barricade.All.Count > 0)
                {
                    // One wall worn down, one knocked flat: Rebuild should raise it.
                    Barricade.All[0].Damage(Barricade.All[0].MaxHealth * 0.6f);
                    target = Barricade.All[0].transform.position;
                }

                int killsBefore = gm.Kills, minesBefore = 0, alive = Enemy.All.Count;
                float wallBefore = 0;
                foreach (var b in Barricade.All) wallBefore += b.Health;
                bool fired = ab.Fire(a, target, forward, force: true);
                Time.timeScale = 1f;
                yield return new WaitForSeconds(a == Abilities.ArtilleryBarrage ? 2.5f : a == Abilities.SentryDrop ? 1.5f : 1.0f);

                int slowed = 0, lured = 0;
                foreach (var e in Enemy.All) { if (e.Lured) lured++; if (e.SlowedNow) slowed++; }
                float wallAfter = 0;
                foreach (var b in Barricade.All) wallAfter += b.Health;
                Time.timeScale = 0f;
                Hud.Hidden = true;
                var look = target - forward * 11f + Vector3.up * 7f;
                yield return Photo($"ability-{a.Id}", look, target, 55f);
                Hud.Hidden = false;
                Log($"abilities: {a.Name,-18} fired={fired} kills+{gm.Kills - killsBefore} alive {alive}->{Enemy.All.Count} slowed={slowed} lured={lured} " +
                    $"sentries={Tower.Sentries.Count} rally=x{1f + AbilityFields.Rally.Damage:0.00} walls {wallBefore:0}->{wallAfter:0}");
                Time.timeScale = 2f;
                yield return new WaitForSeconds(0.5f);
            }
            Time.timeScale = 1f;
        }

        /// <summary>
        /// Every mood on one map, with towers up and the horde on the road: the
        /// whole map from high up, and the view down the road from the player's
        /// height. Night is shot again without bloom, to show what the glow adds.
        /// </summary>
        IEnumerator SkyShots()
        {
            var gm = GameManager.I;
            var map = gm.Map;
            PlaceLayout(gm);
            gm.StartRound();
            Time.timeScale = 3f;
            float t = 0;
            while (gm.Phase == Phase.Combat && t < 25f && Enemy.All.Count < 35) { t += Time.unscaledDeltaTime; yield return null; }
            Time.timeScale = 0f;
            Hud.Hidden = true;

            // From beside the base, looking back up the road at the horde.
            // The front of the horde: the orc nearest the base, which is always on a road.
            Vector3 crowd = map.Base;
            float nearest = float.MaxValue;
            foreach (var e in Enemy.All)
            {
                float d = (e.transform.position - map.Base).sqrMagnitude;
                if (d < nearest && (e.transform.position - map.Base).magnitude > 12f) { nearest = d; crowd = e.transform.position; }
            }
            // On the road behind the horde's front, a little above head height, looking up at it.
            var back = map.Base - crowd;
            back.y = 0;
            // Walk back from the horde towards the base until the spot is clear road, not under a cliff.
            var eye = crowd;
            for (float d = Mathf.Min(16f, back.magnitude * 0.6f); d > 3f; d -= 1f)
            {
                var spot = crowd + back.normalized * d;
                if (!AbilityFields.OnRoad(spot) || Physics.Raycast(Relief.OnGround(spot) + Vector3.up * 0.5f, Vector3.up, 20f, Layers.RockMask)) continue;
                eye = spot;
                break;
            }
            eye = Relief.OnGround(eye) + Vector3.up * 3.2f;
            var mid = new Vector3(map.Width / 2, 0, map.Depth / 2);
            float span = Mathf.Max(map.Width, map.Depth);

            foreach (Mood mood in System.Enum.GetValues(typeof(Mood)))
            {
                Sky.Apply(map, mood);
                yield return null;
                yield return Photo($"{map.id}-{mood.ToString().ToLower()}-wide", mid + new Vector3(span * 0.1f, span * 0.3f, -span * 0.6f), mid, 55f);
                yield return Photo($"{map.id}-{mood.ToString().ToLower()}-road", eye, crowd + Vector3.up * 1.5f, 65f);
                // Up at the sun or moon, over the map.
                var light = -RenderSettings.sun.transform.forward;
                var skyward = new Vector3(light.x, 0, light.z).normalized + Vector3.up * Mathf.Max(0.15f, light.y * 0.8f);
                var from = Relief.OnGround(mid) + Vector3.up * 8f;
                yield return Photo($"{map.id}-{mood.ToString().ToLower()}-sky", from, from + skyward * 10f, 70f);
                if (mood == Mood.Night)
                {
                    PostFx.BloomOn = false;
                    yield return Photo($"{map.id}-night-road-nobloom", eye, crowd + Vector3.up * 1.5f, 65f);
                    PostFx.BloomOn = true;
                }
                if (mood == Mood.Day)
                {
                    PostFx.OcclusionOn = false;
                    yield return Photo($"{map.id}-day-road-noao", eye, crowd + Vector3.up * 1.5f, 65f);
                    PostFx.OcclusionOn = true;
                    for (int view = 1; view <= 3; view++)
                    {
                        PostFx.DebugView = view;
                        yield return Photo($"{map.id}-debug-{(view == 1 ? "ao" : view == 2 ? "shafts" : "depth")}", eye, crowd + Vector3.up * 1.5f, 65f);
                    }
                    PostFx.DebugView = 0;
                }
                if (mood == Mood.Dusk)
                {
                    PostFx.ShaftsOn = false;
                    yield return Photo($"{map.id}-dusk-sky-noshafts", from, from + skyward * 10f, 70f);
                    PostFx.ShaftsOn = true;
                }
                if (mood == Mood.Day)
                {
                    Log($"{map.id,-18} scatter={Scatter.Placed} msaa={QualitySettings.antiAliasing}");
                    // Close in on a ledge by the road, for the grass and the rock beside it.
                    var grid = gm.Grid;
                    var ledge = grid.CellAt(crowd);
                    float best = float.MaxValue;
                    for (int c = 0; c < grid.Columns; c++)
                    for (int r = grid.FirstRow; r <= grid.LastRow; r++)
                    {
                        var cell = new Vector2Int(c, r);
                        if (grid.Kind(cell) != HexGrid.TowerGround) continue;
                        float d = (grid.Center(cell) - crowd).sqrMagnitude;
                        if (d < best) { best = d; ledge = cell; }
                    }
                    var spot = Relief.TowerSpot(ledge);
                    var away = crowd - spot;
                    away.y = 0;
                    // Out through the first gate at its cave, where the horde comes from.
                    var g = map.spawns[0];
                    var gatePos = map.World(g.x, g.y);
                    var inward = map.Inward(gatePos);
                    if (Landscape.EntryPath(g.id) != null)
                    {
                        yield return Photo($"{map.id}-day-cave", gatePos + inward * 7f + Vector3.up * 3f, gatePos - inward * Landscape.CaveDistance + Vector3.up * 2.5f, 60f);
                        var side = Vector3.Cross(Vector3.up, inward);
                        yield return Photo($"{map.id}-day-cave-aerial", gatePos + inward * 12f + side * 14f + Vector3.up * 22f, gatePos - inward * (Landscape.CaveDistance + 4f), 60f);
                    }
                    // Down the length of a tunnel, from a little way out of its entry.
                    if (Links.All.Count > 0)
                    {
                        var tunnel = Links.All.Find(l => l.Tunnel);
                        if (tunnel != null)
                            yield return Photo($"{map.id}-day-tunnel", tunnel.Entry - tunnel.Direction * 9f + Vector3.up * 2.2f, tunnel.Exit + Vector3.up * 1.6f, 60f);
                    }
                    yield return Photo($"{map.id}-day-ledge", spot - away.normalized * 4.5f + Vector3.up * 2.2f, spot + away.normalized * 3f, 60f);
                }
                Log($"{map.id,-18} {mood,-9} fog={RenderSettings.fogColor} sun={RenderSettings.sun.color} x{RenderSettings.sun.intensity:0.00}");
            }
            // A mortar into the horde at night: the blast lights the road, then leaves its scorch.
            Sky.Apply(map, Mood.Night);
            Time.timeScale = 1f;
            Hud.Hidden = true;
            PlayerAbilities.I.Fire(Abilities.Mortar, Relief.OnGround(crowd), Vector3.forward, force: true);
            yield return new WaitForSeconds(0.93f);
            yield return Photo($"{map.id}-night-blast", eye, crowd + Vector3.up * 1.5f, 65f);
            yield return new WaitForSeconds(1.2f);
            yield return Photo($"{map.id}-night-scorch", eye, crowd + Vector3.up * 1.5f, 65f);
            Sky.Apply(map);
            Hud.Hidden = false;
            Time.timeScale = 1f;
        }

        /// <summary>The base by day and at night, then smoking, burning, and under a flaring shield.</summary>
        IEnumerator BaseShots()
        {
            var gm = GameManager.I;
            var map = gm.Map;
            var facing = MapBuilder.BaseFacing;
            var side = Vector3.Cross(Vector3.up, facing);
            var look = map.Base - facing * 5f + Vector3.up * 5f;
            var eye = map.Base + facing * 20f + side * 9f + Vector3.up * 8f;
            var near = map.Base + facing * 9f - side * 4f + Vector3.up * 3f;
            Hud.Hidden = true;
            Sky.Apply(map, Mood.Day);
            yield return new WaitForSeconds(0.5f);
            yield return Photo($"{map.id}-base", eye, look, 55f);
            yield return Photo($"{map.id}-base-near", near, look, 65f);
            Sky.Apply(map, Mood.Night);
            yield return new WaitForSeconds(0.5f);
            yield return Photo($"{map.id}-base-night", eye, look, 55f);
            Sky.Apply(map, Mood.Day);
            gm.SetBaseForTest(0.5f, 0);
            yield return new WaitForSeconds(2.5f);
            yield return Photo($"{map.id}-base-smoking", eye, look, 55f);
            gm.SetBaseForTest(0.2f, 0);
            yield return new WaitForSeconds(2.5f);
            yield return Photo($"{map.id}-base-burning", eye, look, 55f);
            gm.SetBaseForTest(1f, 0);
            yield return new WaitForSeconds(1f);
            Log($"{map.id,-18} base {map.biome} parts restored, shields={gm.MaxShields}");
            if (gm.MaxShields > 0)
            {
                BaseRig.I.Hit(true);
                yield return new WaitForSeconds(0.1f);
                yield return Photo($"{map.id}-base-shield", eye, look, 55f);
            }
            Hud.Hidden = false;
            Sky.Apply(map);
        }

        /// <summary>One photograph from a free camera, with the player's own stood down and fog lifted.</summary>
        IEnumerator Photo(string name, Vector3 from, Vector3 at, float fov)
        {
            var player = PlayerController.I.Cam;
            var cam = new GameObject("Photo").AddComponent<Camera>();
            cam.CopyFrom(player);
            cam.gameObject.AddComponent<PostFx>();
            cam.fieldOfView = fov;
            cam.farClipPlane = 400;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            player.enabled = false;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            yield return null;
            yield return Snap(name);
            RenderSettings.fog = fog;
            player.enabled = true;
            Destroy(cam.gameObject);
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
            if (line == "done" && Sfx.Requested.Count > 0)
            {
                var names = new List<string>(Sfx.Requested.Keys);
                names.Sort();
                Log("sounds: " + string.Join(" ", names.ConvertAll(n => $"{n}={(Sfx.Played.TryGetValue(n, out var p) ? p : 0)}/{Sfx.Requested[n]}")));
            }
            Debug.Log("[SmokeTest] " + line);
            File.AppendAllText(Path.Combine(outDir, "summary.txt"), line + "\n");
        }
    }
}
