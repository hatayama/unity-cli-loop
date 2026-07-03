#nullable enable

using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    public class ScreenshotInfo
    {
        public string ImagePath { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string ImageCoordinateSystem { get; set; } = McpConstants.COORDINATE_SYSTEM_TOP_LEFT_WINDOW;
        public float ResolutionScale { get; set; } = 1.0f;
        public int ImageToInputOffsetY { get; set; }
        public float GameViewWidth { get; set; }
        public float GameViewHeight { get; set; }
        public string ScreenshotToInputFormula { get; set; } = McpConstants.SCREENSHOT_WINDOW_TO_INPUT_FORMULA_UNAVAILABLE;
        public string UnityInputFormula { get; set; } = "";
        public List<UIElementInfo> AnnotatedElements { get; set; } = new();
        public List<RaycastGridPointInfo> RaycastGridPoints { get; set; } = new();

        public ScreenshotInfo(string imagePath, long fileSizeBytes, int width, int height,
            string imageCoordinateSystem = McpConstants.COORDINATE_SYSTEM_TOP_LEFT_WINDOW,
            float resolutionScale = 1.0f)
        {
            ImagePath = imagePath;
            FileSizeBytes = fileSizeBytes;
            Width = width;
            Height = height;
            ImageCoordinateSystem = imageCoordinateSystem;
            ResolutionScale = resolutionScale;
        }

        public ScreenshotInfo()
        {
        }
    }

    public class ScreenshotResponse : BaseToolResponse
    {
        public List<ScreenshotInfo> Screenshots { get; set; } = new();

        public int ScreenshotCount => Screenshots.Count;

        public ScreenshotResponse(List<ScreenshotInfo> screenshots)
        {
            Screenshots = screenshots;
        }

        public ScreenshotResponse()
        {
        }
    }
}
