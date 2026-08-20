using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Confirms the pending Play Mode stop reason when Play Mode exits, and records
    /// script-compilation as a fallback when no explicit CLI stop is pending.
    /// </summary>
    internal static class PlayModeStopReasonSubscriber
    {
        // Why not EditorRuntimeStateSnapshotSubscriber: Infrastructure's asmdef allowlist cannot
        // reference ControlPlayMode, so this feature-owner assembly owns the same startup hook pattern.
        internal static void InitializeForEditorStartup()
        {
            CompilationPipeline.compilationStarted -= HandleCompilationStarted;
            CompilationPipeline.compilationStarted += HandleCompilationStarted;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        internal static void HandleCompilationStarted(object context)
        {
            PlayModeStopReasonSessionStore.TrySetPending(
                ControlPlayModeConstants.StoppedByScriptCompilation);
        }

        internal static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            PlayModeStopReasonSessionStore.ConfirmPending(
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
    }
}
