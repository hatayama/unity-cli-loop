using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads and writes Unity's Play Mode state for the Control Play Mode use case.
    /// </summary>
    public interface IControlPlayModeEditorStateService
    {
        bool IsPlaying { get; set; }
        bool IsPaused { get; set; }
        void Step();
    }

    /// <summary>
    /// Forwards Play Mode state reads/writes to the real EditorApplication API.
    /// </summary>
    internal sealed class ControlPlayModeEditorStateService : IControlPlayModeEditorStateService
    {
        public bool IsPlaying
        {
            get => EditorApplication.isPlaying;
            set => EditorApplication.isPlaying = value;
        }

        public bool IsPaused
        {
            get => EditorApplication.isPaused;
            set => EditorApplication.isPaused = value;
        }

        public void Step()
        {
            EditorApplication.Step();
        }
    }
}
