using System;

namespace io.github.hatayama.uLoopMCP
{
    internal interface IDynamicCodeExecutorPool : IDisposable
    {
        IDynamicCodeExecutor GetOrCreate(DynamicCodeSecurityLevel securityLevel);
    }
}
