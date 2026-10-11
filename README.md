# Orc Horde: FPS

A first-person tower defence game built on [Orc Horde](../orc-horde)'s designs. You build a line of towers and barricades between rounds, then fight in it with a rifle while the horde comes down the road.

Unity **6000.6.4f1**, built-in render pipeline, legacy Input Manager, no packages. The world and the orcs are built at runtime from code. The only art assets are textures baked into `Assets/Resources/Art` (see **Art**).

## Play

Open the project in Unity Hub and open `Assets/Scenes/QuarryRoad.unity` (it opens on its own the first time). Then press Play. The game starts on the last map played; Esc → **Choose map** lists the campaign. Each map unlocks when you hold the one before it, and the two test grounds are always open.

| | |
| --- | --- |
| Move | WASD, Shift to sprint, Space to jump |
| Shoot | Left mouse, R to reload. Headshots do ×2 |
| Abilities | Hold Q (Mortar) or E (Bombing Run) to aim, release to fire |
| Build (between rounds) | 1–5 for towers, 6 for barricade, R to turn a barricade, left click to place, F to pick up and move, X or right click to remove, 0 to put away |
| Tactical view (between rounds) | M. WASD pan, Q/E turn, wheel zoom, place with the mouse, T to drop in on foot there |
| Route preview | V toggles the lines from each gate to the base; red means a road is sealed and orcs will break through |
| Skill tree (between rounds) | B or Tab |
| Start the wave / continue | G |
| Help / pause | H / Esc |

## What came from Orc Horde

Ported from `orc-horde/src/content`. The original numbers are kept in their original units and converted with `U.M()` (1 unit = 4 cm) and `U.Sec()`:

- **Towers:** Machine Gun, Cannon (lobs over barricades), Laser (pierces and ricochets), Arc Tower (slows), Flame Thrower (cone)
- **Enemies:** Fodder, Runner, Armoured, Siege Elite, Siege Breaker, Siege Boss, Sapper, Shaman, Splitter, with armour using the original quarter-damage floor
- **Barricade:** a run of 3 hex cells along one of the grid's three axes, as in Orc Horde; 8 per layout, carves the navmesh with its ends fused to the rock or the next wall, restored on every attempt
- **Abilities:** Mortar and Bombing Run
- **Maps:** all 14 campaign maps and both test grounds, with every round, spawn gate, boss event, bridge and tunnel
- **Bosses:** Siege Boss, Ram Chief (charges), Shieldwall (frontal shield), Warlord (summons a war band), Blood Totem (heals and regenerates)
- **Progression:** the skill tree's currency nodes (tower unlocks, capacity, damage, fire rate, range, splash, slow, cone, barricade count and health, base health, Mortar and Bombing Run upgrades) and the eight 40-level masteries, with Orc Horde's economy. Kills pay win or lose; a win adds kills × (1 + 5% per campaign round), doubled on a first clear. Nodes with effects the FPS doesn't have yet (shields, traits, the seven later abilities) are hidden. An FPS-only Rifle line keeps your own gun in step.

Deliberate deviations are marked `DEVIATION` in the code. The main ones:

- **Horde size.** Orc Horde's budgets grow nearly 600× over the campaign. Here a round fields 12% of the original at Quarry Road's opener, grows as budget^0.85 through the first two maps, then grows linearly in step with Orc Horde from Horseshoe Basin on (`GameManager.FieldedPressure`).
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

In 3D, path cells are open floor and tower-ground cells are a low step (a tower stands at the centre of one, as in Orc Horde). The remaining rock is cliff. Bridges carry orcs over a lane on a deck you can see, and tunnels hide them in the rock until they come out the other side.

## Art

**Textures.** Orc Horde → Bake Textures (`Assets/Editor/TextureBaker.cs`) writes every texture with a normal map derived from its height:
- the path and tower ground: Orc Horde's own painted biome atlases (grassland, canyon, snow, dirt), quartered
- cliff faces: Worley rock in strata
- props: riveted metal, planks, stone blocks, and hide for the orcs

Terrain uses world-space UVs and a textured vertex-lit shader that keeps per-cell light and dark.

**Orcs.** Each type is one procedurally built skinned mesh on a shared 20-bone skeleton (`Assets/Scripts/Actors/Orcs`), designed after Orc Horde's sprite briefs: build, palette, armour, weapon. One draw call per orc, skinned on the GPU. `OrcAnimator` drives the bones with no animation clips:
- a walk cycle paced by actual speed
- an overhead swing while attacking
- a crouch while winding up
- a flinch on hits
- a fall on death; corpses lie a few seconds, then sink

The head hitbox rides the head bone, and each orc gets a slight random tint. A full 110-orc horde holds 60 fps.

**Towers and rifle.** `TowerModels` builds each tower from the same solids, shaped by how it fights. Each turns and tilts to aim, and sweeps slowly when idle:
- **Machine Gun:** rotary barrels spin up; belt feed; casings fly out.
- **Cannon:** elevates to lob, kicks back, smokes.
- **Laser:** the crystal charges between shots and flares when the beam goes.
- **Arc Tower:** the toroid turns; bolts strike every orc in the pulse.
- **Flame Thrower:** the pilot light flickers.

Orcs show what is happening to them: a flash on a hit, an orange flicker and embers while burning, a blue shimmer while slowed. `GunModels` builds the rifle, which has recoil, flash, ejected brass, and a reload in which the magazine drops out and a fresh one seats.

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

**Armoury:** all five towers firing at immortal puppet orcs, then a close-up of each tower and the rifle idle, firing and reloading:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/armory" -smokearmory canyon_approach
```

**Stress:** 110 orcs of mixed kinds walking the map at normal speed, with frame times measured:

```bash
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/stress" -smokestress canyon_approach
```

Test runs keep their own save slot and never touch your campaign.
