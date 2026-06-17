namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines loading operations for Compiled Assembly artifacts.
    /// </summary>
    public interface ICompiledAssemblyLoader
    {
        CompiledAssemblyLoadResult Load(byte[] assemblyBytes);
    }
}
