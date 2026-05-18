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
        private const string UnknownToolName = "unknown";

        private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
        private readonly object _executionStateLock = new();
        private string _runningToolName;

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

            if (!_executionSemaphore.Wait(0))
            {
                throw new UnityCliLoopToolBusyException(GetRunningToolName(), toolName);
            }

            SetRunningToolName(toolName);
            try
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                ct.ThrowIfCancellationRequested();

                UnityCliLoopToolResponse response = await tool.ExecuteAsync(paramsToken, ct);
                if (response == null)
                {
                    throw new InvalidOperationException($"Tool returned null response: {toolName}");
                }

                return response;
            }
            finally
            {
                ClearRunningToolName(toolName);
                _executionSemaphore.Release();
            }
        }

        private void SetRunningToolName(string toolName)
        {
            lock (_executionStateLock)
            {
                _runningToolName = toolName;
            }
        }

        private void ClearRunningToolName(string toolName)
        {
            lock (_executionStateLock)
            {
                if (_runningToolName == toolName)
                {
                    _runningToolName = null;
                }
            }
        }

        private string GetRunningToolName()
        {
            lock (_executionStateLock)
            {
                return string.IsNullOrWhiteSpace(_runningToolName)
                    ? UnknownToolName
                    : _runningToolName;
            }
        }
    }
}
