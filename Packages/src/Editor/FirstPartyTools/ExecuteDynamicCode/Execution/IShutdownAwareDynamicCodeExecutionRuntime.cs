using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines dynamic code execution runtime operations that also support shutdown handling.
    /// </summary>
    internal interface IShutdownAwareDynamicCodeExecutionRuntime : IDynamicCodeExecutionRuntime
    {
        Task ShutdownAsync();
    }
}
