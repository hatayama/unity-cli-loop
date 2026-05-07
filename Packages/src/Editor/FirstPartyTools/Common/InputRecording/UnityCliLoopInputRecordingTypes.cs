#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Unity CLI Loop Record Input operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopRecordInputService
    {
        Task<UnityCliLoopRecordInputResult> RecordInputAsync(UnityCliLoopRecordInputRequest request, CancellationToken ct);
    }

    /// <summary>
    /// Defines the Unity CLI Loop Replay Input operations required by the owning workflow.
    /// </summary>
    public interface IUnityCliLoopReplayInputService
    {
        Task<UnityCliLoopReplayInputResult> ReplayInputAsync(UnityCliLoopReplayInputRequest request, CancellationToken ct);
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Record Input behavior.
    /// </summary>
    public sealed class UnityCliLoopRecordInputRequest
    {
        public RecordInputAction Action { get; set; } = RecordInputAction.Start;
        public string OutputPath { get; set; } = "";
        public string Keys { get; set; } = "";
        public int DelaySeconds { get; set; } = 3;
        public bool ShowOverlay { get; set; } = true;
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Record Input behavior.
    /// </summary>
    public sealed class UnityCliLoopRecordInputResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? OutputPath { get; set; }
        public int? TotalFrames { get; set; }
        public float? DurationSeconds { get; set; }
    }

    /// <summary>
    /// Carries the request data needed for Unity CLI Loop Replay Input behavior.
    /// </summary>
    public sealed class UnityCliLoopReplayInputRequest
    {
        public ReplayInputAction Action { get; set; } = ReplayInputAction.Start;
        public string InputPath { get; set; } = "";
        public bool ShowOverlay { get; set; } = true;
        public bool Loop { get; set; }
    }

    /// <summary>
    /// Carries the result data produced by Unity CLI Loop Replay Input behavior.
    /// </summary>
    public sealed class UnityCliLoopReplayInputResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? InputPath { get; set; }
        public int? CurrentFrame { get; set; }
        public int? TotalFrames { get; set; }
        public float? Progress { get; set; }
        public bool? IsReplaying { get; set; }
    }
}
