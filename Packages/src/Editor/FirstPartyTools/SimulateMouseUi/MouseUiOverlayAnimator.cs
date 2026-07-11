#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Animates the mouse UI overlay around simulated pointer actions.
    /// </summary>
    internal static class MouseUiOverlayAnimator
    {
        internal static async Task<MouseUiFrameWaitOutcome> PlayExpandAnimation(CancellationToken ct)
        {
            SimulateMouseUiOverlay overlay = OverlayCanvasFactory.VisualizationCanvas.MouseUiOverlay;

            // Previous dissipate sets alpha to 0; restore before expand starts
            overlay.SetAlpha(1f);

            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            while (elapsed < SimulateMouseUiAnimationConstants.EXPAND_DURATION)
            {
                float t = elapsed / SimulateMouseUiAnimationConstants.EXPAND_DURATION;
                overlay.SetCursorScale(Mathf.Lerp(SimulateMouseUiAnimationConstants.EXPAND_START_SCALE, 1f, t));
                MouseUiFrameWaitOutcome frameOutcome = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (frameOutcome != MouseUiFrameWaitOutcome.Completed)
                {
                    return frameOutcome;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
                elapsed = Time.realtimeSinceStartup - startTime;
            }
            overlay.SetCursorScale(1f);
            return MouseUiFrameWaitOutcome.Completed;
        }

        internal static async Task<MouseUiFrameWaitOutcome> PlayDissipateAnimation(CancellationToken ct)
        {
            SimulateMouseUiOverlay overlay = OverlayCanvasFactory.VisualizationCanvas.MouseUiOverlay;

            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            while (elapsed < SimulateMouseUiAnimationConstants.DISSIPATE_DURATION)
            {
                float t = elapsed / SimulateMouseUiAnimationConstants.DISSIPATE_DURATION;
                overlay.SetCursorScale(Mathf.Lerp(1f, 0f, t));
                overlay.SetAlpha(Mathf.Lerp(1f, 0f, t));
                MouseUiFrameWaitOutcome frameOutcome = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (frameOutcome != MouseUiFrameWaitOutcome.Completed)
                {
                    return frameOutcome;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
                elapsed = Time.realtimeSinceStartup - startTime;
            }
            overlay!.SetCursorScale(0f);
            overlay!.SetAlpha(0f);
            SimulateMouseUiOverlayState.Clear();
            return MouseUiFrameWaitOutcome.Completed;
        }
    }
}
