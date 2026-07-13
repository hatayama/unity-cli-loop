#if UNITY_EDITOR
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Clears raw capture references when Unity leaves the paused-hit window.
    /// </summary>
    [InitializeOnLoad]
    internal static class UloopPausePointRawCaptureLifecycle
    {
        static UloopPausePointRawCaptureLifecycle()
        {
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            if (!EditorApplication.isPaused)
            {
                return;
            }

            // Why while paused only: abandoned Hit windows must expire without a CLI poll.
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();
        }

        private static void OnPauseStateChanged(PauseState pauseState)
        {
            if (pauseState != PauseState.Unpaused)
            {
                return;
            }

            // Why: Step can transiently unpause then re-pause; delayCall re-checks the settled state.
            EditorApplication.delayCall += ClearRawCaptureIfStillUnpaused;
        }

        private static void ClearRawCaptureIfStillUnpaused()
        {
            if (EditorApplication.isPaused)
            {
                return;
            }

            UloopPausePointRawCaptureHolder.Clear();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                UloopPausePointRawCaptureHolder.Clear();
            }
        }
    }
}
#endif
