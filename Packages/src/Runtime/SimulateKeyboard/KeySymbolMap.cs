#if UNITY_EDITOR
#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides Key Symbol Map behavior for Unity CLI Loop.
    /// </summary>
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

        private static readonly Dictionary<string, (string macSymbol, string otherSymbol)> PlatformSymbols = new()
        {
            { "LeftMeta", ("\u2318", "\u229E") },     // ⌘ or ⊞
            { "RightMeta", ("\u2318", "\u229E") },
            { "LeftWindows", ("\u2318", "\u229E") },
            { "RightWindows", ("\u2318", "\u229E") },
            { "LeftCtrl", ("\u2303", "Ctrl") },      // ⌃ or Ctrl
            { "RightCtrl", ("\u2303", "Ctrl") },
            { "LeftAlt", ("\u2325", "Alt") },        // ⌥ or Alt
            { "RightAlt", ("\u2325", "Alt") },
            { "LeftShift", ("\u21E7", "Shift") },    // ⇧ or Shift
            { "RightShift", ("\u21E7", "Shift") },
            { "Backspace", ("\u232B", "BS") },       // ⌫ or BS
            { "Delete", ("\u2326", "Del") }          // ⌦ or Del
        };

        // Meta key maps to ⌘ on macOS, ⊞ on Windows/Linux
        private static bool IsMac =>
            UnityEngine.Application.platform == RuntimePlatform.OSXEditor ||
            UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;

        public static string GetSymbol(string keyName)
        {
            if (PlatformSymbols.TryGetValue(keyName, out (string macSymbol, string otherSymbol) platformSymbol))
            {
                return IsMac ? platformSymbol.macSymbol : platformSymbol.otherSymbol;
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
#endif
