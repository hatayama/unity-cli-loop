using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    internal interface ICompiledCommandInvoker
    {
        Task<ExecutionResult> ExecuteAsync(ExecutionContext context);
    }
}
