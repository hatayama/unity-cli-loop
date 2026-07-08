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
        rendering = 1
    }

    /// <summary>
    /// Provides Unity CLI Loop Screenshot Coordinate System behavior for Unity CLI Loop.
    /// </summary>
    public static class UnityCliLoopScreenshotCoordinateSystem
    {
        public const string Window = "window";
        public const string GameView = "gameView";
    }
}
