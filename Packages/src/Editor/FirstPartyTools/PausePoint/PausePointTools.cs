using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameters for arming one named pause point marker.
    /// </summary>
    public class ArmPausePointSchema : UnityCliLoopToolSchema
    {
        [Description("Named pause point id passed to UloopPausePoint.Hit.")]
        public string Id { get; set; } = string.Empty;

        [Description("Seconds before the arm expires and stops pausing late hits.")]
        public int TimeoutSeconds { get; set; } = UloopPausePointRegistry.DefaultTimeoutSeconds;
    }

    /// <summary>
    /// Parameters for clearing one or all pause point markers.
    /// </summary>
    public class ClearPausePointSchema : UnityCliLoopToolSchema
    {
        [Description("Named pause point id to clear.")]
        public string Id { get; set; } = string.Empty;

        [Description("Clear every active pause point marker.")]
        public bool All { get; set; }
    }

    /// <summary>
    /// Response shared by pause point tool commands.
    /// </summary>
    public class PausePointResponse : UnityCliLoopToolResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsArmed { get; set; }
        public bool IsHit { get; set; }
        public int HitCount { get; set; }
        public int TimeoutSeconds { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public int ClearedCount { get; set; }
        public string Message { get; set; } = string.Empty;

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
                IsArmed = snapshot.IsArmed,
                IsHit = snapshot.IsHit,
                HitCount = snapshot.HitCount,
                TimeoutSeconds = snapshot.TimeoutSeconds,
                ElapsedMilliseconds = snapshot.ElapsedMilliseconds,
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
                Message = "Pause points cleared."
            };
        }
    }

    /// <summary>
    /// Exposes pause point arming as a Unity CLI Loop tool.
    /// </summary>
    [UnityCliLoopTool]
    public class ArmPausePointTool : UnityCliLoopTool<ArmPausePointSchema, PausePointResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_ARM_PAUSE_POINT;

        protected override Task<PausePointResponse> ExecuteAsync(ArmPausePointSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Arm(parameters);
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
        public PausePointResponse Arm(ArmPausePointSchema parameters)
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

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Arm(id, parameters.TimeoutSeconds);
            return PausePointResponse.FromSnapshot(snapshot);
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
    }
}
