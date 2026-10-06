namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Recognizes a Multiplayer Play Mode Virtual Player by its project root, and words the
    /// missing-assembly reason for it.
    /// </summary>
    internal static class HotReloadVirtualPlayerProject
    {
        internal static bool IsVirtualPlayerProjectRoot(string projectRoot)
        {
            return false;
        }

        internal static string DescribeMissingCompiledAssembly(string projectRoot, string dllPath)
        {
            return "Compiled assembly not found at '" + dllPath + "'. Compile the project first.";
        }
    }
}
