# Orc Horde: FPS

A first-person tower defence game built on [Orc Horde](../orc-horde)'s designs. You build a line of towers and barricades between rounds, then fight in it with a rifle while the horde comes down the road.

Unity **6000.6.4f1**, built-in render pipeline, legacy Input Manager, no packages. The world and the orcs are built at runtime from code. The only art assets are textures baked into `Assets/Resources/Art` (see **Art**).

## Play

Open the project in Unity Hub and open `Assets/Scenes/QuarryRoad.unity` (it opens on its own the first time). Then press Play. The game starts on the last map played; Esc → **Choose map** lists the campaign. Each map unlocks when you hold the one before it, and the two test grounds are always open.

| | |
| --- | --- |
| Move | WASD, Shift to sprint, Space to jump |
| Shoot | Left mouse, R to reload. Headshots do ×2 |
| Aim down the sight | Hold right mouse: zoomed view through the holo reticle, tighter grouping, less recoil, slower movement, no sprint |
| Abilities | Hold Q or E to aim the ability in that hand, release to fire (Rallying Cry fires on the press) |
| Build (between rounds) | 1–5 for towers, 6 for barricade, R to turn a barricade, left click to place, F to pick up and move, X or right click to remove, 0 to put away |
| Tactical view (between rounds) | M. WASD pan, Q/E turn, wheel zoom, place with the mouse, T to drop in on foot there |
| Route preview | V toggles the lines from each gate to the base; red means a road is sealed and orcs will break through |
| Skill web (between rounds) | B or Tab; drag to pan, wheel to zoom, click a node |
| Start the wave / continue | G |
| Help / pause | H / Esc |

## What came from Orc Horde

Ported from `orc-horde/src/content`. The original numbers are kept in their original units and converted with `U.M()` (1 unit = 4 cm) and `U.Sec()`:

- **Towers:** Machine Gun, Cannon (lobs over barricades), Laser (pierces and ricochets), Arc Tower (slows), Flame Thrower (cone)
- **Enemies:** Fodder, Runner, Armoured, Siege Elite, Siege Breaker, Siege Boss, Sapper, Shaman, Splitter, with armour using the original quarter-damage floor
- **Barricade:** a run of 3 hex cells along one of the grid's three axes, as in Orc Horde; 8 per layout, carves the navmesh with its ends fused to the rock or the next wall, restored on every attempt
- **Abilities:** all nine, two carried at a time, one per hand: Mortar, Bombing Run, Emergency Repairs, Oil Slick, Rallying Cry, Sentry Drop, Proximity Mines, Artillery Barrage and Decoy Beacon, with every stat upgrade and trait (Rebuild, Tar, Twin and Cannon Sentries, Chain Reaction, Ignore Armour, Incendiary, Static Field, Overload, Fool Bosses). The loadout is chosen in the skill web.
- **Base shields:** each layer soaks one breach whatever it costs, and spent layers come back one at a time (5 s, quicker with Recharge). A boss that walks in still takes the round.
- **Maps:** all 14 campaign maps and both test grounds, with every round, spawn gate, boss event, bridge and tunnel
- **Bosses:** Siege Boss, Ram Chief (charges), Shieldwall (frontal shield), Warlord (summons a war band), Blood Totem (heals and regenerates)
- **Progression:** Orc Horde's skill web, drawn as it is there: nodes at their authored positions, joined by their requirements, panned and zoomed beside a detail panel. Owned and open nodes are named, the next ring out is a locked outline, and anything further is hidden until you get close. Each node is outlined in what it costs: gold currency, blue Valor or violet Glory. Kills pay currency win or lose; a win adds kills × (1 + 5% per campaign round), doubled on a first clear. Valor comes from the first win of each boss round, and Glory from winning every round of a map without the base taking a hit. Blueprints open their shortcuts. An FPS-only Rifle line, hung off Gun Nests, keeps your own gun in step.

  Not yet brought over: tower traits (Twin Mounts, Cluster Shells, Split Beam, Chain Arc, Napalm and the rest), Expansion's longer barricades, towers on barricades, Thorns and Last Stand. Their nodes show in the web, greyed, and can't be bought.

Deliberate deviations are marked `DEVIATION` in the code. The main ones:

- **Horde size.** Orc Horde's budgets grow nearly 600× over the campaign. On Quarry Road and Canyon Approach a round fields 12% of the original at the opener and grows as budget^0.85 (`GameManager.FieldedPressure`). After that, Orc Horde's linear growth would outrun the player: towers cap at 38 by Twin Gullies, and upgrades get steeply dearer. So from Horseshoe Basin on, each round fields a set share of the player's expected firepower. `Tools/difficulty.py` models that firepower round by round, from Orc Horde's payouts and the skill tree, and writes `Assets/Resources/Difficulty.json`. Each map opens at about 40–50% of what the player can stop and builds to a finale at 74–82%. Both shares rise gently over the campaign. Edit the constants at the top of the script and re-run it to retune.
- **Toughness.** Past `GameManager.BodyBudget` (800 pressure), extra pressure becomes toughness: the same number of orcs drawn from the budget, each with ×T health (heals scale too). Bosses and other set pieces sit outside the budget, as in Orc Horde, and keep their own health. The HUD shows toughness during a round.
- **Rewards.** Each kill pays original budget ÷ spawned budget times its Orc Horde reward, so currency per round matches Orc Horde's.
- **Sealed roads.** Units that normally avoid walls will chew through a fully sealed road.

## Build phase

Before every wave you have as long as you like. Walk the map, or press M for the tactical view, and lay out the line. The panel on the right briefs the next wave: which gates it uses and how long each walk is, the enemy mix and about how many orcs, bosses and when they arrive, and the horde's toughness. The route lines update as you build, so you can see a barricade line reroute the horde, or seal a road (red) and force a fight at the wall. Placed towers show their reach while you build.

Each map's layout is saved as you change it and restored when you come back, as in Orc Horde. Esc → **Clear layout** starts afresh.

Currency, skills, unlocked maps and layouts are saved in PlayerPrefs. Esc → **Reset campaign** starts over.

## Maps

The maps aren't copied by hand. `Tools/export-maps.sh` bundles Orc Horde's own map code with its esbuild, runs the hex layout pass, and writes each map to `Assets/Resources/Maps.json` as a character grid (one character per hex), plus spawns, base, links and rounds. Re-run it whenever Orc Horde's maps change:

```bash
Tools/export-maps.sh ~/code/orc-horde/branches/claude-new-build
```

In 3D, path cells are open floor and tower-ground cells are a raised ledge (a tower stands at the centre of one, as in Orc Horde). The remaining rock is cliff. Bridges carry orcs over a lane on a deck you can see, and tunnels hide them in the rock until they come out the other side.

### Relief

DEVIATION: Orc Horde's maps are flat. Here a smooth height field (`World/Relief.cs`) rolls under each map, seeded by its id so it's the same every time. Roads climb and dip by up to about 6 m and never get steeper than about 25°, inside what the orcs' navmesh allows. Tower ledges stand at least 0.35 m above the road beside them, in 0.4 m terraces: one step is walkable, two need a jump, and orcs can never climb one. The land is level around gates, the base, bridges and tunnels, so everything placed at a fixed height there works as on a flat map.

The hills matter in a fight:

- **Height is reach.** A tower's range scales with how high it stands above the road it covers: +12% per metre, from −20% in a dip to +30% on a high ledge. An average ledge is level, so height trades reach between spots rather than adding it overall. The placement ghost's ring and hint show the effect before you build, and inspecting a tower shows it after.
- **The land blocks sight.** An orc over a crest is hidden from towers and your rifle, and Machine Gun bullets strike the ground. The Arc's pulse won't reach through a hill. The Cannon lobs over everything, so it covers blind spots. The Laser still fires without sight, as before, but its beam now runs at chest height over the land, up and down the road.

Across the campaign, 5–13% of the road within a tower's reach is hidden by the land.

## Art

**Textures.** Orc Horde → Bake Textures (`Assets/Editor/TextureBaker.cs`) writes every texture with a normal map derived from its height:
- the path and tower ground: Orc Horde's own painted biome atlases (grassland, canyon, snow, dirt), quartered
- cliff faces: Worley rock in strata
- props: riveted metal, planks, stone blocks, and hide for the orcs

Terrain uses world-space UVs and a textured vertex-lit shader that keeps per-cell light and dark.

**Sky and light.** Each map is fought under its own mood (`World/Sky.cs`), set to suit it. The pause menu's Sky setting can force one everywhere.
- **Day:** high sun, blue sky, drifting cumulus, crisp shadows.
- **Dusk:** low orange sun, long shadows, a warm haze, torches lit at the gates and base.
- **Night:** a cold blue moon with long soft shadows, stars, deep blue haze. Torches burn warm at the gates and around the base, and fireflies drift over grass.
- **Overcast:** flat grey light, heavy cloud, soft shadows.

The sky is a procedural shader (`Shaders/Sky.shader`): a gradient dome, a sun or a cratered moon with its halo, fbm clouds lit from the light's side, and twinkling stars. Its horizon matches the fog, so the land fades into the sky without a seam. Snow maps get snowfall in every mood. A small bloom (`Core/Bloom.cs`, `Shaders/Bloom.shader`) makes emissive things glow, with each mood setting its own threshold, strength, tint and vignette: tower lights, muzzle flashes, beacons, the moon. Shadows use four cascades out to 90 m. None of this costs frame time with a full horde.

**Orcs.** Each type is one procedurally built skinned mesh on a shared 20-bone skeleton (`Assets/Scripts/Actors/Orcs`), designed after Orc Horde's sprite briefs: build, palette, armour, weapon. One draw call per orc, skinned on the GPU. `OrcAnimator` drives the bones with no animation clips:
- a walk cycle paced by actual speed
- an overhead swing while attacking
- a crouch while winding up
- a flinch on hits
- a fall on death; corpses lie a few seconds, then sink

The head hitbox rides the head bone, and each orc gets a slight random tint. A full 110-orc horde holds 60 fps.

**Biome variants.** Every orc dresses for the map's biome (`OrcBiomes`), built once per type per biome:
- **Snow:** fur mantles, collars, hats and cuffs, warmer wool, white snow settled on shoulders, helms and weapons, snow-caked boots, and breath puffing in the cold.
- **Canyon:** sand scarves and head-wraps, sun-bleached cloth, dust on the shoulders, red dust up the legs.
- **Grassland:** leaf camouflage, vine wraps, feathers, moss, mud.
- **Quarry dirt:** belt lanterns, candle caps, a coil of rope, stone dust, deep mud.

The settled cover and the leg grime are shader overlays, so towers wear them too, more lightly.

**Towers and rifle.** `TowerModels` builds each tower from the same solids, shaped by how it fights. Each turns and tilts to aim, and sweeps slowly when idle:
- **Machine Gun:** rotary barrels spin up; belt feed; casings fly out.
- **Cannon:** elevates to lob, kicks back, smokes.
- **Laser:** the crystal charges between shots and flares when the beam goes.
- **Arc Tower:** the toroid turns; bolts strike every orc in the pulse.
- **Flame Thrower:** the pilot light flickers.

Orcs show what is happening to them: a flash on a hit, an orange flicker and embers while burning, a blue shimmer while slowed. `GunModels` builds the rifle, which has recoil, flash, ejected brass, and a reload in which the magazine drops out and a fresh one seats.

## Sound

Every sound is synthesised by Orc Horde → Bake Sounds (`Assets/Editor/SoundBaker.cs`) and written to `Assets/Resources/Audio` as WAV. Drop in a recording with the same name to replace one; numbered files (`orc_grunt_1`, `_2` ...) are variants picked at random. There are 69 files:
- **Weapons:** rifle shot, reload and sight, hit and headshot ticks; the machine gun, cannon, explosions, laser, arc and flame
- **Orcs:** grunts and deaths whose pitch follows size, weapon swings, boss roars, blows on barricades and their collapse, the Shaman's heal
- **The round:** war horns for the wave and for bosses, the base breach alarm, win and loss stings, the mortar's whistle
- **Interface:** placing, removing, invalid, buying and clicking
- **Player:** footsteps for gravel, snow and grass, and landing
- **Wind:** a seamless loop for each biome

`Sfx` plays them positioned in the world, with pitch variation, and limits how many of each sound play at once, how soon one may repeat, and how far it carries, so a full horde stays readable. Esc → Volume sets the master level.

## Layout

| Path | Purpose |
| --- | --- |
| `Assets/Scripts/Content` | Definitions, ported content, map data model |
| `Assets/Resources/Maps.json` | Exported Orc Horde maps (generated) |
| `Tools/` | Map exporter |
| `Assets/Scripts/World` | Hex grid, terrain meshes, navmesh, bridges and tunnels |
| `Assets/Scripts/Actors` | Enemies, towers, barricades, projectiles; `Orcs/` holds the orc meshes, rig, designs and animator |
| `Assets/Scripts/Player` | Movement, rifle, health, abilities, build mode |
| `Assets/Scripts/Game` | Bootstrap, round loop, HUD, smoke test |
| `Assets/Editor/ProjectSetup.cs` | Writes the scene, layers and build settings |
| `Assets/Editor/TextureBaker.cs` | Bakes the textures |
| `Assets/Editor/SoundBaker.cs` | Synthesises the sounds |
| `Assets/Resources/Audio` | Baked sounds (generated) |
| `Assets/Shaders` | Terrain and orc shaders |

## Smoke tests

Build a macOS player first:

```bash
Unity -batchmode -quit -projectPath . -executeMethod ProjectSetup.BuildMac
```

**Map tour:** on each map it places a layout, plays round 1 at 4× speed, and writes overhead and fight screenshots plus a summary line to `Logs/smoke/`:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/smoke" -smokemaps all
```

**Campaign balance:** plays the first N campaign maps from a fresh save, towers only. It spends all its currency between rounds and retries a lost round up to four times. Nobody shoots, so the result is a floor on what a real player can do:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/campaign" -smokecampaign 4
```

Each line logs the round's result, currency, skills bought, toughness, mean/95th-percentile frame time and the most orcs alive at once. Add `-smokenogun` for a towers-only run.

**Build phase:** on one map, from the tactical view, it places towers and a barricade line through the same API the mouse uses. It checks that invalid spots are refused, reloads the map to confirm the layout comes back, then plays the round against the line:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/build" -smokebuild quarry_road
```

**Orc gallery:** every orc type lined up and photographed walking, side-on, swinging and falling, plus two close-ups:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/gallery" -smokegallery canyon_approach
```

**Relief:** on each listed map it places a layout, lets the horde in, then freezes it and photographs the whole map, a climb, a descent and a view from a ledge, with range rings showing. It logs the road's height range and steepest slope, whether every route is still open, the range factor height gives every buildable spot, and how much of the road in reach the land hides:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/relief" -smokerelief canyon_approach,draw_green
```

**Abilities:** photographs the skill web on a fresh save and a grown one, then unlocks every ability and trait, lets the horde in, fires each ability into it and logs what it did (kills, slowed, lured, sentries, the rally, barricade health) with a photo of each:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/abilities" -smokeabilities canyon_approach
```

**Sky:** on each listed map, places a layout, lets the horde in, then photographs it under every mood: the whole map, the road from behind the horde, and up at the sun or moon. Night is shot again without bloom:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/sky" -smokesky canyon_approach,rimefall_terraces
```

**Armoury:** all five towers firing at immortal puppet orcs, then a close-up of each tower and the rifle idle, firing and reloading:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/armory" -smokearmory canyon_approach
```

**Stress:** 110 orcs of mixed kinds walking the map at normal speed, with frame times measured:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/stress" -smokestress canyon_approach
```

Test runs keep their own save slot and never touch your campaign.
