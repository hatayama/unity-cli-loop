#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of waiting for a Play Mode view Game-camera render to land.
    /// </summary>
    internal enum PlayModeViewRenderWaitResult
    {
        /// <summary>A Game camera rendered after wait started.</summary>
        Rendered,

        /// <summary>No cameras; only editor ticks were waited.</summary>
        NoCamera,

        /// <summary>Editor ticks advanced but no Game camera render was observed.</summary>
        NotRendered,

        /// <summary>Editor ticks themselves stalled (legacy timeout).</summary>
        TicksStalled,
    }

    /// <summary>
    /// Waits until a Game camera actually renders, instead of counting editor update ticks.
    /// Why not Time.renderedFrameCount: when the Editor is unfocused it can advance with
    /// Time.frameCount even though the Play Mode view RT was not redrawn.
    /// </summary>
    internal static class PlayModeViewRenderWaiter
    {
        /// <summary>
        /// Waits for a CameraType.Game render within the timeout, or reports why it did not.
        /// Must start on the editor main thread.
        /// </summary>
        internal static async Task<PlayModeViewRenderWaitResult> WaitForRenderedFrameAsync(
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            Debug.Assert(timeoutMilliseconds > 0, "timeoutMilliseconds must be positive");
            ct.ThrowIfCancellationRequested();

            SynchronizationContext editorContext =
                CapturedEditorSynchronizationContext.RequireCurrent("PlayModeViewRenderWaiter");

            if (Camera.allCamerasCount == 0)
            {
                // Why tick-only: with no cameras a Game render will never arrive.
                bool ready = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, timeoutMilliseconds, ct)
                    .ConfigureAwait(false);
                return ready
                    ? PlayModeViewRenderWaitResult.NoCamera
                    : PlayModeViewRenderWaitResult.TicksStalled;
            }

            bool gameCameraRendered = false;

            void OnPostRender(Camera camera)
            {
                if (camera != null && camera.cameraType == CameraType.Game)
                {
                    gameCameraRendered = true;
                }
            }

            void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
            {
                if (cameras == null)
                {
                    return;
                }

                for (int i = 0; i < cameras.Count; i++)
                {
                    Camera camera = cameras[i];
                    if (camera != null && camera.cameraType == CameraType.Game)
                    {
                        gameCameraRendered = true;
                        return;
                    }
                }
            }

            Camera.onPostRender += OnPostRender;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
            try
            {
                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    // Why SwitchTo: WaitFramesOrTimeoutAsync continuations leave the main thread,
                    // but RepaintMainPlayModeView / QueuePlayerLoopUpdate require it.
                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                    GameViewBridge.RepaintMainPlayModeView();
                    EditorApplication.QueuePlayerLoopUpdate();

                    long remainingLong = timeoutMilliseconds - stopwatch.ElapsedMilliseconds;
                    if (remainingLong <= 0)
                    {
                        break;
                    }

                    int remainingMilliseconds = remainingLong > int.MaxValue
                        ? int.MaxValue
                        : (int)remainingLong;
                    bool tick = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                        1,
                        remainingMilliseconds,
                        ct).ConfigureAwait(false);
                    if (!tick)
                    {
                        return PlayModeViewRenderWaitResult.TicksStalled;
                    }

                    if (gameCameraRendered)
                    {
                        return PlayModeViewRenderWaitResult.Rendered;
                    }
                }

                return PlayModeViewRenderWaitResult.NotRendered;
            }
            finally
            {
                Camera.onPostRender -= OnPostRender;
                RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            }
        }
    }
}
