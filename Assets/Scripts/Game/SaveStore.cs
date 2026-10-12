using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// The campaign's save: one JSON document holding everything a player would
    /// miss if it were lost (progression, unlocks, the last map, every map's
    /// layout), with a revision that goes up on every change. Device settings
    /// (volume, the sky override, the cloud identity) stay in PlayerPrefs on
    /// their own, since they belong to this machine, not the campaign.
    ///
    /// Kept in PlayerPrefs, which a browser build puts in IndexedDB, and copied
    /// to the cloud by <see cref="CloudSave"/> when there is a server to copy to.
    /// One document makes that copy all or nothing: a campaign never arrives
    /// half from one device and half from another.
    /// </summary>
    public static class SaveStore
    {
        [Serializable]
        public sealed class Document
        {
            public int version = 1;
            /// <summary>Goes up by one on every change; the higher of two copies is the newer.</summary>
            public int revision;
            /// <summary>When it last changed, in Unix milliseconds: for showing, not for deciding.</summary>
            public long updatedAt;
            public List<string> keys = new List<string>();
            public List<string> values = new List<string>();
        }

        static readonly string StoreKey = SaveSlot.Key("save");
        static Document doc;

        /// <summary>Raised after every change is written, for the cloud copy to follow.</summary>
        public static event Action Committed;
        /// <summary>Raised when the whole save is replaced, from the cloud or a save code: caches must be dropped.</summary>
        public static event Action Replaced;

        static Document Doc
        {
            get
            {
                if (doc != null) return doc;
                var json = PlayerPrefs.GetString(StoreKey, "");
                doc = string.IsNullOrEmpty(json) ? Migrate() : JsonUtility.FromJson<Document>(json);
                return doc;
            }
        }

        public static int Revision => Doc.revision;
        public static long UpdatedAt => Doc.updatedAt;

        public static string GetString(string key, string fallback = "")
        {
            int i = Doc.keys.IndexOf(key);
            return i < 0 ? fallback : Doc.values[i];
        }

        public static int GetInt(string key, int fallback) =>
            int.TryParse(GetString(key, ""), out var v) ? v : fallback;

        /// <summary>Changes a value in memory. <see cref="Commit"/> writes it out.</summary>
        public static void Set(string key, string value)
        {
            int i = Doc.keys.IndexOf(key);
            if (i >= 0)
            {
                if (Doc.values[i] == value) return;
                Doc.values[i] = value;
            }
            else
            {
                Doc.keys.Add(key);
                Doc.values.Add(value);
            }
            dirty = true;
        }

        public static void Set(string key, int value) => Set(key, value.ToString());

        static bool dirty;

        /// <summary>Writes the save out, one revision on, if anything changed.</summary>
        public static void Commit()
        {
            if (!dirty) return;
            dirty = false;
            Doc.revision++;
            Doc.updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Write();
            Committed?.Invoke();
        }

        static void Write()
        {
            PlayerPrefs.SetString(StoreKey, JsonUtility.ToJson(Doc));
            PlayerPrefs.Save();
        }

        /// <summary>The save as it would go to the cloud.</summary>
        public static string Export() => JsonUtility.ToJson(Doc);

        /// <summary>
        /// Replaces the save with another copy of it (from the cloud, or a save code from
        /// another device). Returns false if the copy can't be read.
        /// </summary>
        public static bool Import(string json, int atLeastRevision = 0)
        {
            Document incoming;
            try { incoming = JsonUtility.FromJson<Document>(json); }
            catch (Exception) { return false; }
            if (incoming == null || incoming.keys == null || incoming.values == null || incoming.keys.Count != incoming.values.Count) return false;
            incoming.revision = Mathf.Max(incoming.revision, atLeastRevision);
            doc = incoming;
            dirty = false;
            Write();
            Replaced?.Invoke();
            return true;
        }

        /// <summary>Puts this copy's revision past another's, so it is the newer of the two.</summary>
        public static void Outrank(int otherRevision)
        {
            if (Doc.revision > otherRevision) return;
            Doc.revision = otherRevision + 1;
            Write();
        }

        /// <summary>
        /// A first run on this version: gathers the keys earlier versions saved one by one
        /// into the document, so nobody loses a campaign on update.
        /// </summary>
        static Document Migrate()
        {
            var d = new Document();
            void Take(string name)
            {
                var old = SaveSlot.Key(name);
                if (!PlayerPrefs.HasKey(old)) return;
                // Unlocks were saved as an int; everything else as a string.
                string value = name == "unlocked" ? PlayerPrefs.GetInt(old, 1).ToString() : PlayerPrefs.GetString(old, "");
                d.keys.Add(name);
                d.values.Add(value);
            }
            Take("progression");
            Take("unlocked");
            Take("last-map");
            foreach (var m in MapCatalog.Load().maps) Take("layout." + m.id);
            if (d.keys.Count > 0) d.revision = 1;
            d.updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return d;
        }
    }
}
