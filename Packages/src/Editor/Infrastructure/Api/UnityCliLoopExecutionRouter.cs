using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{

    /// <summary>
    /// Routes JSON-RPC execution requests received by JsonRpcProcessor to either the
    /// internal bridge command router or the registered tools in the Application layer.
    /// Terminology for "tool" vs "internal bridge command" is defined in docs/glossary.md.
    /// </summary>
    internal sealed class UnityCliLoopExecutionRouter
    {
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;

        internal UnityCliLoopExecutionRouter(UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            _toolRegistrarService = toolRegistrarService
                ?? throw new ArgumentNullException(nameof(toolRegistrarService));
        }

        /// <summary>
        /// Generic command execution method
        /// Uses new command-based structure
        /// </summary>
        /// <param name="commandName">Command name</param>
        /// <param name="paramsToken">Parameters</param>
        /// <returns>Execution result</returns>
        public async Task<UnityCliLoopToolResponse> ExecuteAsync(
            string commandName,
            JToken paramsToken,
            CancellationToken ct)
        {
            UnityCliLoopToolResponse response;
            if (InternalBridgeCommandRouter.IsInternalCommand(commandName))
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                response = InternalBridgeCommandRouter.Execute(commandName, paramsToken, _toolRegistrarService);
                return response;
            }

            response = await _toolRegistrarService.ExecuteToolAsync(
                commandName,
                paramsToken,
                ct);
            return response;
        }
    }
}
