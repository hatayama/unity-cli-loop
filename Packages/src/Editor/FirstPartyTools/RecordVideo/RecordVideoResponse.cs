using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the record-video tool.
    /// </summary>
    public sealed class RecordVideoResponse : UnityCliLoopToolResponse
    {
        public string Message { get; set; } = "";

        public string Action { get; set; } = "";

        public bool IsRecording { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string OutputPath { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public int FrameRate { get; set; }

        public int EncodedFrameCount { get; set; }

        public int SkippedFrameCount { get; set; }

        public double ElapsedSeconds { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string StoppedBy { get; set; }
    }
}
