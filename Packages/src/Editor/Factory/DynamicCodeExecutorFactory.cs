namespace io.github.hatayama.uLoopMCP.Factory
{
    /// <summary>
    /// Exposes the default executor factory through a single composition entry point.
    /// </summary>
    public static class DynamicCodeExecutorFactory
    {
        public static IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel)
        {
            return DynamicCodeServices.ExecutorFactory.Create(securityLevel);
        }
    }
}
