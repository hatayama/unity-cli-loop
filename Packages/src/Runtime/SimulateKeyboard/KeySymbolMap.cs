#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public static class KeySymbolMap
    {
        private static readonly Dictionary<string, string> Symbols = new()
        {
            { "Space", "\u2423" },           // ␣
            { "Enter", "\u23CE" },           // ⏎
            { "UpArrow", "\u2191" },         // ↑
            { "DownArrow", "\u2193" },       // ↓
            { "LeftArrow", "\u2190" },       // ←
            { "RightArrow", "\u2192" },      // →
            { "Tab", "\u21E5" },             // ⇥
            { "Escape", "Esc" },
            { "ContextMenu", "Menu" },
            { "PrintScreen", "PrtSc" },
            { "ScrollLock", "ScrLk" },
        };

        // Meta key maps to ⌘ on macOS, ⊞ on Windows/Linux
        private static bool IsMac =>
            Application.platform == RuntimePlatform.OSXEditor ||
            Application.platform == RuntimePlatform.OSXPlayer;

        public static string GetSymbol(string keyName)
        {
            // Key.LeftMeta.ToString() returns "LeftWindows" in Input System
            if (keyName == "LeftMeta" || keyName == "RightMeta" ||
                keyName == "LeftWindows" || keyName == "RightWindows")
            {
                return IsMac ? "\u2318" : "\u229E"; // ⌘ or ⊞
            }

            if (keyName == "LeftCtrl" || keyName == "RightCtrl")
            {
                return IsMac ? "\u2303" : "Ctrl"; // ⌃ or Ctrl
            }

            if (keyName == "LeftAlt" || keyName == "RightAlt")
            {
                return IsMac ? "\u2325" : "Alt"; // ⌥ or Alt
            }

            if (keyName == "LeftShift" || keyName == "RightShift")
            {
                return IsMac ? "\u21E7" : "Shift"; // ⇧ or Shift
            }

            if (keyName == "Backspace")
            {
                return IsMac ? "\u232B" : "BS"; // ⌫ or BS
            }

            if (keyName == "Delete")
            {
                return IsMac ? "\u2326" : "Del"; // ⌦ or Del
            }

            if (Symbols.TryGetValue(keyName, out string symbol))
            {
                return symbol;
            }

            return keyName;
        }

        // Single non-ASCII characters render smaller than text in most fonts
        public static bool IsGlyphSymbol(string symbol)
        {
            return symbol.Length == 1 && symbol[0] > 127;
        }
    }
}
