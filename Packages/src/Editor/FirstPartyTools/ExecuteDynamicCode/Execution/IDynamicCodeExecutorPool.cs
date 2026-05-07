using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines pooled access to Dynamic Code Executor instances for the owning workflow.
    /// </summary>
    internal interface IDynamicCodeExecutorPool : IDisposable
    {
        IDynamicCodeExecutor GetOrCreate(DynamicCodeSecurityLevel securityLevel);
    }
}
