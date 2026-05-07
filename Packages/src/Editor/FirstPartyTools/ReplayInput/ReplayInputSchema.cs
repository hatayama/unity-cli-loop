
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Replay Input tool.
    /// </summary>
    public class ReplayInputSchema : UnityCliLoopToolSchema
    {
        public ReplayInputAction Action { get; set; } = ReplayInputAction.Start;
        public string InputPath { get; set; } = "";
        public bool ShowOverlay { get; set; } = true;
        public bool Loop { get; set; } = false;
    }
}
