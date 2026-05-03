using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Window name matching modes for FindWindowsByName
    /// </summary>
    public enum WindowMatchMode
    {
        /// <summary>
        /// Exact match (case-insensitive)
        /// </summary>
        exact = 0,

        /// <summary>
        /// Prefix match (case-insensitive)
        /// </summary>
        prefix = 1,

        /// <summary>
        /// Contains match (case-insensitive)
        /// </summary>
        contains = 2
    }

    public enum CaptureMode
    {
        window = 0,
        rendering = 1
    }

    public class ScreenshotSchema : BaseToolSchema
    {
        [Description("Window name to capture (e.g., 'Game', 'Scene', 'Console', 'Inspector', 'Project', 'Hierarchy', or any EditorWindow title). Ignored when CaptureMode is rendering.")]
        public string WindowName { get; set; } = "Game";

        [Description("Resolution scale multiplier (0.1 to 1.0, where 1.0 is original size)")]
        public float ResolutionScale { get; set; } = 1.0f;

        [Description("Window name matching mode: exact(0)=exact match, prefix(1)=starts with, contains(2)=partial match. All modes are case-insensitive. Ignored when CaptureMode is rendering.")]
        public WindowMatchMode MatchMode { get; set; } = WindowMatchMode.exact;

        [Description("Output directory path for saving screenshots. When empty, uses default path (.uloop/outputs/Screenshots/). Accepts absolute paths.")]
        public string OutputDirectory { get; set; } = "";

        [Description("Capture mode: window(0)=capture EditorWindow including toolbar, rendering(1)=capture game rendering only (PlayMode required, coordinates match simulate-mouse)")]
        public CaptureMode CaptureMode { get; set; } = CaptureMode.window;

        [Description("Annotate interactive UI elements with names and simulate-mouse coordinates on the screenshot. Only works with CaptureMode=rendering in PlayMode.")]
        public bool AnnotateElements { get; set; } = false;

        [Description("Return only annotated element JSON without capturing a screenshot image. Requires AnnotateElements=true and CaptureMode=rendering in PlayMode.")]
        public bool ElementsOnly { get; set; } = false;
    }
}
