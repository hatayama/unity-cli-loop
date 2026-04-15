namespace io.github.hatayama.uLoopMCP
{
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
