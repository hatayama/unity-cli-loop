#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports whether a mouse UI frame wait finished normally, was cut short by a
    /// Pause Point, or exceeded the wall-clock guard.
    /// </summary>
    internal enum MouseUiFrameWaitOutcome
    {
        Completed = 0,
        Paused = 1,
        TimedOut = 2
    }

    /// <summary>
    /// Waits for an Editor frame and restores execution to the Unity main thread.
    /// </summary>
    internal static class MouseUiEditorFrameWaiter
    {
        // EditorApplication.update keeps ticking while Play Mode is paused, so a frame wait
        // keyed only to update ticks would never report the pause. Duration-based callers
        // (MouseUiOverlayAnimator, the LongPress hold loop) measure elapsed time with
        // Time.realtimeSinceStartup, which freezes once EditorApplication.isPaused is true,
        // so without this check they would spin forever: every individual frame wait keeps
        // succeeding while the duration they are accumulating toward never advances.
        internal static async Task<MouseUiFrameWaitOutcome> WaitForEditorFrameAndSwitchToMainThreadAsync(CancellationToken ct)
        {
            if (EditorApplication.isPaused)
            {
                return MouseUiFrameWaitOutcome.Paused;
            }

            bool frameReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                1,
                UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                ct).ConfigureAwait(false);
            if (!frameReady)
            {
                return MouseUiFrameWaitOutcome.TimedOut;
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (EditorApplication.isPaused)
            {
                return MouseUiFrameWaitOutcome.Paused;
            }

            return MouseUiFrameWaitOutcome.Completed;
        }
    }
}
