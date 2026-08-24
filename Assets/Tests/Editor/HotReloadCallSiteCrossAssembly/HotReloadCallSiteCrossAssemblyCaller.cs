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
            return HotReloadCallSiteScannerCrossAssemblyTarget.Called();
        }

        public static int CallSameFullNameTarget()
        {
            return HotReloadCallSiteScannerFixture.CalledFromCrossAssembly();
        }
    }

    /// <summary>
    /// Has the same metadata name and method signature as the main-assembly fixture target.
    /// </summary>
    public static class HotReloadCallSiteScannerFixture
    {
        public static int CalledFromCrossAssembly()
        {
            return 8;
        }
    }
}
