# Orc Horde: FPS

A first-person tower defence game built on [Orc Horde](../orc-horde)'s designs. You build a line of towers and barricades between rounds, then fight in it with a rifle while the horde comes down the road.

Unity **6000.6.4f1**, built-in render pipeline, legacy Input Manager. It has no packages and no imported assets: the whole game is built at runtime from code.

## Play

Open the project in Unity Hub and open `Assets/Scenes/QuarryRoad.unity` (it opens on its own the first time). Then press Play. The game starts on the last map played; Esc → **Choose map** lists the campaign. Each map unlocks when you hold the one before it, and the two test grounds are always open.

| | |
| --- | --- |
| Move | WASD, Shift to sprint, Space to jump |
| Shoot | Left mouse, R to reload. Headshots do ×2 |
| Abilities | Hold Q (Mortar) or E (Bombing Run) to aim, release to fire |
| Build (between rounds) | 1–5 for towers, 6 for barricade, R to rotate, left click to place, X or right click to remove, 0 to put away |
| Start round / continue | G |
| Help / pause | H / Esc |

## What came from Orc Horde

Ported from `orc-horde/src/content`. The original numbers are kept in their original units and converted with `U.M()` (1 unit = 4 cm) and `U.Sec()`:

- **Towers:** Machine Gun, Cannon (lobs over barricades), Laser (pierces and ricochets), Arc Tower (slows), Flame Thrower (cone)
- **Enemies:** Fodder, Runner, Armoured, Siege Elite, Siege Breaker, Siege Boss, Sapper, Shaman, Splitter, with armour using the original quarter-damage floor
- **Barricade:** 8 per layout, carves the navmesh, restored on every attempt
- **Abilities:** Mortar and Bombing Run
- **Maps:** all 14 campaign maps and both test grounds, with every round, spawn gate, boss event, bridge and tunnel
- **Bosses:** Siege Boss, Ram Chief (charges), Shieldwall (frontal shield), Warlord (summons a war band), Blood Totem (heals and regenerates)

Deliberate deviations are marked `DEVIATION` in the code. The main ones: horde size is scaled by `GameManager.HordeScale` (0.12), the later towers get small starting limits because there's no skill tree yet, and units that normally avoid walls will chew through a fully sealed road.

## Maps

The maps aren't copied by hand. `Tools/export-maps.sh` bundles Orc Horde's own map code with its esbuild, runs the hex layout pass, and writes each map to `Assets/Resources/Maps.json` as a character grid (one character per hex), plus spawns, base, links and rounds. Re-run it whenever Orc Horde's maps change:

```bash
Tools/export-maps.sh ~/code/orc-horde/branches/claude-new-build
```

In 3D, path cells are open floor and tower-ground cells are a low step (a tower stands at the centre of one, as in Orc Horde). The remaining rock is cliff. Bridges carry orcs over a lane on a deck you can see, and tunnels hide them in the rock until they come out the other side.

## Layout

| Path | Purpose |
| --- | --- |
| `Assets/Scripts/Content` | Definitions, ported content, map data model |
| `Assets/Resources/Maps.json` | Exported Orc Horde maps (generated) |
| `Tools/` | Map exporter |
| `Assets/Scripts/World` | Hex grid, terrain meshes, navmesh, bridges and tunnels |
| `Assets/Scripts/Actors` | Enemies, towers, barricades, projectiles |
| `Assets/Scripts/Player` | Movement, rifle, health, abilities, build mode |
| `Assets/Scripts/Game` | Bootstrap, round loop, HUD, smoke test |
| `Assets/Editor/ProjectSetup.cs` | Writes the scene, layers and build settings |

## Smoke test

Builds a macOS player, then on each map places a layout, plays round 1 at 4× speed, and writes overhead and fight screenshots plus a summary line to `Logs/smoke/`:

```bash
Unity -batchmode -quit -projectPath . -executeMethod ProjectSetup.BuildMac
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/smoke" -smokemaps all
```
