using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Orchestrates tool execution after the domain registry has selected the registered tool.
    /// </summary>
    internal sealed class UnityCliLoopToolExecutionService
    {
        private readonly IEditorRuntimeStatePort _editorRuntimeStatePort;
        private readonly ToolExecutionSession _executionSession = new();

        internal UnityCliLoopToolExecutionService(IEditorRuntimeStatePort editorRuntimeStatePort)
        {
            Debug.Assert(editorRuntimeStatePort != null, "editorRuntimeStatePort must not be null");

            _editorRuntimeStatePort = editorRuntimeStatePort ?? throw new ArgumentNullException(nameof(editorRuntimeStatePort));
        }

        internal async Task<UnityCliLoopToolResponse> ExecuteToolAsync(
            UnityCliLoopToolRegistry registry,
            string toolName,
            JToken paramsToken,
            CancellationToken ct)
        {
            Debug.Assert(registry != null, "registry must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            ct.ThrowIfCancellationRequested();

            if (!registry.TryGetTool(toolName, out IUnityCliLoopTool tool))
            {
                throw new ArgumentException($"Unknown tool: {toolName}");
            }

            if (!registry.IsToolEnabled(toolName)
                && !ToolExecutionAvailability.ShouldReportDependencyUnavailableBeforeDisabled(toolName))
            {
                throw new ToolDisabledException(toolName);
            }

            if (!UnityCliLoopSecurityChecker.IsToolAllowed(registry, toolName))
            {
                throw new UnityCliLoopSecurityException(toolName, "Tool is blocked by security settings");
            }

            ToolExecutionSessionEnterResult enterResult = _executionSession.TryEnter(toolName);
            if (!enterResult.IsEntered)
            {
                throw CreateBusyException(enterResult.RunningToolName, toolName, _editorRuntimeStatePort);
            }

            try
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                UnityCliLoopEditorStateGuard.Validate(toolName, _editorRuntimeStatePort);

                UnityCliLoopToolResponse response = await tool.ExecuteAsync(paramsToken, ct).ConfigureAwait(false);
                if (response == null)
                {
                    throw new InvalidOperationException($"Tool returned null response: {toolName}");
                }

                return response;
            }
            finally
            {
                _executionSession.Exit();
            }
        }

        internal static UnityCliLoopToolBusyException CreateBusyException(
            string runningToolName,
            string requestedToolName,
            IEditorRuntimeStatePort editorRuntimeStatePort)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");
            Debug.Assert(editorRuntimeStatePort != null, "editorRuntimeStatePort must not be null");

            if (MainThreadSwitcher.IsMainThread)
            {
                return new UnityCliLoopToolBusyException(
                    runningToolName,
                    requestedToolName,
                    editorRuntimeStatePort.IsPlaying,
                    editorRuntimeStatePort.IsPaused);
            }

            (bool HasValue, bool IsPlaying, bool IsPaused) playState =
                UnityCliLoopEditorStateSnapshot.GetPlayState();
            if (playState.HasValue)
            {
                return new UnityCliLoopToolBusyException(
                    runningToolName,
                    requestedToolName,
                    playState.IsPlaying,
                    playState.IsPaused);
            }

            return new UnityCliLoopToolBusyException(runningToolName, requestedToolName);
        }

    }
}
