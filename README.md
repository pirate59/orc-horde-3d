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
| Skill tree (between rounds) | B or Tab |
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
- **Progression:** the skill tree's currency nodes (tower unlocks, capacity, damage, fire rate, range, splash, slow, cone, barricade count and health, base health, Mortar and Bombing Run upgrades) and the eight 40-level masteries, with Orc Horde's economy. Kills pay win or lose; a win adds kills × (1 + 5% per campaign round), doubled on a first clear. Nodes with effects the FPS doesn't have yet (shields, traits, the seven later abilities) are hidden. An FPS-only Rifle line keeps your own gun in step.

Deliberate deviations are marked `DEVIATION` in the code. The main ones:

- **Horde size.** Orc Horde's budgets grow nearly 600× over the campaign. Here a round fields 12% of the original at Quarry Road's opener, grows as budget^0.85 through the first two maps, then grows linearly in step with Orc Horde from Horseshoe Basin on (`GameManager.FieldedPressure`).
- **Toughness.** Past `GameManager.BodyBudget` (800 pressure), extra pressure becomes toughness: the same number of orcs drawn from the budget, each with ×T health (heals scale too). Bosses and other set pieces sit outside the budget, as in Orc Horde, and keep their own health. The HUD shows toughness during a round.
- **Rewards.** Each kill pays original budget ÷ spawned budget times its Orc Horde reward, so currency per round matches Orc Horde's.
- **Sealed roads.** Units that normally avoid walls will chew through a fully sealed road.

Currency, skills and unlocked maps are saved in PlayerPrefs. Esc → **Reset campaign** starts over.

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

Test runs keep their own save slot and never touch your campaign.
