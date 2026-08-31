#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Mouse Drag State operations for its owning module.
    /// </summary>
    internal sealed class MouseDragStateService
    {
        internal bool IsDragging => Target != null && PointerData != null;
        internal GameObject? Target { get; set; }
        internal PointerEventData? PointerData { get; set; }

        internal void RegisterPlayModeCallbacks()
        {
            // PlayMode exit leaves dangling references to destroyed GameObjects
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        internal void Clear()
        {
            Target = null;
            PointerData = null;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Clear();
                SimulateMouseUiOverlayState.Clear();
            }
        }
    }

    /// <summary>
    /// Stores Mouse Drag state shared by the owning workflow.
    /// </summary>
    internal static class MouseDragState
    {
        private static readonly MouseDragStateService ServiceValue = new MouseDragStateService();

        internal static void InitializeForEditorStartup()
        {
            ServiceValue.RegisterPlayModeCallbacks();
        }

        internal static bool IsDragging => ServiceValue.IsDragging;

        internal static GameObject? Target
        {
            get => ServiceValue.Target;
            set => ServiceValue.Target = value;
        }

        internal static PointerEventData? PointerData
        {
            get => ServiceValue.PointerData;
            set => ServiceValue.PointerData = value;
        }

        internal static void Clear()
        {
            ServiceValue.Clear();
        }
    }
}
