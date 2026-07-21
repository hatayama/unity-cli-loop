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
            // This is the low-level unpause primitive and is intentionally unconditional; it does
            // not know why the Editor is paused. The registry decides when to call it: clear only
            // resumes a pause-point-owned pause, while disconnect and expiry resume unconditionally.
            EditorApplication.isPaused = false;
        }
    }
}
#endif
