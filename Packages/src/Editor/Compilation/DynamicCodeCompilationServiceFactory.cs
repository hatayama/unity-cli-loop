namespace io.github.hatayama.uLoopMCP
{
    public sealed class DynamicCodeCompilationServiceFactory : IDynamicCompilationServiceFactory
    {
        public IDynamicCompilationService Create(DynamicCodeSecurityLevel securityLevel)
        {
            return new DynamicCodeCompiler(securityLevel);
        }
    }
}
