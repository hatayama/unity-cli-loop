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
        /// <summary>An eligible Game camera rendered after wait started.</summary>
        Rendered,

        /// <summary>
        /// No eligible Game camera that can drive a display render; only editor ticks were waited.
        /// </summary>
        NoCamera,

        /// <summary>Editor ticks advanced but no eligible Game camera render was observed.</summary>
        NotRendered,

        /// <summary>Editor ticks themselves stalled (legacy timeout).</summary>
        TicksStalled,
    }

    /// <summary>
    /// Waits until an eligible Game camera actually renders, instead of counting editor update ticks.
    /// Why not Time.renderedFrameCount: when the Editor is unfocused it can advance with
    /// Time.frameCount even though the Play Mode view RT was not redrawn.
    /// </summary>
    internal static class PlayModeViewRenderWaiter
    {
        /// <summary>
        /// Waits for an eligible Game-camera render within the timeout, or reports why it did not.
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

            if (!HasEligibleGameCamera())
            {
                // Why tick-only: without an eligible Game camera a subscribed render will never arrive.
                bool ready = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, timeoutMilliseconds, ct)
                    .ConfigureAwait(false);
                return ready
                    ? PlayModeViewRenderWaitResult.NoCamera
                    : PlayModeViewRenderWaitResult.TicksStalled;
            }

            bool gameCameraRendered = false;

            void OnPostRender(Camera camera)
            {
                if (IsEligibleGameCamera(camera))
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
                    if (IsEligibleGameCamera(cameras[i]))
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

        /// <summary>
        /// True for a display-bound Game camera (not SceneView / Preview / offscreen RT cameras).
        /// Why exclude targetTexture: those cameras never refresh the Play Mode view RT, so counting
        /// them would falsely complete the wait while the screenshot RT stays stale/black.
        /// </summary>
        private static bool IsEligibleGameCamera(Camera camera)
        {
            return camera != null
                && camera.cameraType == CameraType.Game
                && camera.targetTexture == null;
        }

        /// <summary>
        /// True when at least one eligible Game camera exists in Camera.allCameras.
        /// Must use the same predicate as the render subscriptions.
        /// </summary>
        private static bool HasEligibleGameCamera()
        {
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (IsEligibleGameCamera(cameras[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
