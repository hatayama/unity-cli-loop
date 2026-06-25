using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes Unity Editor play mode state changes for the bundled control-play-mode tool.
    /// </summary>
    public class ControlPlayModeUseCase
    {
        public const int DefaultTimeoutSeconds = 180;

        private readonly IControlPlayModeCompilationFailureProvider _compilationFailureProvider;
        private readonly IControlPlayModeCompilationFailureGate _compilationFailureGate;

        public ControlPlayModeUseCase(
            IControlPlayModeCompilationFailureProvider compilationFailureProvider = null,
            IControlPlayModeCompilationFailureGate compilationFailureGate = null)
        {
            _compilationFailureProvider =
                compilationFailureProvider ?? ControlPlayModeServices.CompilationFailureProvider;
            _compilationFailureGate =
                compilationFailureGate ?? ControlPlayModeServices.CompilationFailureGate;
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
                actionResult.WasAlreadyStopped));
        }

        private ControlPlayModeResponse CreateStatusOnlyResponse(ControlPlayModeSchema parameters)
        {
            if (ShouldBlockPlayForCompileErrors(parameters.Action, EditorApplication.isPlaying))
            {
                ControlPlayModeCompileError[] compileErrors =
                    _compilationFailureProvider.GetLastFailedErrors();
                return CreateCompileErrorBlockedResponse(compileErrors);
            }

            return CreateResponse("Play mode status", false, false);
        }

        private ControlPlayModeActionResult ExecuteRequestedPlayModeAction(PlayModeAction action)
        {
            string message;
            bool wasPaused = EditorApplication.isPaused;
            bool wasPlaying = EditorApplication.isPlaying;

            switch (action)
            {
                case PlayModeAction.Play:
                    return ExecutePlayModeStart(wasPaused, wasPlaying);

                case PlayModeAction.Stop:
                    return ExecutePlayModeStop(wasPaused, wasPlaying);

                case PlayModeAction.Pause:
                    EditorApplication.isPaused = true;
                    return ControlPlayModeActionResult.FromState("Play mode paused", !wasPaused, false);

                case PlayModeAction.Step:
                    return ExecutePlayModeStep(wasPlaying);

                default:
                    message = $"Unknown action: {action}";
                    return ControlPlayModeActionResult.FromState(message, false, false);
            }
        }

        private bool ShouldBlockPlayForCompileErrors(PlayModeAction action, bool isPlaying)
        {
            return action == PlayModeAction.Play &&
                !isPlaying &&
                _compilationFailureGate.HasScriptCompilationFailed();
        }

        private ControlPlayModeActionResult ExecutePlayModeStart(bool wasPaused, bool wasPlaying)
        {
            if (ShouldBlockPlayForCompileErrors(PlayModeAction.Play, wasPlaying))
            {
                ControlPlayModeCompileError[] compileErrors =
                    _compilationFailureProvider.GetLastFailedErrors();
                return ControlPlayModeActionResult.FromResponse(
                    CreateCompileErrorBlockedResponse(compileErrors),
                    true);
            }

            if (wasPaused)
            {
                EditorApplication.isPaused = false;
            }
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }

            bool changed = wasPaused || !wasPlaying;
            string message = wasPaused ? "Play mode resumed" : "Play mode started";
            return ControlPlayModeActionResult.FromState(message, changed, false);
        }

        private static ControlPlayModeActionResult ExecutePlayModeStop(bool wasPaused, bool wasPlaying)
        {
            bool wasAlreadyStopped = !wasPlaying;
            if (wasPaused)
            {
                EditorApplication.isPaused = false;
            }
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            bool changed = wasPaused || wasPlaying;
            string message = wasAlreadyStopped ? "Play mode was already stopped" : "Play mode stopped";
            return ControlPlayModeActionResult.FromState(message, changed, wasAlreadyStopped);
        }

        private static ControlPlayModeActionResult ExecutePlayModeStep(bool wasPlaying)
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

            EditorApplication.Step();
            return ControlPlayModeActionResult.FromState("Stepped one frame; play mode is paused.", true, false);
        }

        private static ControlPlayModeResponse CreateResponse(string message, bool changed, bool wasAlreadyStopped)
        {
            ControlPlayModeResponse response = new()
            {
                IsPlaying = EditorApplication.isPlaying,
                IsPaused = EditorApplication.isPaused,
                Changed = changed,
                WasAlreadyStopped = wasAlreadyStopped,
                CompileErrors = Array.Empty<ControlPlayModeCompileError>(),
                Message = message
            };

            return response;
        }

        private static ControlPlayModeResponse CreateCompileErrorBlockedResponse(
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
                bool hasResponse)
            {
                Message = message;
                Changed = changed;
                WasAlreadyStopped = wasAlreadyStopped;
                Response = response;
                HasResponse = hasResponse;
            }

            public static ControlPlayModeActionResult FromState(
                string message,
                bool changed,
                bool wasAlreadyStopped)
            {
                return new ControlPlayModeActionResult(
                    message,
                    changed,
                    wasAlreadyStopped,
                    null,
                    false);
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
                    true);
            }

            public string Message { get; }
            public bool Changed { get; }
            public bool WasAlreadyStopped { get; }
            public ControlPlayModeResponse Response { get; }
            public bool HasResponse { get; }
        }
    }
}
