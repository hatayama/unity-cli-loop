using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for enabling one named debug break marker.
    /// </summary>
    public class EnablePausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public int TimeoutSeconds { get; set; } = UloopPausePointRegistry.DefaultTimeoutSeconds;
    }

    /// <summary>
    /// Parameters for clearing one or all debug break markers.
    /// </summary>
    public class ClearPausePointSchema : UnityCliLoopToolSchema
    {
        public string Id { get; set; } = string.Empty;

        public bool All { get; set; }
    }

    /// <summary>
    /// Response shared by debug break tool commands.
    /// </summary>
    public class PausePointResponse : UnityCliLoopToolResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public long ElapsedSinceEnabledMilliseconds { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public int ClearedCount { get; set; }
        public string Message { get; set; } = string.Empty;
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
                ElapsedSinceEnabledMilliseconds = snapshot.ElapsedSinceEnabledMilliseconds,
                IsPlaying = snapshot.IsPlaying,
                IsPaused = snapshot.IsPaused,
                Message = snapshot.Message
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
                Message = "Debug breaks cleared."
            };
        }
    }

    /// <summary>
    /// Exposes debug break enabling as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class EnablePausePointTool : UnityCliLoopTool<EnablePausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_ENABLE_DEBUG_BREAK;

        protected override Task<PausePointResponse> ExecuteAsync(EnablePausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Enable(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Exposes debug break clearing as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class ClearPausePointTool : UnityCliLoopTool<ClearPausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_CLEAR_DEBUG_BREAK;

        protected override Task<PausePointResponse> ExecuteAsync(ClearPausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Clear(parameters);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Coordinates debug break tool validation and registry updates.
    /// </summary>
    internal sealed class PausePointUseCase
    {
        public PausePointResponse Enable(EnablePausePointSchema parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            string id = RequireId(parameters.Id);
            if (parameters.TimeoutSeconds <= 0)
            {
                throw new UnityCliLoopToolParameterValidationException("TimeoutSeconds must be greater than zero.");
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(id, parameters.TimeoutSeconds);
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

            string id = RequireId(parameters.Id);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Clear(id);
            return PausePointResponse.FromSnapshot(snapshot);
        }

        private static string RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new UnityCliLoopToolParameterValidationException("Id must not be null or empty.");
            }

            return id;
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

            return "Debug break was enabled before PlayMode while Domain Reload is enabled. " +
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
