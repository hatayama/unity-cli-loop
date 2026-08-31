using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates Record Replay Overlay instances with the dependencies required by this module.
    /// </summary>
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
