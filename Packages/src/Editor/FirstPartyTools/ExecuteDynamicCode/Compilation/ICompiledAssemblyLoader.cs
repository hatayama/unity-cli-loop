using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines loading operations for Compiled Assembly artifacts.
    /// </summary>
    public interface ICompiledAssemblyLoader
    {
        CompiledAssemblyLoadResult Load(
            DynamicCodeSecurityLevel securityLevel,
            byte[] assemblyBytes);
    }
}
