#if UNITY_EDITOR
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Adapts pause point hits to UnityEditor.EditorApplication state.
    /// </summary>
    internal sealed class UnityEditorPausePointPauseController : IUloopPausePointPauseController
    {
        public bool IsPlaying => EditorApplication.isPlaying;
        public bool IsPaused => EditorApplication.isPaused;

        public void Pause()
        {
            EditorApplication.isPaused = true;
        }

        public void Resume()
        {
            // Why unconditional: Option B clears any Editor pause on disconnect/clear/expiry,
            // including a manual pause that happens to be active.
            EditorApplication.isPaused = false;
        }
    }
}
#endif
