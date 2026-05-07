#nullable enable

using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
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

        // "gameView": image from game RenderTexture. Convert to simulate-mouse coords:
        //   sim_x = image_x / ResolutionScale
        //   sim_y = image_y / ResolutionScale + YOffset
        // "window": EditorWindow capture including toolbar
        public string CoordinateSystem { get; set; } = UnityCliLoopScreenshotCoordinateSystem.Window;

        public float ResolutionScale { get; set; } = 1.0f;

        // Y offset to add to image pixel Y to get simulate-mouse Y coordinate.
        // Only meaningful when CoordinateSystem == "gameView".
        public int YOffset { get; set; }

        public List<UIElementInfo> AnnotatedElements { get; set; } = new List<UIElementInfo>();
    }

    /// <summary>
    /// Carries the response data returned by the Screenshot tool.
    /// </summary>
    public class ScreenshotResponse : UnityCliLoopToolResponse
    {
        public List<ScreenshotInfo> Screenshots { get; set; } = new List<ScreenshotInfo>();

        public int ScreenshotCount => Screenshots.Count;
    }
}
