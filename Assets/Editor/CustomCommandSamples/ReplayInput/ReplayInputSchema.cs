using System.ComponentModel;

namespace io.github.hatayama.uLoopMCP
{
    public class ReplayInputSchema : BaseToolSchema
    {
        [Description("Replay action: Start(0) - begin replaying, Stop(1) - stop mid-way, Status(2) - check progress")]
        public ReplayInputAction Action { get; set; } = ReplayInputAction.Start;

        [Description("Path to recording JSON file. If empty, auto-detects the latest recording in .uloop/outputs/InputRecordings/")]
        public string InputPath { get; set; } = "";

        [Description("Show visualization overlay during replay")]
        public bool ShowOverlay { get; set; } = true;

        [Description("Loop replay continuously")]
        public bool Loop { get; set; } = false;
    }
}
