using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Collects physics-collider raycast-grid annotations for a rendering screenshot.
    /// </summary>
    internal static class ScreenshotRaycastGridCollector
    {
        internal static async Task<ScreenshotResponse> CollectRaycastGridAnnotationsAsync(
            ScreenshotSchema request,
            ScreenshotUseCase.RenderingAnnotationCapture annotationCapture,
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
                return ScreenshotCaptureResults.CreateTimedOutResult(
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
    }
}
