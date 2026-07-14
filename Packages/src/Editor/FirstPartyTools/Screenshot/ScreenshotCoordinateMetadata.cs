using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies screenshot coordinate-system metadata onto ScreenshotInfo responses.
    /// </summary>
    internal static class ScreenshotCoordinateMetadata
    {
        internal static void ApplyRendering(
            ScreenshotInfo info,
            Vector2 gameViewSize,
            int imageToInputOffsetY = 0)
        {
            info.ImageCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW;
            info.GameViewWidth = gameViewSize.x;
            info.GameViewHeight = gameViewSize.y;
            info.ImageToInputOffsetY = imageToInputOffsetY;
            info.ScreenshotToInputFormula = UnityCliLoopConstants.SCREENSHOT_RENDERING_TO_INPUT_FORMULA;
            info.UnityInputFormula = UnityCliLoopConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY;
        }

        internal static void ApplyWindow(ScreenshotInfo info)
        {
            info.ImageCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_WINDOW;
            info.ScreenshotToInputFormula = UnityCliLoopConstants.SCREENSHOT_WINDOW_TO_INPUT_FORMULA_UNAVAILABLE;
            info.UnityInputFormula = "";
        }
    }
}
