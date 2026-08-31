namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    public enum UnityCliLoopKeyboardAction
    {
        Press = 0,
        KeyDown = 1,
        KeyUp = 2,
        ReleaseAll = 3
    }

    public enum UnityCliLoopMouseInputAction
    {
        Click = 0,
        LongPress = 1,
        MoveDelta = 2,
        Scroll = 3,
        SmoothDelta = 4
    }

    public enum UnityCliLoopMouseUiAction
    {
        Click = 0,
        Drag = 1,
        DragStart = 2,
        DragMove = 3,
        DragEnd = 4,
        LongPress = 5
    }

    public enum UnityCliLoopMouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    /// <summary>
    /// Default values shared between the CLI schemas and the input simulation pipeline.
    /// </summary>
    public static class UnityCliLoopInputSimulationDefaults
    {
        public const float MouseUiDragSpeed = 2000f;
        public const float MouseUiDuration = 0.5f;
    }

    /// <summary>
    /// Identifies one pause point marker that was hit while an input simulation ran.
    /// </summary>
    public sealed class UnityCliLoopPausePointHit
    {
        public string Id { get; set; } = "";
        public int HitCount { get; set; }
    }
}
