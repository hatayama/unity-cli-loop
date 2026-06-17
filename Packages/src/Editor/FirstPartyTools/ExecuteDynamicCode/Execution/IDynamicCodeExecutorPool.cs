using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines pooled access to Dynamic Code Executor instances for the owning workflow.
    /// </summary>
    internal interface IDynamicCodeExecutorPool : IDisposable
    {
        IDynamicCodeExecutor GetOrCreate();
    }
}
