using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures Unity Editor windows or GameView rendering for the bundled screenshot tool.
    /// </summary>
    public class ScreenshotUseCase
    {
        private const int ANNOTATION_OVERLAY_RENDER_WAIT_FRAMES = 2;

        public async Task<ScreenshotResponse> CaptureAsync(
            ScreenshotSchema request,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            VibeLogger.LogInfo(
                "screenshot_start",
                "Unity window screenshot started",
                new { WindowName = request.WindowName, ResolutionScale = request.ResolutionScale, MatchMode = request.MatchMode.ToString(), OutputDirectory = request.OutputDirectory },
                correlationId: correlationId,
                humanNote: "User requested Unity window screenshot",
                aiTodo: "Monitor capture performance and file size"
            );

            ScreenshotParameterValidator.Validate(request);

            if (request.CaptureMode == CaptureMode.rendering)
            {
                return await CaptureRenderingAsync(request, correlationId, ct).ConfigureAwait(false);
            }

            return await CaptureWindowsAsync(request, correlationId, ct).ConfigureAwait(false);
        }

        private async Task<ScreenshotResponse> CaptureRenderingAsync(
            ScreenshotSchema request, string correlationId, CancellationToken ct)
        {
            SynchronizationContext editorContext =
                CapturedEditorSynchronizationContext.RequireCurrent("rendering screenshot use case");

            if (!EditorApplication.isPlaying)
            {
                VibeLogger.LogError(
                    "screenshot_rendering_requires_playmode",
                    "CaptureMode.rendering requires PlayMode",
                    correlationId: correlationId
                );
                return new ScreenshotResponse
                {
                    Success = false,
                    Message = "Rendering screenshots require PlayMode, but Unity is currently in EditMode.",
                    NextActions = new[] { "Start PlayMode with `uloop control-play-mode --action Play`, then retry the rendering screenshot." }
                };
            }

            RenderingAnnotationCapture annotationCapture = new();
            if (request.AnnotateElements)
            {
                annotationCapture.AnnotatedElements = UIElementAnnotator.CollectInteractiveElements();
                UIElementAnnotator.AssignLabels(annotationCapture.AnnotatedElements);
            }

            if (request.AnnotateRaycastGrid)
            {
                ScreenshotResponse timedOutGrid = await ScreenshotRaycastGridCollector.CollectRaycastGridAnnotationsAsync(
                    request, annotationCapture, editorContext, correlationId, ct).ConfigureAwait(false);
                if (timedOutGrid != null)
                {
                    return timedOutGrid;
                }
            }

            if (request.ElementsOnly)
            {
                return await CaptureElementsOnlyRenderingAsync(
                    request, annotationCapture, editorContext, correlationId, ct).ConfigureAwait(false);
            }

            (Texture2D texture, GameRenderingImageInfo captureRenderingInfo, ScreenshotResponse overlayTimeout) =
                await CaptureTextureAfterHidingOverlaysAsync(
                    request, annotationCapture, editorContext, correlationId, ct).ConfigureAwait(false);
            if (overlayTimeout != null)
            {
                return overlayTimeout;
            }

            // Why switch after the helper: CaptureTextureAfterHidingOverlaysAsync ends on the
            // editor context, but ConfigureAwait(false) resumes this method on a thread-pool
            // thread. texture.width / EncodeToPNG / DestroyImmediate are main-thread only.
            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

            // Uses the settled capture-time size, not the pre-capture gameViewSize sample, so annotated
            // element coordinates stay consistent with the GameViewWidth/Height this response reports.
            UIElementAnnotator.ConvertToSimCoordinates(annotationCapture.AnnotatedElements, Mathf.RoundToInt(captureRenderingInfo.GameViewSize.y));
            List<UIElementInfo> responseAnnotatedElements =
                ScreenshotCaptureResults.CreateResponseAnnotatedElements(annotationCapture.AnnotatedElements, annotationCapture.PhysicsColliderElements);

            if (texture == null)
            {
                VibeLogger.LogError(
                    "screenshot_rendering_unavailable",
                    "Play Mode view RenderTexture is not available. Open the Game view or Device Simulator and wait for a frame before retrying.",
                    correlationId: correlationId
                );
                return new ScreenshotResponse
                {
                    Success = false,
                    Message = "PlayMode rendering did not produce an image.",
                    NextActions = new[] { "Open the Game view or Device Simulator, wait for a frame, then retry the rendering screenshot." }
                };
            }

            int width = texture.width;
            int height = texture.height;
            List<ScreenshotInfo> screenshots = new();

            try
            {
                string outputDirectory = ScreenshotFileWriter.EnsureOutputDirectoryExists(request.OutputDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string savedPath = Path.Combine(outputDirectory, $"Rendering_{timestamp}.png");

                ScreenshotFileWriter.SaveTextureAsPng(texture, savedPath);
                // why: only prune the package default Screenshots folder; never delete files in a user-specified OutputDirectory
                if (string.IsNullOrEmpty(request.OutputDirectory))
                {
                    OutputFileRetention.DeleteOldestBeyondLimit(outputDirectory, "*.png");
                }

                FileInfo savedFileInfo = new(savedPath);
                ScreenshotInfo info = new()
                {
                    ImagePath = savedPath,
                    FileSizeBytes = savedFileInfo.Length,
                    Width = width,
                    Height = height,
                    ResolutionScale = request.ResolutionScale,
                };
                ScreenshotCaptureResults.ApplyRenderingCoordinateMetadata(info, captureRenderingInfo.GameViewSize, captureRenderingInfo.ImageToInputOffsetY);
                info.AnnotatedElements = responseAnnotatedElements;
                info.RaycastLayerSummaries = annotationCapture.RaycastLayerSummaries;
                info.RaycastLayerNamesChecked = annotationCapture.RaycastLayerNamesChecked;
                screenshots.Add(info);
            }
            catch (Exception ex)
            {
                // File I/O is external resource access; catch to report save failure
                VibeLogger.LogWarning(
                    "screenshot_save_exception",
                    $"Exception saving rendering screenshot: {ex.Message}",
                    correlationId: correlationId
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            if (screenshots.Count > 0)
            {
                VibeLogger.LogInfo(
                    "screenshot_success",
                    $"Captured game rendering ({width}x{height})",
                    new { CaptureMode = "rendering", ScreenshotCount = screenshots.Count, AnnotatedElements = annotationCapture.AnnotatedElements.Count },
                    correlationId: correlationId
                );
            }

            return new ScreenshotResponse { Screenshots = screenshots };
        }

        internal sealed class RenderingAnnotationCapture
        {
            public List<UIElementInfo> AnnotatedElements = new();
            public List<UIElementInfo> PhysicsColliderElements = new();
            public List<RaycastLayerSummaryInfo> RaycastLayerSummaries = new();
            public List<string> RaycastLayerNamesChecked = new();
            public GameRenderingImageInfo? RaycastGridRenderingInfo;
        }

        private async Task<ScreenshotResponse> CaptureElementsOnlyRenderingAsync(
            ScreenshotSchema request,
            RenderingAnnotationCapture annotationCapture,
            SynchronizationContext editorContext,
            string correlationId,
            CancellationToken ct)
        {
            GameRenderingImageInfo elementsOnlyRenderingInfo;
            if (annotationCapture.RaycastGridRenderingInfo.HasValue)
            {
                elementsOnlyRenderingInfo = annotationCapture.RaycastGridRenderingInfo.Value;
            }
            else
            {
                GameRenderingImageInfo measuredRenderingInfo;
                bool elementsOnlyInfoTimedOut;
                (measuredRenderingInfo, elementsOnlyInfoTimedOut) =
                    await EditorWindowCaptureUtility.GetGameRenderingImageInfoAsync(
                        UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                        ct).ConfigureAwait(false);
                if (elementsOnlyInfoTimedOut)
                {
                    return ScreenshotCaptureResults.CreateTimedOutResult(
                        "elements-only rendering info capture",
                        correlationId,
                        new List<ScreenshotInfo>());
                }

                await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                elementsOnlyRenderingInfo = measuredRenderingInfo;
            }

            return ScreenshotCaptureResults.BuildElementsOnlyScreenshotInfo(
                annotationCapture.AnnotatedElements,
                annotationCapture.PhysicsColliderElements,
                annotationCapture.RaycastLayerSummaries,
                annotationCapture.RaycastLayerNamesChecked,
                request.ResolutionScale,
                elementsOnlyRenderingInfo);
        }

        private async Task<(Texture2D texture, GameRenderingImageInfo captureRenderingInfo, ScreenshotResponse timeout)>
            CaptureTextureAfterHidingOverlaysAsync(
                ScreenshotSchema request,
                RenderingAnnotationCapture annotationCapture,
                SynchronizationContext editorContext,
                string correlationId,
                CancellationToken ct)
        {
            GameObject annotationOverlay = null;
            Texture2D texture = null;
            GameRenderingImageInfo captureRenderingInfo = default;

            // Why SwitchTo before hide: SetActive/Canvas/RT clear are main-thread only. Keep this
            // await outside try — nothing is hidden yet if cancellation throws here.
            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            (GameObject inputVisualizationOverlay, bool inputVisualizationWasActive) =
                ScreenshotOverlayControl.HideInputVisualizationOverlay();

            try
            {
                ScreenshotResponse hideTimeout = await WaitAfterHidingInputVisualizationAsync(
                    inputVisualizationWasActive, editorContext, correlationId, ct).ConfigureAwait(false);
                if (hideTimeout != null)
                {
                    return (null, default, hideTimeout);
                }

                // Why switch after the helper: when the overlay was active the helper awaited,
                // so ConfigureAwait(false) resumes here off-main before CreateAnnotationOverlay
                // (new GameObject / AddComponent / Canvas.ForceUpdateCanvases).
                await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

                try
                {
                    // Assign before any await so the inner finally still destroys the overlay
                    // if WaitForRenderedFrameAsync throws after CreateAnnotationOverlay.
                    annotationOverlay = ScreenshotOverlayControl.CreateAnnotationOverlayIfNeeded(request, annotationCapture);
                    ScreenshotResponse overlayTimeout = await WaitAfterShowingAnnotationOverlayAsync(
                        annotationOverlay, editorContext, correlationId, ct).ConfigureAwait(false);
                    if (overlayTimeout != null)
                    {
                        return (null, default, overlayTimeout);
                    }

                    // Why switch after the helper: the original entered CaptureGameRenderingAsync
                    // on the main thread. ConfigureAwait(false) hops off-main after the overlay wait.
                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

                    bool captureTimedOut;
                    (texture, captureRenderingInfo, captureTimedOut) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
                        request.ResolutionScale,
                        annotationCapture.RaycastGridRenderingInfo,
                        UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                        ct).ConfigureAwait(false);
                    if (captureTimedOut)
                    {
                        return (null, default, ScreenshotCaptureResults.CreateTimedOutResult(
                            "Play Mode view rendering capture",
                            correlationId,
                            new List<ScreenshotInfo>()));
                    }

                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                }
                finally
                {
                    ScreenshotOverlayControl.DestroyAnnotationOverlay(annotationOverlay, editorContext);
                }
            }
            finally
            {
                ScreenshotOverlayControl.RestoreInputVisualizationOverlay(inputVisualizationOverlay, inputVisualizationWasActive, editorContext);
            }

            return (texture, captureRenderingInfo, null);
        }

        private async Task<ScreenshotResponse> WaitAfterHidingInputVisualizationAsync(
            bool inputVisualizationWasActive,
            SynchronizationContext editorContext,
            string correlationId,
            CancellationToken ct)
        {
            // Why wait inside try: WaitFramesOrTimeoutAsync / SwitchTo throw OperationCanceledException
            // on CLI disconnect; finally must still restore the overlay.
            if (!inputVisualizationWasActive)
            {
                return null;
            }

            PlayModeViewRenderWaitResult hideWaitResult =
                await PlayModeViewRenderWaiter.WaitForRenderedFrameAsync(
                    UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                    ct).ConfigureAwait(false);
            if (hideWaitResult == PlayModeViewRenderWaitResult.TicksStalled)
            {
                return ScreenshotCaptureResults.CreateTimedOutResult(
                    "input visualization overlay hide",
                    correlationId,
                    new List<ScreenshotInfo>());
            }

            if (hideWaitResult == PlayModeViewRenderWaitResult.NotRendered)
            {
                VibeLogger.LogWarning(
                    "screenshot_render_wait_not_confirmed",
                    "Timed out waiting for a Game camera render after hiding the input visualization overlay; continuing capture.",
                    correlationId: correlationId);
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            return null;
        }

        private async Task<ScreenshotResponse> WaitAfterShowingAnnotationOverlayAsync(
            GameObject annotationOverlay,
            SynchronizationContext editorContext,
            string correlationId,
            CancellationToken ct)
        {
            if (ReferenceEquals(annotationOverlay, null))
            {
                return null;
            }

            // Chained CLI calls can read the previous GameView RT before overlay rendering catches up.
            PlayModeViewRenderWaitResult overlayWaitResult =
                await PlayModeViewRenderWaiter.WaitForRenderedFrameAsync(
                    UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                    ct).ConfigureAwait(false);
            if (overlayWaitResult == PlayModeViewRenderWaitResult.TicksStalled)
            {
                return ScreenshotCaptureResults.CreateTimedOutResult(
                    "annotation overlay render",
                    correlationId,
                    new List<ScreenshotInfo>());
            }

            if (overlayWaitResult == PlayModeViewRenderWaitResult.NotRendered)
            {
                VibeLogger.LogWarning(
                    "screenshot_render_wait_not_confirmed",
                    "Timed out waiting for a Game camera render after showing the annotation overlay; continuing capture.",
                    correlationId: correlationId);
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            return null;
        }

        private async Task<ScreenshotResponse> CaptureWindowsAsync(
            ScreenshotSchema request, string correlationId, CancellationToken ct)
        {
            SynchronizationContext editorContext =
                CapturedEditorSynchronizationContext.RequireCurrent("window screenshot use case");
            EditorWindow[] windows = EditorWindowCaptureUtility.FindWindowsByName(request.WindowName, request.MatchMode);
            string captureWindowName = ScreenshotWindowNameResolver.ResolveCaptureWindowName(
                request.WindowName,
                request.MatchMode,
                windows.Length);
            bool usedSimulatorFallback = !string.Equals(
                captureWindowName,
                request.WindowName,
                StringComparison.OrdinalIgnoreCase);
            if (usedSimulatorFallback)
            {
                // why: Device Simulator replaces the Game tab, so the default "Game" title miss should retry Simulator
                windows = EditorWindowCaptureUtility.FindWindowsByName(captureWindowName, request.MatchMode);
                if (windows.Length > 0)
                {
                    VibeLogger.LogInfo(
                        "screenshot_window_fallback_simulator",
                        $"Window '{request.WindowName}' not found; capturing '{captureWindowName}' instead",
                        correlationId: correlationId
                    );
                }
            }

            if (windows.Length == 0)
            {
                string notFoundMessage = usedSimulatorFallback
                    ? "Neither Game nor Simulator window found; open the Game view or Device Simulator and retry"
                    : $"Window '{request.WindowName}' not found (MatchMode: {request.MatchMode})";

                VibeLogger.LogError(
                    "screenshot_window_not_found",
                    notFoundMessage,
                    correlationId: correlationId
                );
                return new ScreenshotResponse
                {
                    Success = false,
                    Message = notFoundMessage,
                    NextActions = new[] { "Open the requested Unity window, then retry the screenshot." }
                };
            }

            string outputDirectory = ScreenshotFileWriter.EnsureOutputDirectoryExists(request.OutputDirectory);
            string safeWindowName = ScreenshotCaptureResults.SanitizeFileName(captureWindowName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            List<ScreenshotInfo> screenshots = new();
            // Why before the loop: a Play Mode exit mid-capture must not skew the chrome Warning.
            bool isPlaying = EditorApplication.isPlaying;

            // Why SwitchTo before hide: SetActive/Canvas/RT clear are main-thread only. Keep this
            // await outside try — nothing is hidden yet if cancellation throws here.
            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            (GameObject inputVisualizationOverlay, bool inputVisualizationWasActive) =
                ScreenshotOverlayControl.HideInputVisualizationOverlay();

            try
            {
                // Why wait inside try: WaitFramesOrTimeoutAsync / SwitchTo throw OperationCanceledException
                // on CLI disconnect; finally must still restore the overlay.
                if (inputVisualizationWasActive)
                {
                    bool hideFramesReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                        ANNOTATION_OVERLAY_RENDER_WAIT_FRAMES,
                        UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                        ct).ConfigureAwait(false);
                    if (!hideFramesReady)
                    {
                        ScreenshotResponse hideTimedOut = ScreenshotCaptureResults.CreateTimedOutResult(
                            "input visualization overlay hide",
                            correlationId,
                            screenshots);
                        hideTimedOut.Warning = ScreenshotPlayModeWindowWarningBuilder.Build(
                            request.CaptureMode, isPlaying, hideTimedOut.Screenshots.Count);
                        return hideTimedOut;
                    }

                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                }

                for (int i = 0; i < windows.Length; i++)
                {
                    EditorWindow window = windows[i];
                    (Texture2D texture, bool timedOut) = await EditorWindowCaptureUtility.CaptureWindowAsync(
                        window,
                        request.ResolutionScale,
                        UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                        ct).ConfigureAwait(false);
                    if (timedOut)
                    {
                        ScreenshotResponse captureTimedOut = ScreenshotCaptureResults.CreateTimedOutResult(
                            "EditorWindow capture",
                            correlationId,
                            screenshots);
                        captureTimedOut.Warning = ScreenshotPlayModeWindowWarningBuilder.Build(
                            request.CaptureMode, isPlaying, captureTimedOut.Screenshots.Count);
                        return captureTimedOut;
                    }

                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                    if (texture == null)
                    {
                        VibeLogger.LogWarning(
                            "screenshot_failed",
                            $"Failed to capture window index {i}",
                            correlationId: correlationId
                        );
                        continue;
                    }

                    string fileName = windows.Length == 1
                        ? $"{safeWindowName}_{timestamp}.png"
                        : $"{safeWindowName}_{i + 1}_{timestamp}.png";
                    string savedPath = Path.Combine(outputDirectory, fileName);

                    int width = texture.width;
                    int height = texture.height;

                    try
                    {
                        ScreenshotFileWriter.SaveTextureAsPng(texture, savedPath);

                        FileInfo savedFileInfo = new(savedPath);
                        ScreenshotInfo info = new()
                        {
                            ImagePath = savedPath,
                            FileSizeBytes = savedFileInfo.Length,
                            Width = width,
                            Height = height,
                        };
                        ScreenshotCaptureResults.ApplyWindowCoordinateMetadata(info);
                        screenshots.Add(info);
                    }
                    catch (Exception ex)
                    {
                        // File I/O is external resource access; catch to continue processing remaining windows
                        VibeLogger.LogWarning(
                            "screenshot_save_exception",
                            $"Exception saving window index {i}: {ex.Message}",
                            correlationId: correlationId
                        );
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
            finally
            {
                ScreenshotOverlayControl.RestoreInputVisualizationOverlay(inputVisualizationOverlay, inputVisualizationWasActive, editorContext);
            }

            // why: only prune the package default Screenshots folder; never delete files in a user-specified OutputDirectory
            if (string.IsNullOrEmpty(request.OutputDirectory))
            {
                OutputFileRetention.DeleteOldestBeyondLimit(outputDirectory, "*.png");
            }

            VibeLogger.LogInfo(
                "screenshot_success",
                $"Captured {screenshots.Count} window(s)",
                new { WindowName = captureWindowName, RequestedWindowName = request.WindowName, ScreenshotCount = screenshots.Count },
                correlationId: correlationId
            );

            return new ScreenshotResponse
            {
                Screenshots = screenshots,
                Warning = ScreenshotPlayModeWindowWarningBuilder.Build(
                    request.CaptureMode, isPlaying, screenshots.Count)
            };
        }
    }
}
