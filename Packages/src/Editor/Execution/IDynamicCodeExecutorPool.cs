using System;

namespace io.github.hatayama.UnityCliLoop
{
    internal interface IDynamicCodeExecutorPool : IDisposable
    {
        IDynamicCodeExecutor GetOrCreate(DynamicCodeSecurityLevel securityLevel);
    }
}
