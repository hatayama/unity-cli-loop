using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines how Dynamic Compilation Service instances are created without exposing concrete construction.
    /// </summary>
    public interface IDynamicCompilationServiceFactory
    {
        IDynamicCompilationService Create(DynamicCodeSecurityLevel securityLevel);
    }
}
