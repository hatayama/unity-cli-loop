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
        public int CompileErrorCount { get; set; }
        public ControlPlayModeCompileError[] CompileErrors { get; set; }
        public string Message { get; set; }
        public string Warning { get; set; } = string.Empty;
    }
}
