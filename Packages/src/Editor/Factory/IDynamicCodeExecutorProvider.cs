namespace io.github.hatayama.UnityCliLoop.Factory
{
    internal interface IDynamicCodeExecutorProvider
    {
        IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel);
    }
}
