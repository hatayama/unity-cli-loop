using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class RecordReplayOverlayFactory
    {
        public static void EnsureRecordOverlay()
        {
            OverlayCanvasFactory.EnsureExists();
            InputVisualizationCanvas canvas = OverlayCanvasFactory.VisualizationCanvas;
            Debug.Assert(canvas.RecordInputOverlayPresenter != null,
                "RecordInputOverlayPresenter must be assigned on InputVisualizationCanvas prefab");
        }

        public static void EnsureReplayOverlay()
        {
            OverlayCanvasFactory.EnsureExists();
            InputVisualizationCanvas canvas = OverlayCanvasFactory.VisualizationCanvas;
            Debug.Assert(canvas.ReplayInputOverlay != null,
                "ReplayInputOverlay must be assigned on InputVisualizationCanvas prefab");
        }
    }
}
