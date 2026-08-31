using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds screenshot timeout, coordinate, and elements-only response payloads.
    /// </summary>
    internal static class ScreenshotCaptureResults
    {
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
