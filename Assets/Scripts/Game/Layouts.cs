using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The line a player built on each map, kept between attempts, rounds and
    /// sessions as Orc Horde keeps it. Saved whenever it changes; restored when
    /// the map is built, through the same checks as a hand placement, so a
    /// layout from before a reset or a respec only brings back what still fits.
    /// </summary>
    public static class Layouts
    {
        [Serializable]
        sealed class Saved
        {
            public List<SavedTower> towers = new List<SavedTower>();
            public List<SavedWall> walls = new List<SavedWall>();
        }

        [Serializable]
        sealed class SavedTower
        {
            public string id;
            public int column, row;
        }

        [Serializable]
        sealed class SavedWall
        {
            public int column, row, axis;
        }

        static string mapId;
        static bool restoring;

        static string Key(string id) => SaveSlot.Key("layout." + id);

        /// <summary>Starts saving changes for this map, and puts back what was saved for it.</summary>
        public static int Restore(string id)
        {
            mapId = id;
            BuildController.LayoutChanged -= Save;
            BuildController.LayoutChanged += Save;

            var json = PlayerPrefs.GetString(Key(id), "");
            if (string.IsNullOrEmpty(json)) return 0;
            var saved = JsonUtility.FromJson<Saved>(json);
            int placed = 0;
            restoring = true;
            try
            {
                foreach (var t in saved.towers)
                {
                    var def = Array.Find(Towers.All, d => d.Id == t.id);
                    if (def != null && BuildController.PlaceTower(def, new Vector2Int(t.column, t.row)) != null) placed++;
                }
                foreach (var w in saved.walls)
                    if (BuildController.PlaceBarricade(new Vector2Int(w.column, w.row), w.axis, ignorePlayer: true) != null) placed++;
            }
            finally { restoring = false; }
            return placed;
        }

        static void Save()
        {
            if (restoring || mapId == null) return;
            var saved = new Saved();
            foreach (var t in Tower.All) saved.towers.Add(new SavedTower { id = t.Def.Id, column = t.Cell.x, row = t.Cell.y });
            foreach (var b in Barricade.All)
                saved.walls.Add(new SavedWall { column = b.Anchor.x, row = b.Anchor.y, axis = b.Axis });
            PlayerPrefs.SetString(Key(mapId), JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
        }
    }
}
