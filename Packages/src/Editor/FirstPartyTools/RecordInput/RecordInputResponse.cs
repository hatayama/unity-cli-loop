#nullable enable

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Record Input tool.
    /// </summary>
    public class RecordInputResponse : UnityCliLoopToolResponse
    {
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? OutputPath { get; set; }
        public int? TotalFrames { get; set; }
        public float? DurationSeconds { get; set; }

        /// <summary>
        /// Id of the pause point that refused this call before it ran anything, null otherwise. A
        /// refusal means nothing was started, so a caller reading only Success would miss that the
        /// action never happened. The CLI's --trigger diagnosis compares this against the marker it
        /// awaits.
        /// </summary>
        public string? RejectedByActivePausePointId { get; set; }
    }
}
