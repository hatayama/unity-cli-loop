#nullable enable

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Record Input tool.
    /// </summary>
    public class RecordInputResponse : UnityCliLoopToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? OutputPath { get; set; }
        public int? TotalFrames { get; set; }
        public float? DurationSeconds { get; set; }
    }
}
