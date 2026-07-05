using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for enabling one named pause point marker.
    /// </summary>
    public class EnablePausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public int TimeoutSeconds { get; set; } = UloopPausePointRegistry.DefaultTimeoutSeconds;
    }

    /// <summary>
    /// Parameters for clearing one or all pause point markers.
    /// </summary>
    public class ClearPausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public bool All { get; set; }
    }

    /// <summary>
    /// Response shared by pause point tool commands.
    /// </summary>
    public class PausePointResponse : UnityCliLoopToolResponse
    {
        // Defaults to true so existing snapshot/clear paths keep their prior semantics.
        // Only explicit validation failures set this to false.
        public bool Success { get; set; } = true;
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public bool Expired { get; set; }
        public string EnabledAtUtc { get; set; } = string.Empty;
        public long ElapsedSinceEnabledMilliseconds { get; set; }
        public long RemainingMilliseconds { get; set; }
        public int Generation { get; set; }
        public PausePointEditorState EditorState { get; set; } = new();
        public string FirstHitAtUtc { get; set; } = string.Empty;
        public string LastHitAtUtc { get; set; } = string.Empty;
        public int FirstHitSequence { get; set; }
        public int LastHitSequence { get; set; }
        public int ClearedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public string RecommendedNextAction { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;

        internal static PausePointResponse FromSnapshot(UloopPausePointSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointResponse
            {
                Id = snapshot.Id,
                Status = snapshot.Status,
                IsEnabled = snapshot.IsEnabled,
                IsHit = snapshot.IsHit,
                HitCount = snapshot.HitCount,
                TimeoutSeconds = snapshot.TimeoutSeconds,
                Expired = snapshot.Expired,
                EnabledAtUtc = snapshot.EnabledAtUtc,
                ElapsedSinceEnabledMilliseconds = snapshot.ElapsedSinceEnabledMilliseconds,
                RemainingMilliseconds = snapshot.RemainingMilliseconds,
                Generation = snapshot.Generation,
                EditorState = PausePointEditorState.FromSnapshot(snapshot.EditorState),
                FirstHitAtUtc = snapshot.FirstHitAtUtc,
                LastHitAtUtc = snapshot.LastHitAtUtc,
                FirstHitSequence = snapshot.FirstHitSequence,
                LastHitSequence = snapshot.LastHitSequence,
                Message = snapshot.Message,
                RecommendedNextAction = snapshot.RecommendedNextAction
            };
        }

        internal static PausePointResponse FromClearAll(UloopPausePointClearAllResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new PausePointResponse
            {
                Status = UloopPausePointStatus.Cleared,
                ClearedCount = result.ClearedCount,
                EditorState = PausePointEditorState.FromSnapshot(result.EditorState),
                Message = result.ClearedCount == 0
                    ? "No active pause points to clear."
                    : "Pause points cleared."
            };
        }
    }

    /// <summary>
    /// Unity Editor play state attached to pause point tool evidence.
    /// </summary>
    public class PausePointEditorState
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string CapturedAt { get; set; } = string.Empty;

        internal static PausePointEditorState FromSnapshot(UloopPausePointEditorStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new PausePointEditorState
            {
                IsPlaying = snapshot.IsPlaying,
                IsPaused = snapshot.IsPaused,
                CapturedAt = snapshot.CapturedAt
            };
        }
    }

    /// <summary>
    /// Exposes pause point enabling as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class EnablePausePointTool : UnityCliLoopTool<EnablePausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_ENABLE_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(EnablePausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Enable(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Exposes pause point clearing as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class ClearPausePointTool : UnityCliLoopTool<ClearPausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_CLEAR_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(ClearPausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Clear(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Coordinates pause point tool validation and registry updates.
    /// </summary>
    internal sealed class PausePointUseCase
    {
        public PausePointResponse Enable(EnablePausePointSchema parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            string idError = ValidateId(parameters.Id);
            if (idError != null)
            {
                return CreateValidationFailure(idError);
            }

            if (parameters.TimeoutSeconds <= 0)
            {
                return CreateValidationFailure("TimeoutSeconds must be greater than zero.");
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(parameters.Id, parameters.TimeoutSeconds);
            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            response.Warning = CreateEnableWarning();
            return response;
        }

        public PausePointResponse Clear(ClearPausePointSchema parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (parameters.All)
            {
                UloopPausePointClearAllResult clearAllResult = UloopPausePointRegistry.ClearAll();
                return PausePointResponse.FromClearAll(clearAllResult);
            }

            string idError = ValidateId(parameters.Id);
            if (idError != null)
            {
                return CreateValidationFailure(idError);
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Clear(parameters.Id);
            return PausePointResponse.FromSnapshot(snapshot);
        }

        // Returns an error message when id fails validation, or null when it is valid.
        private static string ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "Id must not be null or empty.";
            }

            return null;
        }

        private static PausePointResponse CreateValidationFailure(string message)
        {
            return new PausePointResponse
            {
                Success = false,
                Message = message
            };
        }

        private static string CreateEnableWarning()
        {
            if (EditorApplication.isPlaying)
            {
                return string.Empty;
            }

            if (IsDomainReloadDisabledOnEnterPlayMode())
            {
                return string.Empty;
            }

            return "Pause point was enabled before PlayMode while Domain Reload is enabled. " +
                   "Entering PlayMode may clear this marker; keep Domain Reload disabled for this workflow or enable the marker after PlayMode starts.";
        }

        private static bool IsDomainReloadDisabledOnEnterPlayMode()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return false;
            }

            return (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        }
    }
}
