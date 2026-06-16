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
                if (parameters.Action == PlayModeAction.Play &&
                    !EditorApplication.isPlaying &&
                    _compilationFailureGate.HasScriptCompilationFailed())
                {
                    ControlPlayModeCompileError[] compileErrors =
                        _compilationFailureProvider.GetLastFailedErrors();
                    return Task.FromResult(CreateCompileErrorBlockedResponse(compileErrors));
                }

                return Task.FromResult(CreateResponse("Play mode status", false, false));
            }

            string message;
            bool wasPaused = EditorApplication.isPaused;
            bool wasPlaying = EditorApplication.isPlaying;
            bool changed = false;
            bool wasAlreadyStopped = false;

            switch (parameters.Action)
            {
                case PlayModeAction.Play:
                    if (!wasPlaying && _compilationFailureGate.HasScriptCompilationFailed())
                    {
                        ControlPlayModeCompileError[] compileErrors =
                            _compilationFailureProvider.GetLastFailedErrors();
                        return Task.FromResult(CreateCompileErrorBlockedResponse(compileErrors));
                    }

                    if (wasPaused)
                    {
                        EditorApplication.isPaused = false;
                    }
                    if (!EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = true;
                    }
                    changed = wasPaused || !wasPlaying;
                    message = wasPaused ? "Play mode resumed" : "Play mode started";
                    break;

                case PlayModeAction.Stop:
                    wasAlreadyStopped = !wasPlaying;
                    if (wasPaused)
                    {
                        EditorApplication.isPaused = false;
                    }
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                    }
                    changed = wasPaused || wasPlaying;
                    message = wasAlreadyStopped ? "Play mode was already stopped" : "Play mode stopped";
                    break;

                case PlayModeAction.Pause:
                    EditorApplication.isPaused = true;
                    changed = !wasPaused;
                    message = "Play mode paused";
                    break;

                case PlayModeAction.Step:
                    // Same API as the Editor's Next Frame button: advances one frame and
                    // leaves the player paused, independent of Time.timeScale.
                    if (!wasPlaying)
                    {
                        message = "Play mode is not running. Step requires PlayMode; start it with --action Play first.";
                        break;
                    }
                    EditorApplication.Step();
                    changed = true;
                    message = "Stepped one frame; play mode is paused.";
                    break;

                default:
                    message = $"Unknown action: {parameters.Action}";
                    break;
            }

            return Task.FromResult(CreateResponse(message, changed, wasAlreadyStopped));
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
    }
}
