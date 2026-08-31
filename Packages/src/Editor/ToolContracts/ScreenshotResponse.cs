#nullable enable

using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Describes Screenshot information collected by the owning workflow.
    /// </summary>
    public class ScreenshotInfo
    {
        public string ImagePath { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string ImageCoordinateSystem { get; set; } = UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_WINDOW;
        public float ResolutionScale { get; set; } = 1.0f;

        // Y offset to add to image pixel Y to get simulate-mouse Y coordinate.
        // Only meaningful when ImageCoordinateSystem is the Game View system.
        public int ImageToInputOffsetY { get; set; }

        public float GameViewWidth { get; set; }
        public float GameViewHeight { get; set; }
        public string ScreenshotToInputFormula { get; set; } = UnityCliLoopConstants.SCREENSHOT_WINDOW_TO_INPUT_FORMULA_UNAVAILABLE;
        public string UnityInputFormula { get; set; } = "";
        public List<UIElementInfo> AnnotatedElements { get; set; } = new List<UIElementInfo>();
        public List<RaycastLayerSummaryInfo> RaycastLayerSummaries { get; set; } = new List<RaycastLayerSummaryInfo>();
        public List<string> RaycastLayerNamesChecked { get; set; } = new List<string>();
    }

    /// <summary>
    /// Carries the response data returned by the Screenshot tool.
    /// </summary>
    public class ScreenshotResponse : UnityCliLoopToolResponse
    {
        public List<ScreenshotInfo> Screenshots { get; set; } = new List<ScreenshotInfo>();
        public bool TimedOut { get; set; }
        public string Message { get; set; } = "";
        public string Warning { get; set; } = "";
        public string ResolvedCaptureMode { get; set; } = "";
        public string[] NextActions { get; set; } = new string[0];

        public int ScreenshotCount => Screenshots.Count;

        // Why omit empty: Edit Mode and rendering captures must not grow a Warning field.
        public bool ShouldSerializeWarning()
        {
            return !string.IsNullOrEmpty(Warning);
        }
    }
}
