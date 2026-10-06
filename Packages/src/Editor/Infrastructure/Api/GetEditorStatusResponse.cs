using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Editor status payload returned by the internal bridge command behind uloop status.
    /// The running-tool fields are null while no command holds the execution slot.
    /// </summary>
    public class GetEditorStatusResponse : UnityCliLoopToolResponse
    {
        public bool IsBusy { get; set; }
        public string RunningToolName { get; set; }
        public int? RunningToolElapsedSeconds { get; set; }
        public string RunningToolPhase { get; set; }
        public bool HasEditorState { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public bool IsCompiling { get; set; }
        public bool IsUpdating { get; set; }
        public double SecondsSinceLastMainThreadTick { get; set; }
    }
}
