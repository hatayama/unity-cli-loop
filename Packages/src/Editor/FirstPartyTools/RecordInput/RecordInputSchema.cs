
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Record Input tool.
    /// </summary>
    public class RecordInputSchema : UnityCliLoopToolSchema
    {
        public RecordInputAction Action { get; set; } = RecordInputAction.Start;
        public string OutputPath { get; set; } = "";
        public string Keys { get; set; } = "";
        public int DelaySeconds { get; set; } = 3;
        public bool ShowOverlay { get; set; } = true;
    }
}
