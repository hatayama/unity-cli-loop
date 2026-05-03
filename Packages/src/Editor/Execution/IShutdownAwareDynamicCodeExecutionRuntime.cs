using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    internal interface IShutdownAwareDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
    {
        Task ShutdownAsync();
    }
}
