namespace io.github.hatayama.uLoopMCP.Factory
{
    internal interface IDynamicCodeExecutorProvider
    {
        IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel);
    }
}
