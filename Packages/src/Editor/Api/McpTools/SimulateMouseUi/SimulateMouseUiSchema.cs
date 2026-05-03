using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateMouseUiSchema : BaseToolSchema
    {
        [Description("Mouse action: Click(0) - click with selected Button, Drag(1) - one-shot drag, DragStart(2) - begin drag and hold, DragMove(3) - move while holding drag, DragEnd(4) - release drag, LongPress(5) - press and hold for Duration seconds")]
        public MouseAction Action { get; set; } = MouseAction.Click;

        [Description("Target X position in screen pixels (origin: top-left). For Drag action, this is the destination.")]
        public float X { get; set; } = 0f;

        [Description("Target Y position in screen pixels (origin: top-left). For Drag action, this is the destination.")]
        public float Y { get; set; } = 0f;

        [Description("Start X position in screen pixels for Drag action (origin: top-left). Drag starts here and moves to X,Y.")]
        public float FromX { get; set; } = 0f;

        [Description("Start Y position in screen pixels for Drag action (origin: top-left). Drag starts here and moves to X,Y.")]
        public float FromY { get; set; } = 0f;

        [Description("Drag speed in pixels per second (0 for instant). Applies to Drag, DragMove, and DragEnd actions.")]
        public float DragSpeed { get; set; } = McpConstants.SIMULATE_MOUSE_UI_DEFAULT_DRAG_SPEED;

        [Description("Hold duration in seconds for LongPress action (default: 0.5).")]
        public float Duration { get; set; } = 0.5f;

        [Description("Mouse button: Left(0, default), Right(1), Middle(2).")]
        public MouseButton Button { get; set; } = MouseButton.Left;

        [Description("Bypass EventSystem raycast and send click, long-press, or drag events directly to TargetPath, or DropTargetPath for DragEnd. Useful for interacting with UI behind a raycast-blocking overlay.")]
        public bool BypassRaycast { get; set; } = false;

        [Description("Hierarchy path of the target GameObject used by Click, LongPress, Drag, and DragStart when BypassRaycast is true, for example Canvas/Panel/Button.")]
        public string TargetPath { get; set; } = "";

        [Description("Optional hierarchy path of the drop target used by Drag and DragEnd, for example Canvas/DropZone.")]
        public string DropTargetPath { get; set; } = "";
    }
}
