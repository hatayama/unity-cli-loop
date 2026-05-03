using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateMouseInputSchema : BaseToolSchema
    {
        [Description("Mouse input action: Click(0) - inject left/right/middle button press+release, LongPress(1) - inject button hold for Duration seconds, MoveDelta(2) - inject mouse delta for camera/look (one-shot), Scroll(3) - inject scroll wheel, SmoothDelta(4) - inject mouse delta smoothly over Duration seconds")]
        public MouseInputAction Action { get; set; } = MouseInputAction.Click;

        [Description("Target X position in screen pixels (origin: top-left). Used by Click and LongPress to set Mouse.current.position.")]
        public float X { get; set; } = 0f;

        [Description("Target Y position in screen pixels (origin: top-left). Used by Click and LongPress to set Mouse.current.position.")]
        public float Y { get; set; } = 0f;

        [Description("Mouse button: Left(0, default), Right(1), Middle(2). Used by Click and LongPress.")]
        public MouseButton Button { get; set; } = MouseButton.Left;

        [Description("Hold duration in seconds for LongPress, or minimum hold time for Click (0 = one-shot tap). Also used as total duration for SmoothDelta.")]
        public float Duration { get; set; } = 0f;

        [Description("Delta X in pixels for MoveDelta/SmoothDelta action. Positive = right.")]
        public float DeltaX { get; set; } = 0f;

        [Description("Delta Y in pixels for MoveDelta/SmoothDelta action. Positive = up (Unity screen space).")]
        public float DeltaY { get; set; } = 0f;

        [Description("Horizontal scroll delta for Scroll action (default: 0).")]
        public float ScrollX { get; set; } = 0f;

        [Description("Vertical scroll delta for Scroll action. Positive = up, negative = down. Typically 120 per notch.")]
        public float ScrollY { get; set; } = 0f;
    }
}
