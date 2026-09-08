#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds open EditorWindows by title so multiple tools can target the same window the user sees.
    /// </summary>
    public static class EditorWindowFinder
    {
        /// <summary>
        /// Find all EditorWindows matching the given name (title bar text).
        /// </summary>
        /// <param name="windowName">Window name displayed in the title bar (e.g., "Console", "Inspector")</param>
        /// <param name="matchMode">Matching mode: exact, prefix, or contains (all case-insensitive)</param>
        /// <returns>Array of matching EditorWindows (empty if none found)</returns>
        public static EditorWindow[] FindWindowsByName(string windowName, WindowMatchMode matchMode = WindowMatchMode.exact)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                return Array.Empty<EditorWindow>();
            }

            List<EditorWindow> matchingWindows = new();
            EditorWindow[] allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (EditorWindow window in allWindows)
            {
                if (window.titleContent == null)
                {
                    continue;
                }

                // GUIContent.text can be assigned null by a window, and every match branch
                // below calls an instance method on it.
                string title = window.titleContent.text ?? string.Empty;
                bool isMatch = matchMode switch
                {
                    WindowMatchMode.exact => title.Equals(windowName, StringComparison.OrdinalIgnoreCase),
                    WindowMatchMode.prefix => title.StartsWith(windowName, StringComparison.OrdinalIgnoreCase),
                    WindowMatchMode.contains => title.Contains(windowName, StringComparison.OrdinalIgnoreCase),
                    _ => title.Equals(windowName, StringComparison.OrdinalIgnoreCase)
                };

                if (isMatch)
                {
                    matchingWindows.Add(window);
                }
            }

            return matchingWindows.ToArray();
        }

        /// <summary>
        /// Get a list of all open EditorWindow names.
        /// </summary>
        /// <returns>Array of window names</returns>
        public static string[] GetOpenWindowNames()
        {
            EditorWindow[] allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            List<string> names = new();

            foreach (EditorWindow window in allWindows)
            {
                if (window.titleContent != null && !string.IsNullOrEmpty(window.titleContent.text))
                {
                    names.Add(window.titleContent.text);
                }
            }

            return names.ToArray();
        }
    }
}
