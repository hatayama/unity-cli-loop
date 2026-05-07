using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory
{
    /// <summary>
    /// Defines access to Dynamic Code Executor dependencies without exposing their implementation.
    /// </summary>
    internal interface IDynamicCodeExecutorProvider
    {
        IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel);
    }
}
