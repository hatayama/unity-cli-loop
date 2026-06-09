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

        public Task<ControlPlayModeResponse> ExecuteAsync(ControlPlayModeSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            if (parameters.StatusOnly)
            {
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
                Message = message
            };

            return response;
        }
    }
}
