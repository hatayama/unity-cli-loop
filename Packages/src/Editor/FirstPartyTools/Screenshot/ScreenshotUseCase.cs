using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
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
                ScreenshotResponse timedOutGrid = await CollectRaycastGridAnnotationsAsync(
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

            // Uses the settled capture-time size, not the pre-capture gameViewSize sample, so annotated
            // element coordinates stay consistent with the GameViewWidth/Height this response reports.
            UIElementAnnotator.ConvertToSimCoordinates(annotationCapture.AnnotatedElements, Mathf.RoundToInt(captureRenderingInfo.GameViewSize.y));
            List<UIElementInfo> responseAnnotatedElements =
                CreateResponseAnnotatedElements(annotationCapture.AnnotatedElements, annotationCapture.PhysicsColliderElements);

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
                ApplyRenderingCoordinateMetadata(info, captureRenderingInfo.GameViewSize, captureRenderingInfo.ImageToInputOffsetY);
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

        private sealed class RenderingAnnotationCapture
        {
            public List<UIElementInfo> AnnotatedElements = new();
            public List<UIElementInfo> PhysicsColliderElements = new();
            public List<RaycastLayerSummaryInfo> RaycastLayerSummaries = new();
            public List<string> RaycastLayerNamesChecked = new();
            public GameRenderingImageInfo? RaycastGridRenderingInfo;
        }

        private async Task<ScreenshotResponse> CollectRaycastGridAnnotationsAsync(
            ScreenshotSchema request,
            RenderingAnnotationCapture annotationCapture,
            SynchronizationContext editorContext,
            string correlationId,
            CancellationToken ct)
        {
            GameRenderingImageInfo renderingImageInfo;
            bool gridInfoTimedOut;
            (renderingImageInfo, gridInfoTimedOut) = await EditorWindowCaptureUtility.GetGameRenderingImageInfoAsync(
                UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                ct).ConfigureAwait(false);
            if (gridInfoTimedOut)
            {
                return CreateTimedOutResult(
                    "raycast grid rendering info capture",
                    correlationId,
                    new List<ScreenshotInfo>());
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);

            annotationCapture.RaycastGridRenderingInfo = renderingImageInfo;
            RaycastLayerMaskResolution raycastLayerMaskResolution = ScreenshotParameterValidator.ResolveRaycastLayerMask(request);
            List<RaycastLayerDefinition> availableLayerDefinitions = ScreenshotParameterValidator.GetAvailableLayerDefinitions();
            int effectiveLayerMask = raycastLayerMaskResolution.HasLayerNames
                ? raycastLayerMaskResolution.Mask
                : Physics.DefaultRaycastLayers;

            annotationCapture.PhysicsColliderElements = RaycastGridAnnotator.CollectPhysicsColliderElements(
                renderingImageInfo.RenderingImageSize,
                renderingImageInfo.ImageToInputOffsetY,
                effectiveLayerMask);
            annotationCapture.RaycastLayerSummaries = RaycastGridAnnotator.CollectRaycastLayerSummaries(
                renderingImageInfo.RenderingImageSize,
                renderingImageInfo.ImageToInputOffsetY);

            Camera mainCamera = Camera.main;
            int checkedLayerMask = mainCamera != null ? effectiveLayerMask & mainCamera.cullingMask : 0;
            annotationCapture.RaycastLayerNamesChecked = RaycastLayerMaskResolver.CreateLayerNamesFromMask(
                checkedLayerMask,
                availableLayerDefinitions);
            return null;
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
                    return CreateTimedOutResult(
                        "elements-only rendering info capture",
                        correlationId,
                        new List<ScreenshotInfo>());
                }

                await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                elementsOnlyRenderingInfo = measuredRenderingInfo;
            }

            return BuildElementsOnlyScreenshotInfo(
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
                HideInputVisualizationOverlay();

            try
            {
                ScreenshotResponse hideTimeout = await WaitAfterHidingInputVisualizationAsync(
                    inputVisualizationWasActive, editorContext, correlationId, ct).ConfigureAwait(false);
                if (hideTimeout != null)
                {
                    return (null, default, hideTimeout);
                }

                try
                {
                    ScreenshotResponse overlayTimeout;
                    (annotationOverlay, overlayTimeout) = await ShowAnnotationOverlayOrTimeoutAsync(
                        request, annotationCapture, editorContext, correlationId, ct).ConfigureAwait(false);
                    if (overlayTimeout != null)
                    {
                        return (null, default, overlayTimeout);
                    }

                    bool captureTimedOut;
                    (texture, captureRenderingInfo, captureTimedOut) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
                        request.ResolutionScale,
                        annotationCapture.RaycastGridRenderingInfo,
                        UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                        ct).ConfigureAwait(false);
                    if (captureTimedOut)
                    {
                        return (null, default, CreateTimedOutResult(
                            "Play Mode view rendering capture",
                            correlationId,
                            new List<ScreenshotInfo>()));
                    }

                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                }
                finally
                {
                    DestroyAnnotationOverlay(annotationOverlay, editorContext);
                }
            }
            finally
            {
                RestoreInputVisualizationOverlay(inputVisualizationOverlay, inputVisualizationWasActive, editorContext);
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
                return CreateTimedOutResult(
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

        private async Task<(GameObject overlay, ScreenshotResponse timeout)> ShowAnnotationOverlayOrTimeoutAsync(
            ScreenshotSchema request,
            RenderingAnnotationCapture annotationCapture,
            SynchronizationContext editorContext,
            string correlationId,
            CancellationToken ct)
        {
            if (!request.AnnotateElements && !request.AnnotateRaycastGrid)
            {
                return (null, null);
            }

            List<UIElementInfo> overlayElements = new(annotationCapture.AnnotatedElements);
            overlayElements.AddRange(annotationCapture.PhysicsColliderElements);
            GameObject annotationOverlay = UIElementAnnotator.CreateAnnotationOverlay(
                overlayElements,
                request.ResolutionScale);
            Canvas.ForceUpdateCanvases();
            // Chained CLI calls can read the previous GameView RT before overlay rendering catches up.
            PlayModeViewRenderWaitResult overlayWaitResult =
                await PlayModeViewRenderWaiter.WaitForRenderedFrameAsync(
                    UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                    ct).ConfigureAwait(false);
            if (overlayWaitResult == PlayModeViewRenderWaitResult.TicksStalled)
            {
                return (annotationOverlay, CreateTimedOutResult(
                    "annotation overlay render",
                    correlationId,
                    new List<ScreenshotInfo>()));
            }

            if (overlayWaitResult == PlayModeViewRenderWaitResult.NotRendered)
            {
                VibeLogger.LogWarning(
                    "screenshot_render_wait_not_confirmed",
                    "Timed out waiting for a Game camera render after showing the annotation overlay; continuing capture.",
                    correlationId: correlationId);
            }

            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            return (annotationOverlay, null);
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
            string safeWindowName = SanitizeFileName(captureWindowName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            List<ScreenshotInfo> screenshots = new();

            // Why SwitchTo before hide: SetActive/Canvas/RT clear are main-thread only. Keep this
            // await outside try — nothing is hidden yet if cancellation throws here.
            await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            (GameObject inputVisualizationOverlay, bool inputVisualizationWasActive) =
                HideInputVisualizationOverlay();

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
                        return CreateTimedOutResult(
                            "input visualization overlay hide",
                            correlationId,
                            screenshots);
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
                        return CreateTimedOutResult("EditorWindow capture", correlationId, screenshots);
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
                        ApplyWindowCoordinateMetadata(info);
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
                RestoreInputVisualizationOverlay(inputVisualizationOverlay, inputVisualizationWasActive, editorContext);
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

            return new ScreenshotResponse { Screenshots = screenshots };
        }

        // Hides the input-visualization canvas synchronously. Caller must already be on the editor
        // main thread, and must run the 2-frame settle wait inside a try/finally that restores.
        private static (GameObject Overlay, bool WasActive) HideInputVisualizationOverlay()
        {
            GameObject overlay = OverlayCanvasFactory.TryGetExisting();
            bool wasActive = overlay != null && overlay.activeSelf;
            if (!wasActive)
            {
                return (overlay, false);
            }

            overlay.SetActive(false);
            // Also disable Canvas: Screen Space Overlay can keep compositing into the Play Mode
            // view RT for a frame after the GameObject alone is deactivated.
            Canvas overlayCanvas = overlay.GetComponent<Canvas>();
            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = false;
            }

            Canvas.ForceUpdateCanvases();
            // Why clear only without eligible Game cameras: an eligible camera will overwrite the
            // Play Mode RT on the next redraw, so clearing to black would destroy a valid frame.
            // With no eligible camera (including offscreen-only setups), hide the overlay's leftover
            // badge composite by clearing — same predicate as PlayModeViewRenderWaiter.
            if (!PlayModeViewRenderWaiter.HasEligibleGameCamera())
            {
                GameViewBridge.ClearMainPlayModeViewRenderTexture();
            }
            GameViewBridge.RepaintMainPlayModeView();
            return (overlay, true);
        }

        private static void RestoreInputVisualizationOverlay(
            GameObject overlay,
            bool wasActive,
            SynchronizationContext editorContext)
        {
            if (!wasActive || overlay == null)
            {
                return;
            }

            if (SynchronizationContext.Current == editorContext)
            {
                RestoreInputVisualizationOverlayOnMainThread(overlay);
                return;
            }

            // Why post: timeout/error paths may restore from a non-main thread.
            editorContext.Post(_ => RestoreInputVisualizationOverlayOnMainThread(overlay), null);
        }

        private static void RestoreInputVisualizationOverlayOnMainThread(GameObject overlay)
        {
            // Why: Post may run after Play Mode teardown destroyed the DontDestroyOnLoad overlay.
            if (overlay == null)
            {
                return;
            }

            Canvas overlayCanvas = overlay.GetComponent<Canvas>();
            if (overlayCanvas != null)
            {
                overlayCanvas.enabled = true;
            }

            overlay.SetActive(true);
        }

        internal static ScreenshotResponse CreateTimedOutResult(
            string waitName,
            string correlationId,
            List<ScreenshotInfo> screenshots)
        {
            string message =
                $"Timed out after {UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS}ms while waiting for {waitName} frames.";
            VibeLogger.LogWarning(
                "screenshot_timeout",
                message,
                new { WaitName = waitName, TimeoutMilliseconds = UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS },
                correlationId: correlationId
            );

            return new ScreenshotResponse
            {
                Success = false,
                TimedOut = true,
                Message = message,
                NextActions = new[] { "Retry the screenshot after Unity finishes rendering the requested frame." },
                Screenshots = screenshots,
            };
        }

        private static void DestroyAnnotationOverlay(
            GameObject annotationOverlay,
            SynchronizationContext editorContext)
        {
            if (ReferenceEquals(annotationOverlay, null))
            {
                return;
            }

            if (SynchronizationContext.Current == editorContext)
            {
                UIElementAnnotator.DestroyAnnotationOverlay(annotationOverlay);
                return;
            }

            // Why: timeout results may complete from the timer thread while Unity's main thread is stalled.
            editorContext.Post(_ => UIElementAnnotator.DestroyAnnotationOverlay(annotationOverlay), null);
        }

        internal static void ApplyRenderingCoordinateMetadata(
            ScreenshotInfo info,
            Vector2 gameViewSize,
            int imageToInputOffsetY = 0)
        {
            ScreenshotCoordinateMetadata.ApplyRendering(info, gameViewSize, imageToInputOffsetY);
        }

        internal static void ApplyWindowCoordinateMetadata(ScreenshotInfo info)
        {
            ScreenshotCoordinateMetadata.ApplyWindow(info);
        }

        internal static List<UIElementInfo> CreateResponseAnnotatedElements(
            List<UIElementInfo> uiElements,
            List<UIElementInfo> physicsColliderElements)
        {
            return ScreenshotResponseFactory.CreateResponseAnnotatedElements(uiElements, physicsColliderElements);
        }

        // why: ElementsOnly must use the capture-time measured GameViewSize, not the flow-start sample,
        // so SimY and GameViewHeight stay consistent with the full rendering capture path.
        internal static ScreenshotResponse BuildElementsOnlyScreenshotInfo(
            List<UIElementInfo> annotatedElements,
            List<UIElementInfo> physicsColliderElements,
            List<RaycastLayerSummaryInfo> raycastLayerSummaries,
            List<string> raycastLayerNamesChecked,
            float resolutionScale,
            GameRenderingImageInfo renderingInfo)
        {
            UIElementAnnotator.ConvertToSimCoordinates(
                annotatedElements,
                Mathf.RoundToInt(renderingInfo.GameViewSize.y));
            List<UIElementInfo> elementsOnlyAnnotatedElements =
                CreateResponseAnnotatedElements(annotatedElements, physicsColliderElements);
            ScreenshotInfo elementsOnlyInfo = new() { ResolutionScale = resolutionScale };
            ApplyRenderingCoordinateMetadata(
                elementsOnlyInfo,
                renderingInfo.GameViewSize,
                renderingInfo.ImageToInputOffsetY);
            elementsOnlyInfo.AnnotatedElements = elementsOnlyAnnotatedElements;
            elementsOnlyInfo.RaycastLayerSummaries = raycastLayerSummaries;
            elementsOnlyInfo.RaycastLayerNamesChecked = raycastLayerNamesChecked;
            return new ScreenshotResponse
            {
                Screenshots = new List<ScreenshotInfo> { elementsOnlyInfo }
            };
        }

        internal static string CreateInvalidRaycastLayerMaskMessage(
            RaycastLayerMaskResolution raycastLayerMaskResolution)
        {
            return ScreenshotParameterValidator.CreateInvalidRaycastLayerMaskMessage(raycastLayerMaskResolution);
        }

        internal static string SanitizeFileName(string name)
        {
            return ScreenshotFileWriter.SanitizeFileName(name);
        }
    }
}
