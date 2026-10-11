using System;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Maps as exported from Orc Horde by Tools/export-maps.sh into
    /// Resources/Maps.json. Field names match the JSON; positions are in Orc
    /// Horde's units with y running south, and are converted on the way out.
    /// </summary>
    [Serializable]
    public sealed class MapCatalog
    {
        public string[] campaign;
        public MapData[] maps;

        static MapCatalog loaded;

        public static MapCatalog Load() =>
            loaded ??= JsonUtility.FromJson<MapCatalog>(Resources.Load<TextAsset>("Maps").text);

        public MapData Get(string id) => Array.Find(maps, m => m.id == id);

        /// <summary>Position in the campaign, or -1 for maps outside it (the test grounds).</summary>
        public int CampaignIndex(string id) => Array.IndexOf(campaign, id);
    }

    [Serializable]
    public sealed class MapPoint
    {
        public string id, name;
        public float x, y;
    }

    [Serializable]
    public sealed class MapLink
    {
        public string id, kind, direction;
        public float minX, minY, maxX, maxY;
        public bool IsTunnel => kind == "tunnel";
    }

    [Serializable]
    public sealed class EnemyRule
    {
        public string enemy;
        public float weight, fromMs, untilMs, maxFraction;
    }

    [Serializable]
    public sealed class SpawnRule
    {
        public string spawn;
        public float weight, fromMs, untilMs;
    }

    [Serializable]
    public sealed class GuaranteedSpawn
    {
        public string enemy, spawn;
        public int count;
        public float atMs, spacingMs;
    }

    [Serializable]
    public sealed class BossEvent
    {
        public string enemy, spawn;
        public float atMs, warningMs;
    }

    [Serializable]
    public sealed class RoundData
    {
        public string name;
        public int startingHealth;
        public float durationMs, pressureBudget;
        public EnemyRule[] enemyRules;
        public SpawnRule[] spawnRules;
        public GuaranteedSpawn[] guaranteed;
        public BossEvent[] bosses;

        public float Duration => durationMs / 1000f;
    }

    [Serializable]
    public sealed class MapData
    {
        public string id, name, description, biome;
        public float worldWidth, worldHeight, hexApothem;
        public int floorColor, backgroundColor, rockColor;
        public int columns, rows, firstRow;
        /// <summary>One character per hex cell, row-major from the north: # rock, T tower ground, . path, , barricade path.</summary>
        public string cells;
        public MapPoint[] spawns;
        public MapPoint endpoint;
        public MapLink[] links;
        public RoundData[] rounds;

        public float Width => U.M(worldWidth);
        public float Depth => U.M(worldHeight);

        /// <summary>Orc Horde's y runs south; here +Z is north.</summary>
        public Vector3 World(float x, float y) => new Vector3(U.M(x), 0, U.M(worldHeight - y));

        public Vector3 Base => World(endpoint.x, endpoint.y);


        public MapPoint Spawn(string spawnId) => Array.Find(spawns, s => s.id == spawnId) ?? spawns[0];

        public Vector3 SpawnPosition(string spawnId)
        {
            var s = Spawn(spawnId);
            return World(s.x, s.y);
        }

        /// <summary>Unit vector from a point on the rim into the map, along the nearest edge's normal.</summary>
        public Vector3 Inward(Vector3 p)
        {
            float west = p.x, east = Width - p.x, south = p.z, north = Depth - p.z;
            float min = Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));
            if (min == west) return Vector3.right;
            if (min == east) return Vector3.left;
            if (min == south) return Vector3.forward;
            return Vector3.back;
        }
    }
}
