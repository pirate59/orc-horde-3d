// Bundled by export-maps.sh against an Orc Horde checkout ("orc-horde" is an esbuild alias for its src/).
//
// Every map is already laid out in hex cells by Orc Horde itself; this reads
// each cell back as one character, so Unity can raise the ground without
// knowing anything about polygons:
//   #  rock          T  rock a tower can stand on
//   .  path          ,  path a barricade can stand on
import { writeFileSync } from "node:fs";
import { ALL_MAPS } from "orc-horde/content/maps/allMaps";
import { CAMPAIGN_DEFINITION } from "orc-horde/content/campaign";
import { HEX_APOTHEM, hexCenter, isWallGroundCell } from "orc-horde/content/hexCells";
import { isPointInsidePolygon } from "orc-horde/content/mapBuilder";
import { SKILL_TREE_DEFINITION } from "orc-horde/content/skills";

const HEX_RADIUS = (HEX_APOTHEM * 2) / Math.sqrt(3);
const COLUMN_STEP = HEX_RADIUS * 1.5;
const ROW_STEP = HEX_APOTHEM * 2;
const FIRST_ROW = -1;

function exportMap(map) {
  const columns = Math.ceil(map.worldWidth / COLUMN_STEP) + 1;
  const rows = Math.ceil(map.worldHeight / ROW_STEP) - FIRST_ROW + 1;
  let cells = "";
  // Row-major, north row first, as Orc Horde's y runs.
  for (let r = 0; r < rows; r += 1) {
    for (let column = 0; column < columns; column += 1) {
      const cell = { column, row: r + FIRST_ROW };
      const c = hexCenter(cell);
      const inside = c.x >= 0 && c.y >= 0 && c.x <= map.worldWidth && c.y <= map.worldHeight;
      const floor = inside && map.enemyFloorRegions.some((region) => isPointInsidePolygon(c, region));
      if (floor) cells += isWallGroundCell(map, cell) ? "," : ".";
      else if (inside && map.towerBuildRegions.some((region) => isPointInsidePolygon(c, region.polygon))) cells += "T";
      else cells += "#";
    }
  }

  const point = (p) => ({ id: p.id, name: p.name, x: p.position.x, y: p.position.y });
  return {
    id: map.id,
    name: map.name,
    description: map.description,
    biome: map.biome ?? "dirt",
    worldWidth: map.worldWidth,
    worldHeight: map.worldHeight,
    floorColor: map.floorColor,
    backgroundColor: map.backgroundColor,
    rockColor: map.staticObstacles[0]?.renderColor ?? 0x4f4a44,
    hexApothem: HEX_APOTHEM,
    columns,
    rows,
    firstRow: FIRST_ROW,
    cells,
    spawns: map.spawnPoints.map(point),
    endpoint: point(map.endpoint),
    links: (map.links ?? []).map((l) => ({ id: l.id, kind: l.kind, direction: l.direction, ...l.span })),
    rounds: map.rounds.map((r) => ({
      name: r.name,
      startingHealth: r.startingHealth,
      durationMs: r.durationMs,
      pressureBudget: r.pressureBudget,
      enemyRules: r.enemyRules.map((e) => ({
        enemy: e.enemyTypeId, weight: e.weight, fromMs: e.activeFromMs, untilMs: e.activeUntilMs,
        maxFraction: e.maximumPressureFraction,
      })),
      spawnRules: r.spawnRules.map((s) => ({
        spawn: s.spawnPointId, weight: s.weight, fromMs: s.activeFromMs, untilMs: s.activeUntilMs,
      })),
      guaranteed: r.guaranteedSpawns.map((g) => ({
        enemy: g.enemyTypeId, count: g.count, atMs: g.atMs, spacingMs: g.spacingMs, spawn: g.spawnPointId ?? "",
      })),
      bosses: (r.bossEvents ?? []).map((b) => ({
        enemy: b.enemyTypeId, atMs: b.atMs, spawn: b.spawnPointId, warningMs: b.warningMs,
      })),
    })),
  };
}

// The skill tree, as plain data. Unity decides which effects it can honour.
function exportSkills() {
  const prereq = (p) => ({ nodeId: p.nodeId, minimumLevel: p.minimumLevel });
  return SKILL_TREE_DEFINITION.nodes.map((n) => ({
    id: n.id,
    name: n.name,
    description: n.description,
    currency: n.currency ?? "currency",
    mastery: n.mastery ?? false,
    x: n.uiPosition.x,
    y: n.uiPosition.y,
    prerequisites: n.prerequisites.map(prereq),
    alternatives: (n.alternativePrerequisites ?? []).map(prereq),
    levels: n.levels.map((l) => ({
      cost: l.cost,
      label: l.label,
      description: l.description,
      effects: l.effects.map((e) => ({
        kind: e.kind,
        target: e.towerTypeId ?? e.abilityId ?? "",
        stat: e.stat ?? "",
        amount: e.amount ?? 0,
      })),
    })),
  }));
}

const outDir = process.argv[2];
const maps = ALL_MAPS.map(exportMap);
writeFileSync(`${outDir}/Maps.json`, JSON.stringify({ campaign: CAMPAIGN_DEFINITION.mapOrder, maps }));
const skills = exportSkills();
writeFileSync(`${outDir}/Skills.json`, JSON.stringify({ nodes: skills }));
console.log(`wrote ${maps.length} maps and ${skills.length} skill nodes to ${outDir}`);
