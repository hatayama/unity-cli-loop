using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// PlayMode pause state returned by the internal CLI polling bridge command.
    /// </summary>
    public class GetPlayModeStateResponse : UnityCliLoopToolResponse
    {
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public string Message { get; set; }
    }
}
