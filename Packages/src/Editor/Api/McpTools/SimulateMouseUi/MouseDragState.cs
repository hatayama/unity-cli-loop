#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace io.github.hatayama.UnityCliLoop
{
    // Tool instances are created fresh per invocation, so drag state
    // between DragStart/DragMove/DragEnd must be held statically.
    [InitializeOnLoad]
    internal static class MouseDragState
    {
        internal static bool IsDragging => Target != null && PointerData != null;
        internal static GameObject? Target { get; set; }
        internal static PointerEventData? PointerData { get; set; }

        static MouseDragState()
        {
            // PlayMode exit leaves dangling references to destroyed GameObjects
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal static void Clear()
        {
            Target = null;
            PointerData = null;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Clear();
                SimulateMouseUiOverlayState.Clear();
            }
        }
    }
}
