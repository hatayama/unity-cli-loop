using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Orchestrates tool execution after the domain registry has selected the registered tool.
    /// </summary>
    internal sealed class UnityCliLoopToolExecutionService
    {
        private const string UnknownToolName = "unknown";

        private readonly object _executionStateLock = new();
        private string _runningToolName;
        private int _runningExecutionCount;

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

            if (!TryEnterExecution(toolName, out string runningToolName))
            {
                throw CreateBusyException(runningToolName, toolName);
            }

            try
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();
                UnityCliLoopEditorStateGuard.Validate(toolName);

                UnityCliLoopToolResponse response = await tool.ExecuteAsync(paramsToken, ct);
                if (response == null)
                {
                    throw new InvalidOperationException($"Tool returned null response: {toolName}");
                }

                return response;
            }
            finally
            {
                ExitExecution();
            }
        }

        private bool TryEnterExecution(string toolName, out string runningToolName)
        {
            lock (_executionStateLock)
            {
                if (_runningExecutionCount == 0)
                {
                    _runningToolName = toolName;
                    _runningExecutionCount = 1;
                    runningToolName = toolName;
                    return true;
                }

                if (CanShareExecutionSlot(_runningToolName, toolName))
                {
                    _runningExecutionCount++;
                    runningToolName = _runningToolName;
                    return true;
                }

                runningToolName = GetRunningToolNameInsideLock();
                return false;
            }
        }

        private void ExitExecution()
        {
            lock (_executionStateLock)
            {
                Debug.Assert(_runningExecutionCount > 0, "running execution count must be positive before exit");
                _runningExecutionCount--;
                if (_runningExecutionCount > 0)
                {
                    return;
                }

                _runningToolName = null;
            }
        }

        private static bool CanShareExecutionSlot(string runningToolName, string requestedToolName)
        {
            return runningToolName == UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE
                   && requestedToolName == UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE;
        }

        internal static UnityCliLoopToolBusyException CreateBusyException(
            string runningToolName,
            string requestedToolName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");

            if (!MainThreadSwitcher.IsMainThread)
            {
                return new UnityCliLoopToolBusyException(runningToolName, requestedToolName);
            }

            return new UnityCliLoopToolBusyException(
                runningToolName,
                requestedToolName,
                EditorApplication.isPlaying,
                EditorApplication.isPaused);
        }

        private string GetRunningToolNameInsideLock()
        {
            return string.IsNullOrWhiteSpace(_runningToolName)
                ? UnknownToolName
                : _runningToolName;
        }
    }
}
