using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Test Execution State Validation operations for its owning module.
    /// </summary>
    public class TestExecutionStateValidationService
    {
        private const string PlayModePausedMessage =
            "PlayMode is paused. Tests cannot run while paused because tool continuations " +
            "are not executed during pause, which would hang the test runner. " +
            "Use control-play-mode --action Play to resume, " +
            "or clear-pause-point --all if a pause point caused the pause.";

        private const string EditModePlayModeBlockedMessage =
            "EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests.";

        private const string UnsavedEditorChangesFailureMessage =
            "Tests cannot run while the editor has unsaved scene or prefab changes. Save or discard these changes before running tests.";
        private const string UnsavedEditorChangesSaveFailureMessage =
            "Tests cannot save unsaved scene or prefab changes before running tests.";

        private readonly IEditorUnsavedChangesQuietSaver _unsavedChangesQuietSaver;

        public TestExecutionStateValidationService()
            : this(new EditorUnsavedChangesQuietSaver())
        {
        }

        public TestExecutionStateValidationService(IEditorUnsavedChangesQuietSaver unsavedChangesQuietSaver)
        {
            Debug.Assert(unsavedChangesQuietSaver != null, "unsavedChangesQuietSaver must not be null");
            _unsavedChangesQuietSaver = unsavedChangesQuietSaver;
        }

        protected virtual bool IsPlaying => EditorApplication.isPlaying;
        protected virtual bool IsPaused => EditorApplication.isPaused;
        protected virtual bool IsCompiling => EditorApplication.isCompiling;
        protected virtual bool IsUpdating => EditorApplication.isUpdating;
        protected virtual string[] DetectUnsavedEditorChanges()
        {
            return _unsavedChangesQuietSaver.DetectUnsavedEditorChanges();
        }
        protected virtual ValidationResult SaveUnsavedEditorChanges()
        {
            string[] failedChanges = _unsavedChangesQuietSaver.SaveUnsavedEditorChanges();
            Debug.Assert(failedChanges != null, "Unsaved editor change save must return an array");
            if (failedChanges.Length > 0)
            {
                return ValidationResult.Failure(CreateUnsavedEditorChangesSaveFailureMessage(failedChanges));
            }

            return ValidationResult.Success();
        }

        public virtual ValidationResult Validate(UnityCliLoopTestMode testMode, bool saveBeforeRun)
        {
            if (IsCompiling)
            {
                return ValidationResult.Failure("Tests cannot run while compilation is in progress");
            }

            if (IsUpdating)
            {
                return ValidationResult.Failure("Tests cannot run while the editor is updating");
            }

            if (testMode == UnityCliLoopTestMode.EditMode && IsPlaying)
            {
                return ValidationResult.Failure(EditModePlayModeBlockedMessage);
            }

            if (testMode == UnityCliLoopTestMode.PlayMode && IsPaused)
            {
                return ValidationResult.Failure(PlayModePausedMessage);
            }

            if (saveBeforeRun)
            {
                ValidationResult saveResult = SaveUnsavedEditorChanges();
                if (!saveResult.IsValid)
                {
                    return saveResult;
                }
            }

            string[] unsavedEditorChanges = DetectUnsavedEditorChanges();
            Debug.Assert(unsavedEditorChanges != null, "Unsaved editor change detection must return an array");
            if (unsavedEditorChanges.Length > 0)
            {
                return ValidationResult.Failure(CreateUnsavedEditorChangesFailureMessage(unsavedEditorChanges));
            }

            return ValidationResult.Success();
        }

        private static string CreateUnsavedEditorChangesFailureMessage(string[] unsavedEditorChanges)
        {
            Debug.Assert(unsavedEditorChanges != null, "unsavedEditorChanges must not be null");
            Debug.Assert(unsavedEditorChanges.Length > 0, "unsavedEditorChanges must not be empty");

            return UnsavedEditorChangesFailureMessage + " Unsaved changes: " + string.Join(", ", unsavedEditorChanges);
        }

        private static string CreateUnsavedEditorChangesSaveFailureMessage(string[] failedChanges)
        {
            Debug.Assert(failedChanges != null, "failedChanges must not be null");
            Debug.Assert(failedChanges.Length > 0, "failedChanges must not be empty");

            return UnsavedEditorChangesSaveFailureMessage + " Unsaved changes that failed to save: " + string.Join(", ", failedChanges);
        }
    }
}
