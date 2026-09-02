using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads Editor Play Mode state for capture-mode auto resolution and the paused-capture Warning.
    /// </summary>
    internal interface IScreenshotEditorStateReader
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
    }

    /// <summary>
    /// Forwards Play Mode reads to EditorApplication.
    /// </summary>
    internal sealed class ScreenshotEditorStateReader : IScreenshotEditorStateReader
    {
        public bool IsPlaying => EditorApplication.isPlaying;
        public bool IsPaused => EditorApplication.isPaused;
    }
}
