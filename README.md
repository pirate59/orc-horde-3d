# Orc Horde: FPS

A first-person tower defence game built on [Orc Horde](../orc-horde)'s designs. You build a line of towers and barricades between rounds, then fight in it with a rifle while the horde comes down the road.

Unity **6000.6.4f1**, built-in render pipeline, legacy Input Manager. It has no packages and no imported assets: the whole game is built at runtime from code.

## Play

Open the project in Unity Hub and open `Assets/Scenes/QuarryRoad.unity` (it opens on its own the first time). Then press Play.

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
- **Map:** Quarry Road and its five rounds, including the round 5 Siege Boss set piece

Deliberate deviations are marked `DEVIATION` in the code. The main ones: horde size is scaled by `GameManager.HordeScale` (0.12), the later towers get small starting limits because there's no skill tree yet, and units that normally avoid walls will chew through a fully sealed road.

## Layout

| Path | Purpose |
| --- | --- |
| `Assets/Scripts/Content` | Definitions and ported content |
| `Assets/Scripts/World` | Raises the 2D map into 3D blocks and bakes the navmesh |
| `Assets/Scripts/Actors` | Enemies, towers, barricades, projectiles |
| `Assets/Scripts/Player` | Movement, rifle, health, abilities, build mode |
| `Assets/Scripts/Game` | Bootstrap, round loop, HUD, smoke test |
| `Assets/Editor/ProjectSetup.cs` | Writes the scene, layers and build settings |

## Smoke test

Builds a macOS player, places a layout, plays two rounds at 3× speed, and writes screenshots and a summary to `Logs/smoke/`:

```bash
Unity -batchmode -quit -projectPath . -executeMethod ProjectSetup.BuildMac
Builds/OrcHordeFPS.app/Contents/MacOS/* -smoketest "$PWD/Logs/smoke"
```
