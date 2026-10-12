// The few things the game needs from the page around it in a browser build.
mergeInto(LibraryManager.library, {
  // Copies text to the clipboard. Called from a click, so the browser allows it.
  BrowserCopy: function (text) {
    var s = UTF8ToString(text);
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(s).catch(function () { window.prompt("Copy your save code:", s); });
    } else {
      window.prompt("Copy your save code:", s);
    }
  },

  // Asks for a line of text; returns it, or an empty string if cancelled.
  BrowserPrompt: function (message) {
    var answer = window.prompt(UTF8ToString(message), "") || "";
    var size = lengthBytesUTF8(answer) + 1;
    var buffer = _malloc(size);
    stringToUTF8(answer, buffer, size);
    return buffer;
  },

  // Whether the browser really has the mouse captured. Unity's own Cursor.lockState
  // reports what was asked for, not what the browser granted.
  BrowserPointerLocked: function () {
    return document.pointerLockElement ? 1 : 0;
  },

  // Whether Esc was pressed since the last call. In a browser Esc belongs to the page:
  // it frees a captured mouse, and the game doesn't always see the key itself.
  BrowserTakeEscape: function () {
    if (!window.__orcEscape) {
      window.__orcEscape = { pressed: 0 };
      window.addEventListener("keydown", function (e) { if (e.key === "Escape") window.__orcEscape.pressed = 1; }, true);
      document.addEventListener("pointerlockchange", function () {
        // Losing the lock without a keydown (Esc swallowed by the browser) counts as Esc.
        if (!document.pointerLockElement) window.__orcEscape.lost = 1;
      });
    }
    // 1: the key itself; 2: the mouse freed without one.
    var hit = (window.__orcEscape.pressed ? 1 : 0) | (window.__orcEscape.lost ? 2 : 0);
    window.__orcEscape.pressed = 0;
    window.__orcEscape.lost = 0;
    return hit;
  },

  // The page's origin, for talking to the Worker that served it.
  BrowserOrigin: function () {
    var origin = window.location.origin || "";
    var size = lengthBytesUTF8(origin) + 1;
    var buffer = _malloc(size);
    stringToUTF8(origin, buffer, size);
    return buffer;
  },
});
