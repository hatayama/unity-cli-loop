#nullable enable

namespace io.github.hatayama.uLoopMCP
{
    public class RecordInputResponse : BaseToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? OutputPath { get; set; }
        public int? TotalFrames { get; set; }
        public float? DurationSeconds { get; set; }
    }
}
