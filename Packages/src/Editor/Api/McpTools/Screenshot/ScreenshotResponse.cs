#nullable enable

using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    public class ScreenshotInfo
    {
        public string ImagePath { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        // "gameView": image from game RenderTexture. Convert to simulate-mouse coords:
        //   sim_x = image_x / ResolutionScale
        //   sim_y = image_y / ResolutionScale + YOffset
        // "window": EditorWindow capture including toolbar
        public string CoordinateSystem { get; set; } = McpConstants.COORDINATE_SYSTEM_WINDOW;

        public float ResolutionScale { get; set; } = 1.0f;

        // Y offset to add to image pixel Y to get simulate-mouse Y coordinate.
        // Only meaningful when CoordinateSystem == "gameView".
        public int YOffset { get; set; }

        public List<UIElementInfo> AnnotatedElements { get; set; } = new();

        public ScreenshotInfo(string imagePath, long fileSizeBytes, int width, int height,
            string coordinateSystem = McpConstants.COORDINATE_SYSTEM_WINDOW,
            float resolutionScale = 1.0f, int yOffset = 0)
        {
            ImagePath = imagePath;
            FileSizeBytes = fileSizeBytes;
            Width = width;
            Height = height;
            CoordinateSystem = coordinateSystem;
            ResolutionScale = resolutionScale;
            YOffset = yOffset;
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
