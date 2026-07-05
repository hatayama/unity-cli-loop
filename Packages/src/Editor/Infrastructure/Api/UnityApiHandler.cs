using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ApplicationRegistrar = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{

    /// <summary>
    /// Routes JSON-RPC execution requests received by JsonRpcProcessor to either the
    /// internal bridge command router or the registered tools in the Application layer.
    /// Terminology for "tool" vs "internal bridge command" is defined in docs/glossary.md.
    /// </summary>
    public static class UnityApiHandler
    {
        /// <summary>
        /// Get command registry
        /// Use this registry when adding new commands
        /// </summary>
        public static UnityCliLoopToolRegistry CommandRegistry => ApplicationRegistrar.GetRegistry();

        /// <summary>
        /// Generic command execution method
        /// Uses new command-based structure
        /// </summary>
        /// <param name="commandName">Command name</param>
        /// <param name="paramsToken">Parameters</param>
        /// <returns>Execution result</returns>
        public static async Task<UnityCliLoopToolResponse> ExecuteCommandAsync(
            string commandName,
            JToken paramsToken,
            CancellationToken ct)
        {
            UnityCliLoopToolResponse response;
            if (InternalBridgeCommandRouter.IsInternalCommand(commandName))
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                response = InternalBridgeCommandRouter.Execute(commandName, paramsToken);
                return response;
            }

            response = await ApplicationRegistrar.ExecuteToolAsync(
                commandName,
                paramsToken,
                ct);
            return response;
        }
    }
}
