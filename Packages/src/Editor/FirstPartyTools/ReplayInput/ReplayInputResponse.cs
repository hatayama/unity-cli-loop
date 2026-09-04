#nullable enable

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Replay Input tool.
    /// </summary>
    public class ReplayInputResponse : UnityCliLoopToolResponse
    {
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? InputPath { get; set; }
        public int? CurrentFrame { get; set; }
        public int? TotalFrames { get; set; }
        public float? Progress { get; set; }
        public bool? IsReplaying { get; set; }

        /// <summary>
        /// Id of the pause point that refused this call before it ran anything, null otherwise. A
        /// refusal means nothing was started, so a caller reading only Success would miss that the
        /// action never happened. The CLI's --trigger diagnosis compares this against the marker it
        /// awaits.
        /// </summary>
        public string? RejectedByActivePausePointId { get; set; }

        /// <summary>
        /// True when this command was refused before it did anything: the PlayMode preflight
        /// rejected it. Why a separate flag from RejectedByActivePausePointId: a refusal is not
        /// always owned by a pause point (PlayMode simply not running is the common case), and the
        /// CLI's --trigger wait has to abort on "the trigger performed no action" without matching
        /// message text. Mid-flight failures leave this false.
        /// </summary>
        public bool RejectedBeforeExecution { get; set; }
    }
}
