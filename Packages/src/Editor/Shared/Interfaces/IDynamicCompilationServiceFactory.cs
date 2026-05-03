namespace io.github.hatayama.UnityCliLoop
{
    public interface IDynamicCompilationServiceFactory
    {
        IDynamicCompilationService Create(DynamicCodeSecurityLevel securityLevel);
    }
}
