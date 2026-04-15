namespace io.github.hatayama.uLoopMCP
{
    internal interface ICompiledAssemblyLoader
    {
        CompiledAssemblyLoadResult Load(
            DynamicCodeSecurityLevel securityLevel,
            byte[] assemblyBytes);
    }
}
