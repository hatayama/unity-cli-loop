namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    public enum WindowMatchMode
    {
        exact = 0,
        prefix = 1,
        contains = 2
    }

    public enum CaptureMode
    {
        window = 0,
        rendering = 1,
        // Alias for rendering: agents commonly pass GameView when they mean Game View pixels.
        // Same underlying value so CaptureMode comparisons against rendering keep working.
        GameView = 1
    }
}
