using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.InternalAPIBridge;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{

    /// <summary>
    /// Routes JSON-RPC execution requests received by JsonRpcRequestProcessor to either the
    /// internal bridge command router or the registered tools in the Application layer.
    /// Terminology for "tool" vs "internal bridge command" is defined in docs/glossary.md.
    /// </summary>
    internal sealed class UnityCliLoopExecutionRouter
    {
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;
        private readonly EditorExecutionActivity _executionActivity;

        internal UnityCliLoopExecutionRouter(
            UnityCliLoopToolRegistrarService toolRegistrarService,
            EditorExecutionActivity executionActivity)
        {
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");
            System.Diagnostics.Debug.Assert(executionActivity != null, "executionActivity must not be null");

            _toolRegistrarService = toolRegistrarService
                ?? throw new ArgumentNullException(nameof(toolRegistrarService));
            _executionActivity = executionActivity
                ?? throw new ArgumentNullException(nameof(executionActivity));
        }

        /// <summary>
        /// Routes one JSON-RPC method to either an internal bridge command or a registered tool.
        /// </summary>
        /// <param name="methodName">JSON-RPC method name</param>
        /// <param name="paramsToken">Parameters</param>
        /// <returns>Execution result</returns>
        public async Task<UnityCliLoopToolResponse> ExecuteAsync(
            string methodName,
            JToken paramsToken,
            CancellationToken ct)
        {
            if (methodName == UnityCliLoopConstants.COMMAND_NAME_GET_EDITOR_STATUS)
            {
                // Why before the main-thread switch below: uloop status must answer while the Editor
                // main thread is blocked, and this command reads only thread-safe snapshots.
                ct.ThrowIfCancellationRequested();
                // Why wake the loop: an idle Editor's main loop can sleep until something signals it,
                // so the stall counter grows while nothing is wrong. Waking it lets the CLI's second
                // probe tell a sleeping loop from a blocked one.
                EditorApplicationTickBridge.SignalTick();
                return EditorStatusBridgeCommand.Execute(
                    _toolRegistrarService,
                    EditorMainThreadLivenessTracker.SecondsSinceLastMainThreadTick());
            }

            // Why hold an activity for the length of the request: macOS throttles an Editor that is not
            // frontmost, and a request that arrives while it is throttled is served several times slower.
            // Why not around the status answer above: that path must stay free of anything that can block
            // or fail, because it answers while the main thread is stuck.
            using (_executionActivity.Hold())
            {
                return await ExecuteCommandOrToolAsync(methodName, paramsToken, ct);
            }
        }

        /// <summary>
        /// Runs one internal bridge command on the main thread, or hands one tool to the registrar.
        /// </summary>
        private async Task<UnityCliLoopToolResponse> ExecuteCommandOrToolAsync(
            string methodName,
            JToken paramsToken,
            CancellationToken ct)
        {
            UnityCliLoopToolResponse response;
            if (InternalBridgeCommandRouter.IsInternalCommand(methodName))
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                response = InternalBridgeCommandRouter.Execute(methodName, paramsToken, _toolRegistrarService);
                return response;
            }

            response = await _toolRegistrarService.ExecuteToolAsync(
                methodName,
                paramsToken,
                ct);
            return response;
        }
    }
}
