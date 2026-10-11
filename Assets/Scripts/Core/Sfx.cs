using System.Collections.Generic;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// Plays the baked sounds in Resources/Audio. Every call picks one of a
    /// sound's variants at random and nudges its pitch, so repeats never sound
    /// identical. A horde can ask for a hundred sounds a second, so each sound
    /// has limits: how many may play at once and how soon it may start again.
    /// Over a limit, the quieter request is simply dropped.
    /// </summary>
    public static class Sfx
    {
        /// <summary>How each sound behaves: loudness, voice limit, minimum gap, audible range, pitch spread.</summary>
        struct Rule
        {
            public float Volume, Gap, Range, Pitch;
            public int Voices;
        }

        static readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>
        {
            ["rifle_shot"] = R(0.55f, 8, 0f, 0, 0.05f),
            ["mg_shot"] = R(0.45f, 6, 0.03f, 70, 0.07f),
            ["cannon_fire"] = R(0.85f, 3, 0.05f, 110, 0.06f),
            ["explosion"] = R(0.8f, 4, 0.06f, 130, 0.08f),
            ["laser_zap"] = R(0.45f, 3, 0.05f, 70, 0.06f),
            ["arc_zap"] = R(0.4f, 3, 0.08f, 60, 0.1f),
            ["flame_burst"] = R(0.4f, 3, 0.12f, 50, 0.08f),
            ["orc_grunt"] = R(0.5f, 4, 0.12f, 40, 0.12f),
            ["orc_death"] = R(0.55f, 4, 0.08f, 50, 0.1f),
            ["orc_swing"] = R(0.35f, 3, 0.1f, 30, 0.1f),
            ["boss_roar"] = R(1f, 2, 0.5f, 160, 0.05f),
            ["wall_hit"] = R(0.5f, 4, 0.1f, 60, 0.12f),
            ["wall_break"] = R(0.85f, 2, 0.2f, 90, 0.05f),
            ["heal"] = R(0.3f, 2, 0.3f, 40, 0.05f),
            ["mortar_whistle"] = R(0.6f, 2, 0f, 120, 0.03f),
            ["step_gravel"] = R(0.28f, 2, 0f, 0, 0.1f),
            ["step_snow"] = R(0.3f, 2, 0f, 0, 0.1f),
            ["step_grass"] = R(0.25f, 2, 0f, 0, 0.1f),
            ["land"] = R(0.4f, 1, 0.1f, 0, 0.05f),
            ["hit"] = R(0.45f, 2, 0.03f, 0, 0.04f),
            ["headshot"] = R(0.5f, 2, 0.05f, 0, 0.03f),
        };

        static Rule R(float volume, int voices, float gap, float range, float pitch) =>
            new Rule { Volume = volume, Voices = voices, Gap = gap, Range = range, Pitch = pitch };

        static readonly Rule Default = R(0.6f, 3, 0.05f, 80, 0.04f);
        const int PoolSize = 40;
        const string VolumeKey = "orc-horde-fps.volume";

        static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        static AudioSource[] pool;
        static string[] playing;
        static Transform root;

        /// <summary>Sounds asked for and sounds actually played, by name: for the smoke test.</summary>
        public static readonly Dictionary<string, int> Requested = new Dictionary<string, int>(), Played = new Dictionary<string, int>();

        /// <summary>Master volume, 0 to 1, remembered between sessions.</summary>
        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        static AudioClip[] Clips(string name)
        {
            if (clips.TryGetValue(name, out var found)) return found;
            var list = new List<AudioClip>();
            var first = Resources.Load<AudioClip>("Audio/" + name);
            if (first != null) list.Add(first);
            for (int i = 1; ; i++)
            {
                var more = Resources.Load<AudioClip>($"Audio/{name}_{i}");
                if (more == null) break;
                list.Add(more);
            }
            if (list.Count == 0) Debug.LogWarning($"[Sfx] No sound called {name}");
            return clips[name] = list.ToArray();
        }

        /// <summary>The pool lives with the scene, so a map change rebuilds it.</summary>
        static void EnsurePool()
        {
            if (root != null) return;
            root = new GameObject("Audio").transform;
            pool = new AudioSource[PoolSize];
            playing = new string[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var src = new GameObject("Voice").AddComponent<AudioSource>();
                src.transform.SetParent(root, false);
                src.playOnAwake = false;
                src.dopplerLevel = 0;
                src.rolloffMode = AudioRolloffMode.Linear;
                pool[i] = src;
            }
            AudioListener.volume = Volume;
        }

        /// <summary>A sound in the world, quieter with distance.</summary>
        public static void At(string name, Vector3 position, float volume = 1f, float pitch = 1f) => Play(name, position, volume, true, pitch);

        /// <summary>A sound in the player's ears: their own gun, the HUD, the horns.</summary>
        public static void UI(string name, float volume = 1f) => Play(name, Vector3.zero, volume, false, 1f);

        static void Play(string name, Vector3 position, float volume, bool spatial, float pitch)
        {
            Requested[name] = Requested.TryGetValue(name, out var r) ? r + 1 : 1;
            var set = Clips(name);
            if (set.Length == 0) return;
            EnsurePool();
            var rule = rules.TryGetValue(name, out var found) ? found : Default;

            // Too soon after the last one, too many already sounding, or out of earshot: drop it.
            if (lastPlayed.TryGetValue(name, out var last) && Time.unscaledTime - last < rule.Gap) return;
            int voices = 0, free = -1;
            for (int i = 0; i < PoolSize; i++)
            {
                if (pool[i].isPlaying) { if (playing[i] == name) voices++; }
                else if (free < 0) free = i;
            }
            if (voices >= rule.Voices || free < 0) return;
            if (spatial && rule.Range > 0 && Camera.main != null &&
                (Camera.main.transform.position - position).sqrMagnitude > rule.Range * rule.Range) return;

            var src = pool[free];
            playing[free] = name;
            lastPlayed[name] = Time.unscaledTime;
            src.clip = set[Random.Range(0, set.Length)];
            src.volume = rule.Volume * volume;
            src.pitch = pitch * (1f + Random.Range(-rule.Pitch, rule.Pitch));
            src.spatialBlend = spatial ? 1f : 0f;
            src.minDistance = 3f;
            src.maxDistance = rule.Range > 0 ? rule.Range : 60f;
            src.transform.position = position;
            src.Play();
            Played[name] = Played.TryGetValue(name, out var p) ? p + 1 : 1;
        }

        /// <summary>Plays a looping sound in the player's ears until the scene goes: the wind.</summary>
        public static AudioSource Loop(string name, float volume)
        {
            var set = Clips(name);
            if (set.Length == 0) return null;
            EnsurePool();
            var src = new GameObject("Loop " + name).AddComponent<AudioSource>();
            src.transform.SetParent(root, false);
            src.clip = set[0];
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = 0;
            src.Play();
            return src;
        }
    }
}
