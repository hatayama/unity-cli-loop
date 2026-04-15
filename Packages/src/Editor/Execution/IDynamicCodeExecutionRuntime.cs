using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    internal interface IDynamicCodeExecutionRuntime
    {
        bool SupportsAutoPrewarm();

        Task<ExecutionResult> ExecuteAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken = default);

        Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken = default);
    }
}
