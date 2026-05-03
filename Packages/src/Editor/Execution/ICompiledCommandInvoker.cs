using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    internal interface ICompiledCommandInvoker
    {
        Task<ExecutionResult> ExecuteAsync(ExecutionContext context);
    }
}
