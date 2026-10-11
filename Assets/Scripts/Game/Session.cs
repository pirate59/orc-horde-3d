using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrcHordeFps
{
    /// <summary>
    /// Names the saved keys. A smoke-test run keeps its own, so testing never
    /// touches a player's campaign.
    /// </summary>
    public static class SaveSlot
    {
        static readonly string suffix =
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoketest") >= 0 ? ".smoketest" : "";

        public static string Key(string name) => "orc-horde-fps." + name + suffix;
    }

    /// <summary>
    /// Which map is being played, and how far the campaign has got. Changing map
    /// reloads the scene: every map is built fresh by <see cref="Bootstrap"/>.
    /// </summary>
    public static class Session
    {
        static readonly string UnlockedKey = SaveSlot.Key("unlocked"), LastMapKey = SaveSlot.Key("last-map");

        static string mapId;

        public static MapCatalog Catalog => MapCatalog.Load();

        public static MapData Current => Catalog.Get(CurrentId);

        public static string CurrentId
        {
            get
            {
                if (mapId != null) return mapId;
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-map" && Catalog.Get(args[i + 1]) != null) return mapId = args[i + 1];
                var last = PlayerPrefs.GetString(LastMapKey, "");
                return mapId = IsAvailable(last) ? last : Catalog.campaign[0];
            }
        }

        /// <summary>How many campaign maps are open, counting from the first.</summary>
        public static int Unlocked
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, Catalog.campaign.Length);
            set => PlayerPrefs.SetInt(UnlockedKey, Mathf.Max(Unlocked, value));
        }

        /// <summary>Campaign maps open in order; maps outside the campaign (the test grounds) always are.</summary>
        public static bool IsAvailable(string id)
        {
            if (Catalog.Get(id) == null) return false;
            int index = Catalog.CampaignIndex(id);
            return index < 0 || index < Unlocked;
        }

        /// <summary>The campaign map after this one, or null at the end (or off the campaign).</summary>
        public static string NextMap(string id)
        {
            int index = Catalog.CampaignIndex(id);
            return index >= 0 && index + 1 < Catalog.campaign.Length ? Catalog.campaign[index + 1] : null;
        }

        public static void ResetUnlocks() => PlayerPrefs.SetInt(UnlockedKey, 1);

        public static void MarkHeld(string id)
        {
            int index = Catalog.CampaignIndex(id);
            if (index >= 0) Unlocked = index + 2;
        }

        public static void Play(string id)
        {
            mapId = id;
            PlayerPrefs.SetString(LastMapKey, id);
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
