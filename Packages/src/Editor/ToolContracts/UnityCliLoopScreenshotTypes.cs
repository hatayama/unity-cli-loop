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
        auto = 2,
        // Alias for rendering: agents commonly pass GameView when they mean Game View pixels.
        // Same underlying value so CaptureMode comparisons against rendering keep working.
        // Why after auto: Unity serializes the schema default as the enum ordinal, and the
        // CLI maps that number onto Enum.GetNames by index. Declaring GameView (value 1)
        // before auto (value 2) would make default 2 display as GameView in help.
        GameView = 1
    }
}
