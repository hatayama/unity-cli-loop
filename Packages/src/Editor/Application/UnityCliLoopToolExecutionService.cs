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
        private readonly ToolExecutionSession _executionSession;

        internal UnityCliLoopToolExecutionService(
            IEditorRuntimeStatePort editorRuntimeStatePort,
            ToolExecutionSession executionSession = null)
        {
            Debug.Assert(editorRuntimeStatePort != null, "editorRuntimeStatePort must not be null");

            _editorRuntimeStatePort = editorRuntimeStatePort ?? throw new ArgumentNullException(nameof(editorRuntimeStatePort));
            _executionSession = executionSession ?? new ToolExecutionSession();
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

            ToolExecutionSessionBeginResult beginResult = _executionSession.Begin(registry, toolName);
            if (!beginResult.IsEntered)
            {
                throw CreateBusyException(
                    beginResult.RunningToolName,
                    toolName,
                    _editorRuntimeStatePort,
                    beginResult.RunningToolElapsedSeconds);
            }

            try
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                UnityCliLoopEditorStateGuard.Validate(toolName, _editorRuntimeStatePort);

                UnityCliLoopToolResponse response = await beginResult.Tool.ExecuteAsync(paramsToken, ct).ConfigureAwait(false);
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
            IEditorRuntimeStatePort editorRuntimeStatePort,
            int? runningToolElapsedSeconds = null)
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
                    editorRuntimeStatePort.IsPaused,
                    editorRuntimeStatePort.IsCompiling,
                    editorRuntimeStatePort.IsUpdating,
                    runningToolElapsedSeconds);
            }

            (bool HasValue, bool IsPlaying, bool IsPaused) playState =
                UnityCliLoopEditorStateSnapshot.GetPlayState();
            if (playState.HasValue)
            {
                return new UnityCliLoopToolBusyException(
                    runningToolName,
                    requestedToolName,
                    playState.IsPlaying,
                    playState.IsPaused,
                    runningToolElapsedSeconds: runningToolElapsedSeconds);
            }

            return new UnityCliLoopToolBusyException(
                runningToolName,
                requestedToolName,
                runningToolElapsedSeconds: runningToolElapsedSeconds);
        }
    }
}
