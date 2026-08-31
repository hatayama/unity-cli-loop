using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the runtime operations required to execute dynamic code.
    /// </summary>
    internal interface IDynamicCodeExecutionRuntime
    {
        Task<ExecutionResult> ExecuteAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken = default);

        Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken = default);
    }
}
