using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Editor status payload returned by the internal bridge command behind uloop status.
    /// The running-tool fields are null while no command holds the execution slot.
    /// OpenDialogs lists the modal dialogs Unity reported as open; only Unity 6000.7 and later report
    /// them, so it is always empty in older Editors.
    /// ProcessId is the Editor's own process ID, so the CLI can read that process's windows for a
    /// dialog without searching the process list.
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
        public EditorDialogInfo[] OpenDialogs { get; set; }
        public int ProcessId { get; set; }
    }
}
