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

        /// <summary>
        /// Why new-source membership cannot be trusted, or null when it can. A compile or an import
        /// in progress ends on its own, so it is <see cref="HotReloadFailureKinds.EditorNotReady"/>.
        /// The errors a failed compile left stay until the reader fixes them, so that state is a
        /// <see cref="HotReloadFailureKinds.Declaration"/>.
        /// </summary>
        internal HotReloadFailureDescription GetNotReadyFailure()
        {
            if (IsCompiling)
            {
                return HotReloadFailureDescription.EditorNotReady(
                    "The Editor is compiling, so new source membership is not ready. Compile the project first and retry hot reload.");
            }

            if (IsUpdating)
            {
                return HotReloadFailureDescription.EditorNotReady(
                    "The Editor is importing assets, so new source membership is not ready. Wait for import to finish, then retry hot reload.");
            }

            if (ScriptCompilationFailed)
            {
                return HotReloadFailureDescription.Declaration(
                    "The last script compilation failed, so new source membership cannot be verified. Fix the compile errors, compile the project, and retry hot reload.");
            }

            return null;
        }

        /// <summary>
        /// Why a request that arrives now is refused before the transform, or null when it is not.
        /// Only a compile or an import in progress counts: both end on their own, so the CLI waits
        /// and applies again. A failed last compile is not busy, because the loaded assemblies are
        /// the last good build and waiting does not clear the errors.
        /// </summary>
        internal HotReloadFailureDescription GetBusyFailure()
        {
            if (IsCompiling)
            {
                return HotReloadFailureDescription.EditorNotReady(HotReloadConstants.EditorCompilingBeforeTransformReason);
            }

            if (IsUpdating)
            {
                return HotReloadFailureDescription.EditorNotReady(HotReloadConstants.EditorImportingBeforeTransformReason);
            }

            return null;
        }
    }

    /// <summary>
    /// Separates Editor-state collection from the pure readiness decision used by new-source
    /// admission, so a test can decide what state the admission path sees.
    /// </summary>
    internal interface IHotReloadEditorStateSnapshotCapture
    {
        HotReloadEditorStateSnapshot CaptureCurrent();
    }

    /// <summary>
    /// The production capture: reads the Editor's own compile and import flags.
    /// </summary>
    internal sealed class HotReloadEditorStateSnapshotCapture : IHotReloadEditorStateSnapshotCapture
    {
        public HotReloadEditorStateSnapshot CaptureCurrent()
        {
            return new HotReloadEditorStateSnapshot(
                EditorApplication.isCompiling,
                EditorApplication.isUpdating,
                EditorUtility.scriptCompilationFailed);
        }
    }
}
