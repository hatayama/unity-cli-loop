namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One active hot-reload patch for --status and the stale-signature warning: method key
    /// plus the source file path that was applied (project-relative when applied through the
    /// orchestrator), and the name of the assembly that declares the patched method.
    /// </summary>
    internal sealed class HotReloadActivePatchInfo
    {
        public string MethodKey { get; }
        public string FilePath { get; }

        // Why kept beside the label: two assemblies can declare a method with the same label, and
        // a compiled call site must be matched to the patch of its own assembly's method.
        public string AssemblyName { get; }

        public HotReloadActivePatchInfo(string methodKey, string filePath, string assemblyName)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            AssemblyName = assemblyName ?? string.Empty;
        }
    }
}
