namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Compiled Assembly Load operations for its owning module.
    /// </summary>
    internal sealed class CompiledAssemblyLoadService : ICompiledAssemblyLoader
    {
        public CompiledAssemblyLoadResult Load(byte[] assemblyBytes)
        {
            return CompiledAssemblyLoader.Load(assemblyBytes);
        }
    }
}
