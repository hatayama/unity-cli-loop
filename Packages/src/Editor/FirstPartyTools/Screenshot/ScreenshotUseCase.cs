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

            List<UIElementInfo> annotatedElements = new();
            List<UIElementInfo> physicsColliderElements = new();
            Vector2 gameViewSize = GameViewCoordinateUtility.GetMainGameViewSize();
            List<RaycastLayerSummaryInfo> raycastLayerSummaries = new();
            List<string> raycastLayerNamesChecked = new();
            GameRenderingImageInfo? raycastGridRenderingInfo = null;

            if (request.AnnotateElements)
            {
                annotatedElements = UIElementAnnotator.CollectInteractiveElements();
                UIElementAnnotator.AssignLabels(annotatedElements);
            }

            if (request.AnnotateRaycastGrid)
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

                raycastGridRenderingInfo = renderingImageInfo;
                gameViewSize = renderingImageInfo.GameViewSize;
                RaycastLayerMaskResolution raycastLayerMaskResolution = ScreenshotParameterValidator.ResolveRaycastLayerMask(request);
                List<RaycastLayerDefinition> availableLayerDefinitions = ScreenshotParameterValidator.GetAvailableLayerDefinitions();
                int effectiveLayerMask = raycastLayerMaskResolution.HasLayerNames
                    ? raycastLayerMaskResolution.Mask
                    : Physics.DefaultRaycastLayers;

                physicsColliderElements = RaycastGridAnnotator.CollectPhysicsColliderElements(
                    renderingImageInfo.RenderingImageSize,
                    renderingImageInfo.ImageToInputOffsetY,
                    effectiveLayerMask);
                raycastLayerSummaries = RaycastGridAnnotator.CollectRaycastLayerSummaries(
                    renderingImageInfo.RenderingImageSize,
                    renderingImageInfo.ImageToInputOffsetY);

                Camera mainCamera = Camera.main;
                int checkedLayerMask = mainCamera != null ? effectiveLayerMask & mainCamera.cullingMask : 0;
                raycastLayerNamesChecked = RaycastLayerMaskResolver.CreateLayerNamesFromMask(
                    checkedLayerMask,
                    availableLayerDefinitions);
            }

            if (request.ElementsOnly)
            {
                UIElementAnnotator.ConvertToSimCoordinates(annotatedElements, Mathf.RoundToInt(gameViewSize.y));
                List<UIElementInfo> elementsOnlyAnnotatedElements =
                    CreateResponseAnnotatedElements(annotatedElements, physicsColliderElements);
                ScreenshotInfo elementsOnlyInfo = new() { ResolutionScale = request.ResolutionScale };
                int elementsOnlyImageToInputOffsetY = raycastGridRenderingInfo?.ImageToInputOffsetY ?? 0;
                ApplyRenderingCoordinateMetadata(elementsOnlyInfo, gameViewSize, elementsOnlyImageToInputOffsetY);
                elementsOnlyInfo.AnnotatedElements = elementsOnlyAnnotatedElements;
                elementsOnlyInfo.RaycastLayerSummaries = raycastLayerSummaries;
                elementsOnlyInfo.RaycastLayerNamesChecked = raycastLayerNamesChecked;
                return new ScreenshotResponse
                {
                    Screenshots = new List<ScreenshotInfo> { elementsOnlyInfo }
                };
            }

            GameObject annotationOverlay = null;
            Texture2D texture;
            GameRenderingImageInfo captureRenderingInfo;
            bool captureTimedOut;
            try
            {
                if (request.AnnotateElements || request.AnnotateRaycastGrid)
                {
                    List<UIElementInfo> overlayElements = new(annotatedElements);
                    overlayElements.AddRange(physicsColliderElements);
                    annotationOverlay = UIElementAnnotator.CreateAnnotationOverlay(
                        overlayElements,
                        request.ResolutionScale);
                    Canvas.ForceUpdateCanvases();
                    // Chained CLI calls can read the previous GameView RT before overlay rendering catches up.
                    bool overlayFramesReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                        ANNOTATION_OVERLAY_RENDER_WAIT_FRAMES,
                        UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                        ct).ConfigureAwait(false);
                    if (!overlayFramesReady)
                    {
                        return CreateTimedOutResult(
                            "annotation overlay render",
                            correlationId,
                            new List<ScreenshotInfo>());
                    }

                    await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
                }

                (texture, captureRenderingInfo, captureTimedOut) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
                    request.ResolutionScale,
                    raycastGridRenderingInfo,
                    UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                    ct).ConfigureAwait(false);
                if (captureTimedOut)
                {
                    return CreateTimedOutResult(
                        "Play Mode view rendering capture",
                        correlationId,
                        new List<ScreenshotInfo>());
                }

                await CapturedEditorSynchronizationContext.SwitchTo(editorContext, ct);
            }
            finally
            {
                DestroyAnnotationOverlay(annotationOverlay, editorContext);
            }

            // Uses the settled capture-time size, not the pre-capture gameViewSize sample, so annotated
            // element coordinates stay consistent with the GameViewWidth/Height this response reports.
            UIElementAnnotator.ConvertToSimCoordinates(annotatedElements, Mathf.RoundToInt(captureRenderingInfo.GameViewSize.y));
            List<UIElementInfo> responseAnnotatedElements =
                CreateResponseAnnotatedElements(annotatedElements, physicsColliderElements);

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
                info.RaycastLayerSummaries = raycastLayerSummaries;
                info.RaycastLayerNamesChecked = raycastLayerNamesChecked;
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
                    new { CaptureMode = "rendering", ScreenshotCount = screenshots.Count, AnnotatedElements = annotatedElements.Count },
                    correlationId: correlationId
                );
            }

            return new ScreenshotResponse { Screenshots = screenshots };
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
