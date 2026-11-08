using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Editor state captured when Unity has accepted a compile request but not started compiling for the
    /// whole pending timeout. Why these fields: the known cause is Unity believing a mouse button is held,
    /// which CanReloadAssemblies and the window-active flag discriminate while hotControl does not.
    /// </summary>
    internal sealed class CompileStartPendingDiagnostics
    {
        internal int PendingMs { get; }

        /// <summary>
        /// Unity's internal EditorApplication.CanReloadAssemblies; null when the Editor build does not expose it.
        /// </summary>
        internal bool? CanReloadAssemblies { get; }

        internal bool IsEditorWindowActive { get; }
        internal int HotControl { get; }
        internal bool IsEditorCompiling { get; }
        internal bool IsEditorUpdating { get; }
        internal bool IsEditorPlaying { get; }
        internal bool IsEditorPaused { get; }

        internal CompileStartPendingDiagnostics(
            int pendingMs,
            bool? canReloadAssemblies,
            bool isEditorWindowActive,
            int hotControl,
            bool isEditorCompiling,
            bool isEditorUpdating,
            bool isEditorPlaying,
            bool isEditorPaused)
        {
            Debug.Assert(pendingMs >= 0, "pendingMs must not be negative");

            PendingMs = pendingMs;
            CanReloadAssemblies = canReloadAssemblies;
            IsEditorWindowActive = isEditorWindowActive;
            HotControl = hotControl;
            IsEditorCompiling = isEditorCompiling;
            IsEditorUpdating = isEditorUpdating;
            IsEditorPlaying = isEditorPlaying;
            IsEditorPaused = isEditorPaused;
        }

        internal double PendingSeconds => PendingMs / 1000.0;

        /// <summary>
        /// Reads the current Editor state on the main thread.
        /// </summary>
        internal static CompileStartPendingDiagnostics Capture(int pendingMs)
        {
            return new CompileStartPendingDiagnostics(
                pendingMs,
                ReadCanReloadAssemblies(),
                InternalEditorUtility.isApplicationActive,
                GUIUtility.hotControl,
                EditorApplication.isCompiling,
                EditorApplication.isUpdating,
                EditorApplication.isPlaying,
                EditorApplication.isPaused);
        }

        // Why reflection: CanReloadAssemblies is internal to UnityEditor, and it is the one flag that
        // says whether Unity is withholding the compile because it thinks a mouse button is held.
        private static bool? ReadCanReloadAssemblies()
        {
            MethodInfo canReloadAssemblies = typeof(EditorApplication).GetMethod(
                "CanReloadAssemblies",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (canReloadAssemblies == null || canReloadAssemblies.GetParameters().Length != 0)
            {
                return null;
            }

            object value = canReloadAssemblies.Invoke(null, null);
            if (value is bool canReload)
            {
                return canReload;
            }

            return null;
        }

        /// <summary>
        /// Shapes the diagnostics for the compile response Details field.
        /// </summary>
        internal Dictionary<string, object> ToDetails()
        {
            return new Dictionary<string, object>
            {
                ["PendingSeconds"] = PendingSeconds,
                ["CanReloadAssemblies"] = CanReloadAssemblies,
                ["EditorWindowActive"] = IsEditorWindowActive,
                ["HotControl"] = HotControl,
                ["EditorCompiling"] = IsEditorCompiling,
                ["EditorUpdating"] = IsEditorUpdating,
                ["EditorPlaying"] = IsEditorPlaying,
                ["EditorPaused"] = IsEditorPaused
            };
        }

        /// <summary>
        /// Shapes the diagnostics for VibeLogger context, in the snake_case the other compile logs use.
        /// </summary>
        internal Dictionary<string, object> ToLogContext()
        {
            return new Dictionary<string, object>
            {
                ["pending_ms"] = PendingMs,
                ["can_reload_assemblies"] = CanReloadAssemblies,
                ["editor_window_active"] = IsEditorWindowActive,
                ["hot_control"] = HotControl
            };
        }
    }
}
