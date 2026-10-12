using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace OrcHordeFps
{
    /// <summary>
    /// Keeps a copy of the campaign save (<see cref="SaveStore"/>) on the server that
    /// served the game: the Cloudflare Worker in web/worker, which stores it in D1.
    ///
    /// No accounts. Each device makes up an anonymous id and a secret the first time
    /// it runs; the server keeps the save under the id and only lets that secret
    /// change it. Together they make a save code: copied on one device and entered on
    /// another, the second device takes over the same campaign.
    ///
    /// When the game starts it fetches the cloud copy: if that's newer (a higher
    /// revision) it replaces the local one and the map reloads; if the local one is
    /// newer, it's sent up. While playing, each change is sent a couple of seconds
    /// later. If the server has meanwhile been sent a newer copy by another device
    /// still signed in with the same code, the device being played wins: it's the
    /// one with the player in front of it.
    ///
    /// Off where there's no server: a desktop build without -cloud, a page opened
    /// from a file, and the smoke tests.
    /// </summary>
    public sealed class CloudSave : MonoBehaviour
    {
        public enum State { Off, Connecting, Synced, Saving, Offline, Refused }

        public static CloudSave I { get; private set; }
        public static State Status { get; private set; } = State.Off;
        /// <summary>A line for the pause menu: what's happened with the cloud copy.</summary>
        public static string Note { get; private set; } = "";

        const string IdKey = "orc-horde-fps.cloud-id", SecretKey = "orc-horde-fps.cloud-secret";
        const float PushDelay = 2f, RetryDelay = 20f;

        string server, id, secret;
        float pushAt = -1f;
        bool pushing;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void BrowserCopy(string text);
        [DllImport("__Internal")] static extern string BrowserPrompt(string message);
        [DllImport("__Internal")] static extern string BrowserOrigin();
#endif

        /// <summary>Starts the cloud copy, once, at the first scene. Does nothing where there's no server.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (I != null) return;
            var server = Server();
            if (string.IsNullOrEmpty(server)) return;
            var go = new GameObject("Cloud save");
            DontDestroyOnLoad(go);
            I = go.AddComponent<CloudSave>();
            I.server = server.TrimEnd('/');
        }

        static string Server()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-smoketest") >= 0) return null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-cloud") return args[i + 1];
#if UNITY_WEBGL && !UNITY_EDITOR
            var origin = BrowserOrigin();
            return origin.StartsWith("http") ? origin : null;
#else
            return null;
#endif
        }

        public static bool Available => I != null;

        /// <summary>This device's save code: the id and secret together.</summary>
        public static string Code => I == null ? "" : $"{I.id}.{I.secret}";

        IEnumerator Start()
        {
            id = PlayerPrefs.GetString(IdKey, "");
            secret = PlayerPrefs.GetString(SecretKey, "");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
            {
                id = NewToken(16);
                secret = NewToken(24);
                PlayerPrefs.SetString(IdKey, id);
                PlayerPrefs.SetString(SecretKey, secret);
                PlayerPrefs.Save();
            }
            SaveStore.Committed += () => pushAt = Time.unscaledTime + PushDelay;
            yield return Pull(adoptIfNewer: true);
        }

        void Update()
        {
            if (pushAt < 0 || pushing || Time.unscaledTime < pushAt) return;
            pushAt = -1f;
            StartCoroutine(Push());
        }

        static string NewToken(int bytes)
        {
            var raw = new byte[bytes];
            var a = Guid.NewGuid().ToByteArray();
            var b = Guid.NewGuid().ToByteArray();
            for (int i = 0; i < bytes; i++) raw[i] = i < 16 ? a[i] : b[i - 16];
            return Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        [Serializable]
        sealed class Envelope
        {
            public int revision;
            public long updatedAt;
            public string data;
        }

        UnityWebRequest Request(string method, string body = null)
        {
            var req = new UnityWebRequest($"{server}/api/save/{id}", method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("Authorization", "Bearer " + secret);
            return req;
        }

        /// <summary>Fetches the cloud copy; takes it if it's newer, or sends ours up if ours is.</summary>
        IEnumerator Pull(bool adoptIfNewer)
        {
            Status = State.Connecting;
            using var req = Request("GET");
            yield return req.SendWebRequest();
            if (req.responseCode == 404)
            {
                // Nothing there yet: this device starts the cloud copy.
                yield return Push();
                yield break;
            }
            if (req.responseCode == 403) { Refuse("That save code doesn't match."); yield break; }
            if (req.result != UnityWebRequest.Result.Success) { GoOffline(); yield break; }

            var cloud = JsonUtility.FromJson<Envelope>(req.downloadHandler.text);
            if (adoptIfNewer && cloud.revision > SaveStore.Revision)
            {
                Adopt(cloud, "Campaign restored from the cloud save.");
                yield break;
            }
            if (cloud.revision < SaveStore.Revision) { yield return Push(); yield break; }
            Synced();
        }

        /// <summary>Sends our copy up. On a clash with a newer copy from elsewhere, ours wins.</summary>
        IEnumerator Push()
        {
            pushing = true;
            Status = State.Saving;
            var body = JsonUtility.ToJson(new Envelope { revision = SaveStore.Revision, updatedAt = SaveStore.UpdatedAt, data = SaveStore.Export() });
            using (var req = Request("PUT", body))
            {
                yield return req.SendWebRequest();
                if (req.responseCode == 409)
                {
                    // The server has a newer copy: put ours past it and send again.
                    var cloud = JsonUtility.FromJson<Envelope>(req.downloadHandler.text);
                    SaveStore.Outrank(cloud.revision);
                    pushing = false;
                    pushAt = Time.unscaledTime;
                    yield break;
                }
                pushing = false;
                if (req.responseCode == 403) { Refuse("This device's save code was refused."); yield break; }
                if (req.result != UnityWebRequest.Result.Success) { GoOffline(); pushAt = Time.unscaledTime + RetryDelay; yield break; }
            }
            Synced();
        }

        void Adopt(Envelope cloud, string message)
        {
            if (!SaveStore.Import(cloud.data, cloud.revision)) { Refuse("The cloud save couldn't be read."); return; }
            Progression.Reload();
            Session.Forget();
            Synced();
            Note = message;
            // Rebuild the scene from the restored campaign: its map, its layout.
            Session.Play(Session.CurrentId);
        }

        void Synced()
        {
            Status = State.Synced;
            Note = $"Saved to the cloud · revision {SaveStore.Revision}";
        }

        void GoOffline()
        {
            Status = State.Offline;
            Note = "Offline: saved on this device, will sync when back online";
        }

        void Refuse(string why)
        {
            Status = State.Refused;
            Note = why;
        }

        // --- Save codes ------------------------------------------------------------------------

        /// <summary>Copies this device's save code, for entering on another device.</summary>
        public static void CopyCode()
        {
            if (I == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            BrowserCopy(Code);
#else
            GUIUtility.systemCopyBuffer = Code;
#endif
            Note = "Save code copied. Enter it on another device to carry on there.";
        }

        /// <summary>Asks for a save code from another device, and takes over that campaign.</summary>
        public static void EnterCode()
        {
            if (I == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            var code = BrowserPrompt("Enter a save code from another device. This replaces the campaign on this one.");
#else
            var code = GUIUtility.systemCopyBuffer;
#endif
            I.StartCoroutine(I.UseCode(code?.Trim() ?? ""));
        }

        IEnumerator UseCode(string code)
        {
            int dot = code.IndexOf('.');
            if (dot <= 0 || dot == code.Length - 1) { Note = "That isn't a save code."; yield break; }
            string oldId = id, oldSecret = secret;
            id = code.Substring(0, dot);
            secret = code.Substring(dot + 1);
            using var req = Request("GET");
            yield return req.SendWebRequest();
            if (req.responseCode != 200)
            {
                id = oldId;
                secret = oldSecret;
                Note = req.responseCode == 404 ? "No campaign is saved under that code."
                    : req.responseCode == 403 ? "That save code doesn't match." : "Couldn't reach the cloud save.";
                yield break;
            }
            PlayerPrefs.SetString(IdKey, id);
            PlayerPrefs.SetString(SecretKey, secret);
            PlayerPrefs.Save();
            Adopt(JsonUtility.FromJson<Envelope>(req.downloadHandler.text), "Campaign loaded from the save code.");
        }
    }
}
