using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads live Unity Editor runtime state directly from EditorApplication.
    /// </summary>
    public sealed class EditorRuntimeStateService : IEditorRuntimeStatePort
    {
        public bool IsCompiling => EditorApplication.isCompiling;
        public bool IsUpdating => EditorApplication.isUpdating;
        public bool IsPlaying => EditorApplication.isPlaying;
        public bool IsPaused => EditorApplication.isPaused;
    }
}
