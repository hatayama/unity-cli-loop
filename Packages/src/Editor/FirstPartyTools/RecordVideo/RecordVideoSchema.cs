using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the record-video tool.
    /// </summary>
    public sealed class RecordVideoSchema : UnityCliLoopToolSchema
    {
        public RecordVideoAction Action { get; set; } = RecordVideoAction.Start;

        public int FrameRate { get; set; } = 30;

        public int MaxDurationSeconds { get; set; } = 60;

        public string OutputPath { get; set; } = "";
    }
}
