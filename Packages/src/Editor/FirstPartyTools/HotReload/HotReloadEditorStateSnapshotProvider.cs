using System;

using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures the Editor state that makes a new-source assembly membership unsafe to trust.
    /// </summary>
    internal sealed class HotReloadEditorStateSnapshot
    {
        internal HotReloadEditorStateSnapshot(bool isCompiling, bool isUpdating, bool scriptCompilationFailed)
        {
            IsCompiling = isCompiling;
            IsUpdating = isUpdating;
            ScriptCompilationFailed = scriptCompilationFailed;
        }

        internal bool IsCompiling { get; }

        internal bool IsUpdating { get; }

        internal bool ScriptCompilationFailed { get; }
    }

    /// <summary>
    /// Separates Editor-state collection from the pure readiness decision used by new-source admission.
    /// </summary>
    internal static class HotReloadEditorStateSnapshotProvider
    {
        // Why a replaceable seam: the real Editor flags are process state, while tests must prove
        // that each unsafe state reaches the production resolver without starting a group.
        internal static Func<HotReloadEditorStateSnapshot> CaptureForTesting = Capture;

        internal static HotReloadEditorStateSnapshot CaptureCurrent()
        {
            return CaptureForTesting();
        }

        internal static string GetNotReadyReason(HotReloadEditorStateSnapshot snapshot)
        {
            if (snapshot.IsCompiling)
            {
                return "The Editor is compiling, so new source membership is not ready. Compile the project first and retry hot reload.";
            }

            if (snapshot.IsUpdating)
            {
                return "The Editor is importing assets, so new source membership is not ready. Wait for import to finish, then retry hot reload.";
            }

            if (snapshot.ScriptCompilationFailed)
            {
                return "The last script compilation failed, so new source membership cannot be verified. Fix the compile errors, compile the project, and retry hot reload.";
            }

            return null;
        }

        private static HotReloadEditorStateSnapshot Capture()
        {
            return new HotReloadEditorStateSnapshot(
                EditorApplication.isCompiling,
                EditorApplication.isUpdating,
                EditorUtility.scriptCompilationFailed);
        }
    }
}
