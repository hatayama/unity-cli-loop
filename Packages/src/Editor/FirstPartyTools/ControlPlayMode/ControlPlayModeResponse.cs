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
        public string Message { get; set; }
    }
}

