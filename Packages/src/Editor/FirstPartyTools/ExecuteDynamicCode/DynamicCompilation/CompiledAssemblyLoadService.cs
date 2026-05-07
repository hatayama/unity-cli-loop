using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Compiled Assembly Load operations for its owning module.
    /// </summary>
    internal sealed class CompiledAssemblyLoadService : ICompiledAssemblyLoader
    {
        public CompiledAssemblyLoadResult Load(
            DynamicCodeSecurityLevel securityLevel,
            byte[] assemblyBytes)
        {
            return CompiledAssemblyLoader.Load(securityLevel, assemblyBytes);
        }
    }
}
