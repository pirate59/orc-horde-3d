#!/usr/bin/env python3
"""Sets how hard each campaign round is: writes Assets/Resources/Difficulty.json.

Orc Horde's budgets grow linearly to the end of the campaign, but in this game
the player's firepower stops growing that fast. Towers are capped at 38, and by
Twin Gullies every one is on the field. After that the only growth is upgrades,
and their prices rise steeply. Fielding Orc Horde's budgets as they are left
the middle maps easy and turned Vale Common's finale into a wall.

So this script models the player's firepower round by round instead:
  - Currency comes in at Orc Horde's rate. The game's RewardScale keeps
    payouts there, whatever is fielded.
  - It is spent as the smoke-test bot spends it: on the cheapest node first,
    with more towers favoured over better ones.
  - Firepower is then the sum, over every tower the player may field, of
    base damage per second times the upgrade multipliers, times a rough count
    of orcs each shot reaches.
  - Each round's mix of orcs sets how much of that firepower its pressure
    soaks up: health per unit of pressure, after armour has taken its cut of
    every hit. Armoured orcs and Siege Breakers shrug off light guns.

Each round then fields a set fraction of that firepower (PRESSURE). Within a
map, the fraction climbs from OPENER to FINALE. Both rise gently over the
campaign, and FEEL nudges single maps whose layout makes them easier or harder
than the model knows.

Quarry Road and Canyon Approach keep the original curve. On them the rifle
carries much of the fight, and they were tuned by hand.

    Tools/difficulty.py          # write the table and print the curve
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, "..", "Assets", "Resources")

# The fraction of modelled firepower a round fields, at the campaign's first
# hand-off map and its last. In between it is interpolated by map.
OPENER = (0.40, 0.50)
FINALE = (0.74, 0.82)
# Maps whose layout the model misjudges: a multiplier on their pressure.
# Vale Common's base stands in open ground right where its two roads meet,
# so heavy orcs that get through the jaws are on it at once.
# Horseshoe Basin's armoured mix looks worse on paper than it plays: its
# cannons arrive just in time for it.
FEEL = {"horseshoe_basin": 1.2, "vale_common": 0.9}
# Maps kept on the original curve (GameManager.FieldedPressure).
HAND_TUNED = {"quarry_road", "canyon_approach"}

# Base damage, seconds per shot and orcs reached per shot, from Content.cs.
# The reach is a judgement call: splash, chaining and the flame's cone.
TOWERS = {
    "machine_gun": (6, 0.12, 1, 3),
    "cannon": (10, 0.9, 3, 0),
    "laser": (2.5, 0.22, 1, 0),
    "arc_tower": (1, 0.5, 4, 0),
    "flame_thrower": (3.5, 0.14, 3, 0),
}

# Health, armour and pressure of each budget-drawn orc, from Content.cs.
ENEMIES = {
    "fodder": (35, 0, 1 / 3), "runner": (25, 0, 2 / 3), "armoured": (130, 6, 2),
    "siege_elite": (500, 0, 20 / 3), "mini_boss": (1200, 10, 15),
    "sapper": (60, 0, 1), "shaman": (90, 0, 1), "splitter": (180, 0, 2),
}

# Currency per unit of Orc Horde budget on a first clear, before the
# completion multiplier. Measured from the campaign harness: the first two
# maps pay more because the rifle adds kills on top of the towers.
def earn_factor(map_index):
    return (3.0, 2.2)[map_index] if map_index < 2 else 1.6

# Skill effects this game honours (Progression.Honoured).
SUPPORTED = {"unlock_tower", "tower_capacity_delta", "tower_stat_multiplier_delta",
             "wall_capacity_delta", "wall_health_multiplier_delta", "base_health_delta",
             "ability_stat_multiplier_delta"}
TOWER_STATS = {"damage", "fireRate", "range", "splashRadius", "slowDuration", "coneWidth"}


def honoured(e):
    if e["kind"] not in SUPPORTED:
        return False
    if e["kind"] == "tower_stat_multiplier_delta" and e.get("stat") not in TOWER_STATS:
        return False
    if e["kind"] == "ability_stat_multiplier_delta" and (
            e.get("target") not in ("mortar", "bombing_run") or e.get("stat") not in ("cooldown", "damage", "radius")):
        return False
    return True


def offered(nodes):
    ok = {n["id"] for n in nodes if n["currency"] == "currency"
          and all(honoured(e) for l in n["levels"] for e in l["effects"])}
    changed = True
    while changed:
        changed = False
        for n in nodes:
            if n["id"] not in ok:
                continue
            main = all(p["nodeId"] in ok for p in n["prerequisites"])
            alt = n["alternatives"] and all(p["nodeId"] in ok for p in n["alternatives"])
            if not main and not alt:
                ok.discard(n["id"])
                changed = True
    return [n for n in nodes if n["id"] in ok]


def bought_effects(nodes, levels):
    for n in nodes:
        for l in n["levels"][:levels.get(n["id"], 0)]:
            yield from l["effects"]


def towers(nodes, levels):
    """Every tower the player may field: (count, damage per hit, hits per second)."""
    effects = list(bought_effects(nodes, levels))

    def total(kind, target, stat=None):
        return sum(e["amount"] for e in effects
                   if e["kind"] == kind and e["target"] == target and (stat is None or e.get("stat") == stat))

    out = []
    for tower, (damage, interval, reach, limit) in TOWERS.items():
        if limit == 0 and not any(e["kind"] == "unlock_tower" and e["target"] == tower for e in effects):
            continue
        count = limit + round(total("tower_capacity_delta", tower))
        dmg = 1 + total("tower_stat_multiplier_delta", tower, "damage")
        rate = 1 + total("tower_stat_multiplier_delta", tower, "fireRate")
        out.append((count, damage * dmg, reach * rate / interval))
    return out


def firepower(fielded):
    return sum(count * hit * rate for count, hit, rate in fielded)


def after_armour(hit, armour):
    """Enemy.TakeDamage: armour comes off every hit, but a quarter always lands."""
    return max(hit - armour, hit * 0.25)


def health_per_firepower(fielded, rnd):
    """
    How long a unit of this round's pressure takes to kill, against a unit of
    firepower. Health per pressure varies by type, and armour takes a cut of
    every hit. A round of Siege Breakers soaks far more than its pressure says.
    """
    raw = firepower(fielded)
    weights = [(ENEMIES[e["enemy"]], e["weight"]) for e in rnd["enemyRules"] if e["enemy"] in ENEMIES]
    pressure = sum(w * p for (hp, armour, p), w in weights)
    seconds = 0.0
    for (hp, armour, p), w in weights:
        dps = sum(count * after_armour(hit, armour) * rate for count, hit, rate in fielded)
        seconds += w * hp / (dps / raw)
    return seconds / pressure


def spend(nodes, levels, bank):
    def met(n):
        ok = lambda s: all(levels.get(p["nodeId"], 0) >= p["minimumLevel"] for p in s)
        return ok(n["prerequisites"]) or (n["alternatives"] and ok(n["alternatives"]))

    while True:
        best, best_score = None, float("inf")
        for n in nodes:
            level = levels.get(n["id"], 0)
            if level >= len(n["levels"]) or not met(n):
                continue
            cost = n["levels"][level]["cost"]
            if cost > bank:
                continue
            more = any(e["kind"] in ("unlock_tower", "tower_capacity_delta") for e in n["levels"][level]["effects"])
            score = cost * (0.5 if more else 1)
            if score < best_score:
                best, best_score = n, score
        if best is None:
            return bank
        bank -= best["levels"][levels.get(best["id"], 0)]["cost"]
        levels[best["id"]] = levels.get(best["id"], 0) + 1


def main():
    nodes = offered(json.load(open(os.path.join(RES, "Skills.json")))["nodes"])
    data = json.load(open(os.path.join(RES, "Maps.json")))
    maps = {m["id"]: m for m in data["maps"]}
    campaign = data["campaign"]
    tuned = [m for m in campaign if m not in HAND_TUNED]

    # First the player's arsenal at every round, as the campaign pays for it.
    levels, bank, k = {}, 0, 0
    plan = []
    for mi, map_id in enumerate(campaign):
        for r, rnd in enumerate(maps[map_id]["rounds"]):
            fielded = towers(nodes, levels)
            plan.append((map_id, r, rnd, fielded, health_per_firepower(fielded, rnd)))
            bank += int(rnd["pressureBudget"] * earn_factor(mi) * (1 + 0.05 * k) * 2)
            bank = spend(nodes, levels, bank)
            k += 1

    # Then each round's pressure: its share of firepower, made lighter where
    # its orcs soak more than the average tuned round's.
    soak = [h for map_id, _, _, _, h in plan if map_id in tuned]
    mean_soak = sum(soak) / len(soak)
    out = {}
    for k, (map_id, r, rnd, fielded, soak) in enumerate(plan):
        rounds = len(maps[map_id]["rounds"])
        t = tuned.index(map_id) / max(1, len(tuned) - 1) if map_id in tuned else 0
        opener = OPENER[0] + (OPENER[1] - OPENER[0]) * t
        finale = FINALE[0] + (FINALE[1] - FINALE[0]) * t
        share = opener + (finale - opener) * r / max(1, rounds - 1)
        power = firepower(fielded)
        pressure = share * power * mean_soak / soak * FEEL.get(map_id, 1.0)
        print(f"{k + 1:2} {map_id:18} r{r + 1} firepower={power:6.0f} soak={soak / mean_soak:4.2f} share={share:.2f} "
              f"pressure={pressure:6.0f} toughness=x{max(1, pressure / 800):.1f}"
              + ("  (hand-tuned curve)" if map_id in HAND_TUNED else ""))
        if map_id not in HAND_TUNED:
            out.setdefault(map_id, []).append(round(pressure))
    out = [{"id": m, "pressure": p} for m, p in out.items()]

    path = os.path.join(RES, "Difficulty.json")
    with open(path, "w") as f:
        json.dump({"maps": out}, f, indent=2)
        f.write("\n")
    print(f"wrote {os.path.relpath(path)}")


if __name__ == "__main__":
    main()
