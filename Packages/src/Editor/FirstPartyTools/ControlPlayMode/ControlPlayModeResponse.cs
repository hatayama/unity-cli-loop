using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Control Play Mode tool.
    /// </summary>
    public class ControlPlayModeResponse : UnityCliLoopToolResponse
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public bool Changed { get; set; }
        public bool WasAlreadyStopped { get; set; }
        public bool ResumedFromPause { get; set; }
        public bool BlockedByCompileErrors { get; set; }
        public bool BlockedByUnsavedChanges { get; set; }
        public int CompileErrorCount { get; set; }
        public ControlPlayModeCompileError[] CompileErrors { get; set; }
        public string Message { get; set; }
        public string Warning { get; set; } = string.Empty;

        public bool ShouldSerializeWarning()
        {
            return !string.IsNullOrEmpty(Warning);
        }

        /// <summary>
        /// Why Play Mode last stopped. Omitted when this Editor session has no confirmed stop.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string StoppedBy { get; set; }

        /// <summary>
        /// UTC ISO 8601 timestamp of the last Play Mode stop. Omitted with StoppedBy when none is recorded.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string StoppedAt { get; set; }
    }
}
