#nullable enable
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Waits for an Editor frame and restores execution to the Unity main thread.
    /// </summary>
    internal static class MouseUiEditorFrameWaiter
    {
        internal static async Task<bool> WaitForEditorFrameAndSwitchToMainThreadAsync(CancellationToken ct)
        {
            bool frameReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                1,
                UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                ct).ConfigureAwait(false);
            if (!frameReady)
            {
                return false;
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            return true;
        }
    }
}
