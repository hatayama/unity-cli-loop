using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads Editor Play/Pause flags for execute-dynamic-code pause-state application.
    /// </summary>
    internal interface IDynamicCodeEditorStateReader
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
    }

    /// <summary>
    /// Forwards Play/Pause reads to EditorApplication.
    /// </summary>
    internal sealed class DynamicCodeEditorStateReader : IDynamicCodeEditorStateReader
    {
        public bool IsPlaying => EditorApplication.isPlaying;

        public bool IsPaused => EditorApplication.isPaused;
    }
}
