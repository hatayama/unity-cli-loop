namespace io.github.hatayama.UnityCliLoop
{
    internal interface ICompiledAssemblyLoader
    {
        CompiledAssemblyLoadResult Load(
            DynamicCodeSecurityLevel securityLevel,
            byte[] assemblyBytes);
    }
}
