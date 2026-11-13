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
    /// get-editor-status, cancel-running-tool, and report-blocking-dialog are answered here, off the main thread and outside
    /// the activity hold, because they must answer while a tool blocks the Editor main thread.
    /// Terminology for "tool" vs "internal bridge command" is defined in docs/glossary.md.
    /// </summary>
    internal sealed class UnityCliLoopExecutionRouter
    {
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;
        private readonly EditorExecutionActivity _executionActivity;
        private readonly EditorDialogTracker _dialogTracker;
        // Why read here: the router is built once when the server starts, and get-editor-status must
        // stay cheap because it answers while the main thread is blocked.
        private readonly int _editorProcessId;

        internal UnityCliLoopExecutionRouter(
            UnityCliLoopToolRegistrarService toolRegistrarService,
            EditorExecutionActivity executionActivity,
            EditorDialogTracker dialogTracker)
        {
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");
            System.Diagnostics.Debug.Assert(executionActivity != null, "executionActivity must not be null");
            System.Diagnostics.Debug.Assert(dialogTracker != null, "dialogTracker must not be null");

            _toolRegistrarService = toolRegistrarService
                ?? throw new ArgumentNullException(nameof(toolRegistrarService));
            _executionActivity = executionActivity
                ?? throw new ArgumentNullException(nameof(executionActivity));
            _dialogTracker = dialogTracker
                ?? throw new ArgumentNullException(nameof(dialogTracker));
            using (System.Diagnostics.Process editorProcess = System.Diagnostics.Process.GetCurrentProcess())
            {
                _editorProcessId = editorProcess.Id;
            }
        }

        /// <summary>
        /// Routes one JSON-RPC method to either an internal bridge command or a registered tool.
        /// </summary>
        /// <param name="methodName">JSON-RPC method name</param>
        /// <param name="paramsToken">Parameters</param>
        /// <param name="cancellableByUser">Whether uloop cancel may stop the tool this request runs</param>
        /// <returns>Execution result</returns>
        public async Task<UnityCliLoopToolResponse> ExecuteAsync(
            string methodName,
            JToken paramsToken,
            CancellationToken ct,
            bool cancellableByUser = true)
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
                    EditorMainThreadLivenessTracker.SecondsSinceLastMainThreadTick(),
                    _dialogTracker.GetOpenDialogs(),
                    _editorProcessId);
            }

            if (methodName == UnityCliLoopConstants.COMMAND_NAME_CANCEL_RUNNING_TOOL)
            {
                // Why before the main-thread switch below: uloop cancel exists for the tool that
                // blocks the main thread, so it must answer without waiting for it.
                ct.ThrowIfCancellationRequested();
                return CancelRunningToolBridgeCommand.Execute(_toolRegistrarService);
            }

            if (methodName == UnityCliLoopConstants.COMMAND_NAME_REPORT_BLOCKING_DIALOG)
            {
                // Why before the main-thread switch below: the report is about a main thread a dialog
                // holds, so it must be taken without waiting for it.
                ct.ThrowIfCancellationRequested();
                return ReportBlockingDialogBridgeCommand.Execute(_toolRegistrarService);
            }

            // Why hold an activity for the length of the request: macOS throttles an Editor that is not
            // frontmost, and a request that arrives while it is throttled is served several times slower.
            // Why not around the status, cancel, and dialog-report answers above: those paths must stay free of anything
            // that can block or fail, because they answer while the main thread is stuck.
            using (_executionActivity.Hold())
            {
                return await ExecuteCommandOrToolAsync(methodName, paramsToken, ct, cancellableByUser);
            }
        }

        /// <summary>
        /// Runs one internal bridge command on the main thread, or hands one tool to the registrar.
        /// </summary>
        private async Task<UnityCliLoopToolResponse> ExecuteCommandOrToolAsync(
            string methodName,
            JToken paramsToken,
            CancellationToken ct,
            bool cancellableByUser)
        {
            UnityCliLoopToolResponse response;
            if (InternalBridgeCommandRouter.IsInternalCommand(methodName))
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                response = InternalBridgeCommandRouter.Execute(methodName, paramsToken, _toolRegistrarService);
                return response;
            }

            response = await _toolRegistrarService.ExecuteRequestedToolAsync(
                methodName,
                paramsToken,
                ct,
                cancellableByUser);
            return response;
        }
    }
}
