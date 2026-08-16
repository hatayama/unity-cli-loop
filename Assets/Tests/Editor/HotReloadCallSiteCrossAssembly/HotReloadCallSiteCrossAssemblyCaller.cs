namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled caller in a separate assembly so the call-site scanner must follow
    /// a project-assembly reference, not only the target assembly itself.
    /// </summary>
    public static class HotReloadCallSiteCrossAssemblyCaller
    {
        public static int Call()
        {
            return HotReloadCallSiteScannerFixture.CalledFromCrossAssembly();
        }
    }
}
