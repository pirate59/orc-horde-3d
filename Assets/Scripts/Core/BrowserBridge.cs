using System.Runtime.InteropServices;
using UnityEngine;

namespace OrcHordeFps
{
    /// <summary>
    /// What the game needs to know from the page in a browser build (Plugins/WebGL/Browser.jslib),
    /// with plain answers everywhere else.
    /// </summary>
    public static class BrowserBridge
    {
        public static readonly bool InBrowser = Application.platform == RuntimePlatform.WebGLPlayer;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int BrowserPointerLocked();
        [DllImport("__Internal")] static extern int BrowserTakeEscape();

        /// <summary>Whether the browser really has the mouse captured.</summary>
        public static bool PointerLocked => BrowserPointerLocked() != 0;

        /// <summary>
        /// Esc pressed since last asked, or the mouse freed by the browser while the game still
        /// wanted it (the browser swallowing an Esc). The game freeing it itself doesn't count.
        /// </summary>
        public static bool TakeEscape(bool wantedLock)
        {
            int hit = BrowserTakeEscape();
            return (hit & 1) != 0 || ((hit & 2) != 0 && wantedLock);
        }
#else
        public static bool PointerLocked => Cursor.lockState == CursorLockMode.Locked;
        public static bool TakeEscape(bool wantedLock) => false;
#endif
    }
}
