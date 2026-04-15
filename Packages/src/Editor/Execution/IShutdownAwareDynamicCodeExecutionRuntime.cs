using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    internal interface IShutdownAwareDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
    {
        Task ShutdownAsync();
    }
}
