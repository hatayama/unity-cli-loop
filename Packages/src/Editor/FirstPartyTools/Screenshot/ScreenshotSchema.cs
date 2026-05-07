
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the parameters accepted by the Screenshot tool.
    /// </summary>
    public class ScreenshotSchema : UnityCliLoopToolSchema
    {
        public string WindowName { get; set; } = "Game";
        public float ResolutionScale { get; set; } = 1.0f;
        public WindowMatchMode MatchMode { get; set; } = WindowMatchMode.exact;
        public string OutputDirectory { get; set; } = "";
        public CaptureMode CaptureMode { get; set; } = CaptureMode.window;
        public bool AnnotateElements { get; set; } = false;
        public bool ElementsOnly { get; set; } = false;
    }
}
