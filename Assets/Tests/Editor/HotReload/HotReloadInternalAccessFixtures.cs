namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Supplies an internal member that generated artifact code cannot normally invoke.
    /// </summary>
    internal static class HotReloadInternalAccessFixture
    {
        internal static int Read()
        {
            return 21;
        }
    }
}
