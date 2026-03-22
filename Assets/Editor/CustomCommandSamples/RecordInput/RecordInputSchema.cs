using System.ComponentModel;

namespace io.github.hatayama.uLoopMCP
{
    public class RecordInputSchema : BaseToolSchema
    {
        [Description("Recording action: Start(0) - begin recording input, Stop(1) - stop recording and save to file")]
        public RecordInputAction Action { get; set; } = RecordInputAction.Start;

        [Description("Output file path for the recording JSON. If empty, auto-generates under .uloop/outputs/InputRecordings/")]
        public string OutputPath { get; set; } = "";

        [Description("Comma-separated key filter. Only record specified keys (e.g. 'W,A,S,D,Space'). Empty means record all common game keys.")]
        public string Keys { get; set; } = "";

        [Description("Countdown delay in seconds before recording starts (0-10). Gives time to switch focus to Game View.")]
        public int DelaySeconds { get; set; } = 3;

        [Description("Show recording overlay (countdown + REC indicator)")]
        public bool ShowOverlay { get; set; } = true;
    }
}
