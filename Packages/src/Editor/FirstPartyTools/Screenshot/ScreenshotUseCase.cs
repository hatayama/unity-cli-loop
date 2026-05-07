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
    public class ScreenshotUseCase : IUnityCliLoopScreenshotService
    {
        private const int ANNOTATION_OVERLAY_RENDER_WAIT_FRAMES = 2;

        public async Task<UnityCliLoopScreenshotResult> CaptureAsync(
            UnityCliLoopScreenshotRequest request,
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

            ValidateParameters(request);

            if (request.CaptureMode == CaptureMode.rendering)
            {
                return await CaptureRenderingAsync(request, correlationId, ct);
            }

            return await CaptureWindowsAsync(request, correlationId, ct);
        }

        private async Task<UnityCliLoopScreenshotResult> CaptureRenderingAsync(
            UnityCliLoopScreenshotRequest request, string correlationId, CancellationToken ct)
        {
            if (!EditorApplication.isPlaying)
            {
                VibeLogger.LogError(
                    "screenshot_rendering_requires_playmode",
                    "CaptureMode.rendering requires PlayMode",
                    correlationId: correlationId
                );
                return new UnityCliLoopScreenshotResult();
            }

            List<UIElementInfo> annotatedElements = new();

            if (request.AnnotateElements)
            {
                annotatedElements = UIElementAnnotator.CollectInteractiveElements();
                UIElementAnnotator.AssignLabels(annotatedElements);
            }

            if (request.ElementsOnly)
            {
                UIElementAnnotator.ConvertToSimCoordinates(annotatedElements, (int)Handles.GetMainGameViewSize().y);
                UnityCliLoopScreenshotInfo elementsOnlyInfo = new();
                elementsOnlyInfo.CoordinateSystem = UnityCliLoopScreenshotCoordinateSystem.GameView;
                elementsOnlyInfo.AnnotatedElements = annotatedElements;
                return new UnityCliLoopScreenshotResult
                {
                    Screenshots = new List<UnityCliLoopScreenshotInfo> { elementsOnlyInfo }
                };
            }

            GameObject annotationOverlay = null;
            Texture2D texture;
            int yOffset;
            try
            {
                if (request.AnnotateElements)
                {
                    annotationOverlay = UIElementAnnotator.CreateAnnotationOverlay(
                        annotatedElements,
                        request.ResolutionScale);
                    Canvas.ForceUpdateCanvases();
                    // Chained CLI calls can read the previous GameView RT before overlay rendering catches up.
                    await EditorDelay.DelayFrame(ANNOTATION_OVERLAY_RENDER_WAIT_FRAMES, ct);
                }

                (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
                    request.ResolutionScale, ct);
            }
            finally
            {
                UIElementAnnotator.DestroyAnnotationOverlay(annotationOverlay);
            }

            UIElementAnnotator.ConvertToSimCoordinates(annotatedElements, (int)Handles.GetMainGameViewSize().y);

            if (texture == null)
            {
                VibeLogger.LogError(
                    "screenshot_rendering_unavailable",
                    "GameView RenderTexture is not available. Open the Game view and wait for a frame before retrying.",
                    correlationId: correlationId
                );
                return new UnityCliLoopScreenshotResult();
            }

            int width = texture.width;
            int height = texture.height;
            List<UnityCliLoopScreenshotInfo> screenshots = new();

            try
            {
                string outputDirectory = EnsureOutputDirectoryExists(request.OutputDirectory);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string savedPath = Path.Combine(outputDirectory, $"Rendering_{timestamp}.png");

                SaveTextureAsPng(texture, savedPath);

                FileInfo savedFileInfo = new(savedPath);
                UnityCliLoopScreenshotInfo info = new()                {
                    ImagePath = savedPath,
                    FileSizeBytes = savedFileInfo.Length,
                    Width = width,
                    Height = height,
                    CoordinateSystem = UnityCliLoopScreenshotCoordinateSystem.GameView,
                    ResolutionScale = request.ResolutionScale,
                    YOffset = yOffset,
                    AnnotatedElements = annotatedElements,
                };
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

            return new UnityCliLoopScreenshotResult { Screenshots = screenshots };
        }

        private async Task<UnityCliLoopScreenshotResult> CaptureWindowsAsync(
            UnityCliLoopScreenshotRequest request, string correlationId, CancellationToken ct)
        {
            EditorWindow[] windows = EditorWindowCaptureUtility.FindWindowsByName(request.WindowName, request.MatchMode);
            if (windows.Length == 0)
            {
                VibeLogger.LogError(
                    "screenshot_window_not_found",
                    $"Window '{request.WindowName}' not found (MatchMode: {request.MatchMode})",
                    correlationId: correlationId
                );
                return new UnityCliLoopScreenshotResult();
            }

            string outputDirectory = EnsureOutputDirectoryExists(request.OutputDirectory);
            string safeWindowName = SanitizeFileName(request.WindowName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            List<UnityCliLoopScreenshotInfo> screenshots = new();

            for (int i = 0; i < windows.Length; i++)
            {
                EditorWindow window = windows[i];
                Texture2D texture = await EditorWindowCaptureUtility.CaptureWindowAsync(window, request.ResolutionScale, ct);
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
                    SaveTextureAsPng(texture, savedPath);

                    FileInfo savedFileInfo = new(savedPath);
                    screenshots.Add(new UnityCliLoopScreenshotInfo
                    {
                        ImagePath = savedPath,
                        FileSizeBytes = savedFileInfo.Length,
                        Width = width,
                        Height = height,
                    });
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

            VibeLogger.LogInfo(
                "screenshot_success",
                $"Captured {screenshots.Count} window(s)",
                new { WindowName = request.WindowName, ScreenshotCount = screenshots.Count },
                correlationId: correlationId
            );

            return new UnityCliLoopScreenshotResult { Screenshots = screenshots };
        }

        private void ValidateParameters(UnityCliLoopScreenshotRequest request)
        {
            if (request.CaptureMode != CaptureMode.rendering &&
                string.IsNullOrEmpty(request.WindowName))
            {
                throw new UnityCliLoopToolParameterValidationException("WindowName cannot be null or empty");
            }

            if (request.ResolutionScale < 0.1f || request.ResolutionScale > 1.0f)
            {
                throw new UnityCliLoopToolParameterValidationException(
                    $"ResolutionScale must be between 0.1 and 1.0, got: {request.ResolutionScale}");
            }

            // AnnotateElements and ElementsOnly rely on PlayMode rendering pipeline
            if (request.CaptureMode != CaptureMode.rendering)
            {
                if (request.AnnotateElements)
                {
                    throw new UnityCliLoopToolParameterValidationException("AnnotateElements is only supported when CaptureMode=rendering");
                }

                if (request.ElementsOnly)
                {
                    throw new UnityCliLoopToolParameterValidationException("ElementsOnly is only supported when CaptureMode=rendering");
                }
            }

            if (request.ElementsOnly && !request.AnnotateElements)
            {
                throw new UnityCliLoopToolParameterValidationException("ElementsOnly requires AnnotateElements=true");
            }
        }

        private string EnsureOutputDirectoryExists(string outputDirectory)
        {
            string resolvedDirectory;

            if (string.IsNullOrEmpty(outputDirectory))
            {
                string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
                resolvedDirectory = Path.Combine(projectRoot, UnityCliLoopConstants.OUTPUT_ROOT_DIR, UnityCliLoopConstants.SCREENSHOTS_DIR);
            }
            else
            {
                resolvedDirectory = Path.GetFullPath(outputDirectory);
            }

            Directory.CreateDirectory(resolvedDirectory);

            return resolvedDirectory;
        }

        private string SanitizeFileName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = name;
            foreach (char c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }
            return sanitized;
        }

        private void SaveTextureAsPng(Texture2D texture, string fullPath)
        {
            byte[] pngData = texture.EncodeToPNG();
            if (pngData == null)
            {
                throw new InvalidOperationException($"Failed to encode texture to PNG. Format: {texture.format}, Size: {texture.width}x{texture.height}");
            }
            File.WriteAllBytes(fullPath, pngData);
        }
    }
}
