using System.Collections.Generic;
using System.Threading;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Hides, restores, creates, and destroys screenshot overlays on the editor thread.
    /// </summary>
    internal static class ScreenshotOverlayControl
    {
        internal static GameObject CreateAnnotationOverlayIfNeeded(
            ScreenshotSchema request,
            ScreenshotUseCase.RenderingAnnotationCapture annotationCapture)
        {
            if (!request.AnnotateElements && !request.AnnotateRaycastGrid)
            {
                return null;
            }

            List<UIElementInfo> overlayElements = new(annotationCapture.AnnotatedElements);
            overlayElements.AddRange(annotationCapture.PhysicsColliderElements);
            GameObject annotationOverlay = UIElementAnnotator.CreateAnnotationOverlay(
                overlayElements,
                request.ResolutionScale);
            Canvas.ForceUpdateCanvases();
            return annotationOverlay;
        }

        // Hides the input-visualization canvas synchronously. Caller must already be on the editor
        // main thread, and must run the 2-frame settle wait inside a try/finally that restores.
        internal static (GameObject Overlay, bool WasActive) HideInputVisualizationOverlay()
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

        internal static void RestoreInputVisualizationOverlay(
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

        internal static void DestroyAnnotationOverlay(
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
    }
}
