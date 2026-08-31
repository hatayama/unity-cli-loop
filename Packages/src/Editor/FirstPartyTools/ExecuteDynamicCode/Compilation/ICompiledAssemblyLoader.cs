namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines loading operations for Compiled Assembly artifacts.
    /// </summary>
    public interface ICompiledAssemblyLoader
    {
        // Why pdbBytes: optional portable PDB from shared-worker / one-shot csc; null keeps the
        // AssemblyBuilder fallback path (no line numbers) working without a second Load API.
        CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes);
    }
}
