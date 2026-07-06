using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ApplicationRegistrar = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Compatibility entrypoint for execution callers that have not received UnityCliLoopExecutionRouter through DI yet.
    /// </summary>
    public static class UnityApiHandler
    {
        public static Task<UnityCliLoopToolResponse> ExecuteCommandAsync(
            string commandName,
            JToken paramsToken,
            CancellationToken ct)
        {
            UnityCliLoopToolRegistrarService toolRegistrarService = ApplicationRegistrar.Service;
            UnityCliLoopExecutionRouter executionRouter = new(toolRegistrarService);
            return executionRouter.ExecuteAsync(commandName, paramsToken, ct);
        }
    }
}
