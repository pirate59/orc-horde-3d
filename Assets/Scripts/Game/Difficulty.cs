using System;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The pressure each campaign round fields, from Resources/Difficulty.json.
    /// Tools/difficulty.py writes the table from a model of the player's
    /// firepower round by round, so each map opens at a fair fraction of what
    /// the player can stop and ends in a real test. Maps not in the table
    /// (Quarry Road, Canyon Approach and the test grounds) keep
    /// <see cref="GameManager.FieldedPressure"/>.
    /// </summary>
    public static class Difficulty
    {
        [Serializable]
        sealed class Table
        {
            public Entry[] maps = new Entry[0];
        }

        [Serializable]
        sealed class Entry
        {
            public string id;
            public float[] pressure;
        }

        static Table table;

        static Table Loaded
        {
            get
            {
                if (table != null) return table;
                var asset = Resources.Load<TextAsset>("Difficulty");
                return table = asset != null ? JsonUtility.FromJson<Table>(asset.text) : new Table();
            }
        }

        /// <summary>The pressure this round fields, before it is split into bodies and toughness.</summary>
        public static float Pressure(MapData map, int round)
        {
            var entry = Array.Find(Loaded.maps, e => e.id == map.id);
            if (entry != null && round < entry.pressure.Length) return entry.pressure[round];
            return GameManager.FieldedPressure(map.rounds[round].pressureBudget);
        }
    }
}
