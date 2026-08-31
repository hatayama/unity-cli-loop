using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines invocation of Compiled Command commands without exposing execution details.
    /// </summary>
    internal interface ICompiledCommandInvoker
    {
        Task<ExecutionResult> ExecuteAsync(ExecutionContext context);
    }
}
