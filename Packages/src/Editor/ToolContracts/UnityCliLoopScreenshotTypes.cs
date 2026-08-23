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
        // Why a distinct value: Enum.GetNames sorts by value, so a shared ordinal with
        // rendering would occupy the next help index and make default auto display as GameView.
        // Alias semantics live in ScreenshotCaptureModeResolver, not in a shared value.
        GameView = 3
    }
}
