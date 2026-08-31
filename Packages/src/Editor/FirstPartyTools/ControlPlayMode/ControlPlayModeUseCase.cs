using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes Unity Editor play mode state changes for the bundled control-play-mode tool.
    /// </summary>
    public class ControlPlayModeUseCase
    {
        public const int DefaultTimeoutSeconds = 180;

        private const string UnsavedEditorChangesSaveFailureMessage =
            "Play mode could not start because unsaved scene or prefab changes could not be saved.";
        private const string UnsavedEditorChangesRemainingFailureMessage =
            "Play mode could not start while the editor has unsaved scene or prefab changes.";

        // A fresh Play start looks identical to a resume in the response's "changed"/"message"
        // fields unless callers already know they expected a resume; this makes the distinction
        // explicit so a caller that expected to resume a paused session notices its state was lost.
        internal const string FreshPlayStartFromNewSessionWarning =
            "Play mode started a new session from Edit-time scene state. If you expected to resume "
            + "a paused session, that session had already ended (for example, a recompile with "
            + "\"Script Changes While Playing = Stop Playing And Recompile\" stops Play Mode); "
            + "re-establish your runtime state before continuing verification.";

        private readonly IControlPlayModeCompilationFailureProvider _compilationFailureProvider;
        private readonly IControlPlayModeCompilationFailureGate _compilationFailureGate;
        private readonly IEditorUnsavedChangesQuietSaver _unsavedChangesQuietSaver;
        private readonly IControlPlayModeEditorStateService _editorStateService;
        private readonly IControlPlayModeDomainReloadDropStateProvider _domainReloadDropStateProvider;
        private readonly IEditorFocusStateProvider _editorFocusStateProvider;

        public ControlPlayModeUseCase(
            IControlPlayModeCompilationFailureProvider compilationFailureProvider = null,
            IControlPlayModeCompilationFailureGate compilationFailureGate = null,
            IEditorUnsavedChangesQuietSaver unsavedChangesQuietSaver = null,
            IControlPlayModeEditorStateService editorStateService = null,
            IControlPlayModeDomainReloadDropStateProvider domainReloadDropStateProvider = null,
            IEditorFocusStateProvider editorFocusStateProvider = null)
        {
            _compilationFailureProvider =
                compilationFailureProvider ?? ControlPlayModeServices.CompilationFailureProvider;
            _compilationFailureGate =
                compilationFailureGate ?? ControlPlayModeServices.CompilationFailureGate;
            _unsavedChangesQuietSaver =
                unsavedChangesQuietSaver ?? new EditorUnsavedChangesQuietSaver();
            _editorStateService =
                editorStateService ?? ControlPlayModeServices.EditorStateService;
            _domainReloadDropStateProvider =
                domainReloadDropStateProvider ?? ControlPlayModeServices.DomainReloadDropStateProvider;
            _editorFocusStateProvider = editorFocusStateProvider ?? new EditorFocusStateProvider();
        }

        public Task<ControlPlayModeResponse> ExecuteAsync(ControlPlayModeSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            if (parameters.StatusOnly)
            {
                return Task.FromResult(CreateStatusOnlyResponse(parameters));
            }

            ControlPlayModeActionResult actionResult = ExecuteRequestedPlayModeAction(parameters.Action);
            if (actionResult.HasResponse)
            {
                return Task.FromResult(actionResult.Response);
            }

            return Task.FromResult(CreateResponse(
                actionResult.Message,
                actionResult.Changed,
                actionResult.WasAlreadyStopped,
                actionResult.ResumedFromPause,
                actionResult.Warning,
                parameters.Action));
        }

        private ControlPlayModeResponse CreateStatusOnlyResponse(ControlPlayModeSchema parameters)
        {
            if (ShouldBlockPlayForCompileErrors(parameters.Action, _editorStateService.IsPlaying))
            {
                ControlPlayModeCompileError[] compileErrors =
                    _compilationFailureProvider.GetLastFailedErrors();
                return CreateCompileErrorBlockedResponse(compileErrors);
            }

            return CreateResponse("Play mode status", false, false, action: parameters.Action);
        }

        private ControlPlayModeActionResult ExecuteRequestedPlayModeAction(PlayModeAction action)
        {
            string message;
            bool wasPaused = _editorStateService.IsPaused;
            bool wasPlaying = _editorStateService.IsPlaying;

            switch (action)
            {
                case PlayModeAction.Play:
                case PlayModeAction.Resume:
                    return ExecutePlayModeStart(wasPaused, wasPlaying);

                case PlayModeAction.Stop:
                    return ExecutePlayModeStop(wasPaused, wasPlaying);

                case PlayModeAction.Pause:
                    _editorStateService.IsPaused = true;
                    return ControlPlayModeActionResult.FromState("Play mode paused", !wasPaused, false);

                case PlayModeAction.Step:
                    return ExecutePlayModeStep(wasPlaying);

                case PlayModeAction.Status:
                    return CreateStatusActionResult();

                default:
                    message = $"Unknown action: {action}";
                    return ControlPlayModeActionResult.FromState(message, false, false);
            }
        }

        // Status reports the current compile-error blocker via a read-only check (no new compile
        // triggered), but never predicts BlockedByUnsavedChanges: that field means a save attempt
        // during this request failed, and Status makes no save attempt.
        private ControlPlayModeActionResult CreateStatusActionResult()
        {
            if (!_compilationFailureGate.HasScriptCompilationFailed())
            {
                return ControlPlayModeActionResult.FromState("Play mode status", false, false);
            }

            ControlPlayModeCompileError[] compileErrors =
                _compilationFailureProvider.GetLastFailedErrors() ?? Array.Empty<ControlPlayModeCompileError>();
            ControlPlayModeResponse response = CreateResponse(
                "Play mode status",
                false,
                false,
                action: PlayModeAction.Status);
            response.BlockedByCompileErrors = true;
            response.CompileErrors = compileErrors;
            response.CompileErrorCount = compileErrors.Length;
            return ControlPlayModeActionResult.FromResponse(response, false);
        }

        private bool ShouldBlockPlayForCompileErrors(PlayModeAction action, bool isPlaying)
        {
            // Why Resume too: it is a Play alias, including for StatusOnly IPC probes that bypass CLI wait normalization.
            bool isPlayOrResume = action == PlayModeAction.Play || action == PlayModeAction.Resume;
            return isPlayOrResume &&
                !isPlaying &&
                _compilationFailureGate.HasScriptCompilationFailed();
        }

        private ControlPlayModeActionResult ExecutePlayModeStart(bool wasPaused, bool wasPlaying)
        {
            // Captured before this method mutates editor state, so the warning reflects the
            // request-start snapshot the same way CompileUseCase does.
            int activeHotReloadPatchCount = _domainReloadDropStateProvider.GetActiveHotReloadPatchCount();
            int activePausePointCount = _domainReloadDropStateProvider.GetActivePausePointCount();
            bool isDomainReloadDisabledOnEnterPlayMode =
                _domainReloadDropStateProvider.IsDomainReloadDisabledOnEnterPlayMode();

            if (ShouldBlockPlayForCompileErrors(PlayModeAction.Play, wasPlaying))
            {
                ControlPlayModeCompileError[] compileErrors =
                    _compilationFailureProvider.GetLastFailedErrors();
                return ControlPlayModeActionResult.FromResponse(
                    CreateCompileErrorBlockedResponse(compileErrors),
                    true);
            }

            // Why: already-running Play used to report "Play mode started" even when Changed
            // was false, which made a no-op look like a new session.
            if (wasPlaying && !wasPaused)
            {
                return ControlPlayModeActionResult.FromState(
                    ControlPlayModeConstants.AlreadyRunningPlayMessage,
                    false,
                    false);
            }

            // Why only when entering Play from Edit: SaveScene does not work while already playing,
            // and resume-from-pause must not rewrite Scene assets.
            if (!wasPlaying)
            {
                ControlPlayModeActionResult saveResult = SaveDirtyEditorChangesBeforePlayStart();
                if (saveResult.HasResponse)
                {
                    return saveResult;
                }
            }

            if (wasPaused)
            {
                _editorStateService.IsPaused = false;
            }
            if (!_editorStateService.IsPlaying)
            {
                // Why: only CLI-started Play owns the override; manual Editor Play must keep project defaults.
                ControlPlayModeServices.RunInBackgroundService.EnableForCliPlayStart();
                _editorStateService.IsPlaying = true;
            }

            bool changed = wasPaused || !wasPlaying;
            bool resumedFromPause = wasPaused && wasPlaying;
            string message = wasPaused ? "Play mode resumed" : "Play mode started";
            string warning = JoinWarnings(
                wasPlaying ? string.Empty : FreshPlayStartFromNewSessionWarning,
                PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                    wasPlaying,
                    isDomainReloadDisabledOnEnterPlayMode,
                    activeHotReloadPatchCount,
                    activePausePointCount));
            return ControlPlayModeActionResult.FromState(message, changed, false, resumedFromPause, warning);
        }

        private static string JoinWarnings(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return string.IsNullOrEmpty(second) ? string.Empty : second;
            }

            if (string.IsNullOrEmpty(second))
            {
                return first;
            }

            return first + " " + second;
        }

        private ControlPlayModeActionResult SaveDirtyEditorChangesBeforePlayStart()
        {
            string[] failedChanges = _unsavedChangesQuietSaver.SaveUnsavedEditorChanges();
            Debug.Assert(failedChanges != null, "Unsaved editor change save must return an array");
            if (failedChanges.Length > 0)
            {
                return ControlPlayModeActionResult.FromResponse(
                    CreateSaveFailedResponse(UnsavedEditorChangesSaveFailureMessage, failedChanges),
                    false);
            }

            string[] remainingChanges = _unsavedChangesQuietSaver.DetectUnsavedEditorChanges();
            Debug.Assert(remainingChanges != null, "Unsaved editor change detection must return an array");
            if (remainingChanges.Length > 0)
            {
                return ControlPlayModeActionResult.FromResponse(
                    CreateSaveFailedResponse(UnsavedEditorChangesRemainingFailureMessage, remainingChanges),
                    false);
            }

            return ControlPlayModeActionResult.FromState(string.Empty, false, false);
        }

        private ControlPlayModeActionResult ExecutePlayModeStop(bool wasPaused, bool wasPlaying)
        {
            bool wasAlreadyStopped = !wasPlaying;
            if (wasPaused)
            {
                _editorStateService.IsPaused = false;
            }
            if (_editorStateService.IsPlaying)
            {
                _editorStateService.IsPlaying = false;
            }

            bool changed = wasPaused || wasPlaying;
            string message = wasAlreadyStopped ? "Play mode was already stopped" : "Play mode stopped";
            return ControlPlayModeActionResult.FromState(message, changed, wasAlreadyStopped);
        }

        private ControlPlayModeActionResult ExecutePlayModeStep(bool wasPlaying)
        {
            // Same API as the Editor's Next Frame button: advances one frame and
            // leaves the player paused, independent of Time.timeScale.
            if (!wasPlaying)
            {
                return ControlPlayModeActionResult.FromState(
                    "Play mode is not running. Step requires PlayMode; start it with --action Play first.",
                    false,
                    false);
            }

            _editorStateService.Step();
            return ControlPlayModeActionResult.FromState("Stepped one frame; play mode is paused.", true, false);
        }

        private ControlPlayModeResponse CreateResponse(
            string message,
            bool changed,
            bool wasAlreadyStopped,
            bool resumedFromPause = false,
            string warning = "",
            PlayModeAction action = PlayModeAction.Play)
        {
            ControlPlayModeResponse response = new()
            {
                IsPlaying = _editorStateService.IsPlaying,
                IsPaused = _editorStateService.IsPaused,
                Changed = changed,
                WasAlreadyStopped = wasAlreadyStopped,
                ResumedFromPause = resumedFromPause,
                CompileErrors = Array.Empty<ControlPlayModeCompileError>(),
                Message = message,
                Warning = JoinWarnings(
                    warning,
                    action == PlayModeAction.Status
                        ? EditorUnfocusedWarningBuilder.BuildPlayModeProgressHint(
                            _editorStateService.IsPlaying,
                            _editorFocusStateProvider.IsFocused)
                        : string.Empty)
            };
            PlayModeStopReasonResponseFiller.CopyConfirmedIfNeeded(response, action, wasAlreadyStopped);

            return response;
        }

        private ControlPlayModeResponse CreateSaveFailedResponse(string messagePrefix, string[] failedChanges)
        {
            Debug.Assert(!string.IsNullOrEmpty(messagePrefix), "messagePrefix must not be null or empty");
            Debug.Assert(failedChanges != null, "failedChanges must not be null");
            Debug.Assert(failedChanges.Length > 0, "failedChanges must not be empty");

            string message = messagePrefix + " Unsaved changes: " + string.Join(", ", failedChanges);
            ControlPlayModeResponse response = CreateResponse(message, false, false);
            response.BlockedByUnsavedChanges = true;
            return response;
        }

        private ControlPlayModeResponse CreateCompileErrorBlockedResponse(
            ControlPlayModeCompileError[] compileErrors)
        {
            ControlPlayModeCompileError[] errors = compileErrors ?? Array.Empty<ControlPlayModeCompileError>();
            string message = errors.Length == 0
                ? "Play mode could not start because Unity reports script compilation failed, but no saved compiler diagnostics are available. Run `uloop compile` or `uloop get-logs` for details."
                : "Play mode could not start because Unity has compiler errors.";

            ControlPlayModeResponse response = CreateResponse(message, false, false);
            response.BlockedByCompileErrors = true;
            response.CompileErrors = errors;
            response.CompileErrorCount = errors.Length;
            return response;
        }

        private readonly struct ControlPlayModeActionResult
        {
            private ControlPlayModeActionResult(
                string message,
                bool changed,
                bool wasAlreadyStopped,
                ControlPlayModeResponse response,
                bool hasResponse,
                bool resumedFromPause,
                string warning)
            {
                Message = message;
                Changed = changed;
                WasAlreadyStopped = wasAlreadyStopped;
                Response = response;
                HasResponse = hasResponse;
                ResumedFromPause = resumedFromPause;
                Warning = warning;
            }

            public static ControlPlayModeActionResult FromState(
                string message,
                bool changed,
                bool wasAlreadyStopped,
                bool resumedFromPause = false,
                string warning = "")
            {
                return new ControlPlayModeActionResult(
                    message,
                    changed,
                    wasAlreadyStopped,
                    null,
                    false,
                    resumedFromPause,
                    warning);
            }

            public static ControlPlayModeActionResult FromResponse(
                ControlPlayModeResponse response,
                bool changed)
            {
                return new ControlPlayModeActionResult(
                    response.Message,
                    changed,
                    response.WasAlreadyStopped,
                    response,
                    true,
                    false,
                    string.Empty);
            }

            public string Message { get; }
            public bool Changed { get; }
            public bool WasAlreadyStopped { get; }
            public ControlPlayModeResponse Response { get; }
            public bool HasResponse { get; }
            public bool ResumedFromPause { get; }
            public string Warning { get; }
        }
    }
}
