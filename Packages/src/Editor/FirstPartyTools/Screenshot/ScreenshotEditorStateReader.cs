using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads Editor Play Mode for screenshot capture-mode auto resolution.
    /// </summary>
    internal interface IScreenshotEditorStateReader
    {
        bool IsPlaying { get; }
    }

    /// <summary>
    /// Forwards Play Mode reads to EditorApplication.
    /// </summary>
    internal sealed class ScreenshotEditorStateReader : IScreenshotEditorStateReader
    {
        public bool IsPlaying => EditorApplication.isPlaying;
    }
}
