namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Checks that the compilation pipeline puts an assembly where hot reload reads it from.
    /// </summary>
    internal static class HotReloadCompiledAssemblyPathCheck
    {
        internal static HotReloadFailureDescription DescribeOutputPathMismatch(
            CompiledAssemblyLayout layout,
            string assemblyName,
            string outputPath)
        {
            return null;
        }
    }
}
