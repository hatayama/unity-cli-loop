using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Owns the thread-safe single-flight state for tool execution sessions.
    /// </summary>
    internal sealed class ToolExecutionSession
    {
        private const string UnknownToolName = "unknown";

        private readonly object _executionStateLock = new();
        private readonly Func<long> _timestampProvider;
        private string _runningToolName;
        private int _runningExecutionCount;
        private long _runningStartedTimestamp;

        internal ToolExecutionSession(Func<long> timestampProvider = null)
        {
            _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        }

        internal ToolExecutionSessionBeginResult Begin(UnityCliLoopToolRegistry registry, string toolName)
        {
            Debug.Assert(registry != null, "registry must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

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

            ToolExecutionSessionEnterResult enterResult = TryEnter(toolName);
            if (!enterResult.IsEntered)
            {
                return ToolExecutionSessionBeginResult.Busy(
                    enterResult.RunningToolName,
                    enterResult.RunningToolElapsedSeconds);
            }

            return ToolExecutionSessionBeginResult.Entered(tool);
        }

        internal ToolExecutionSessionEnterResult TryEnter(string requestedToolName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");

            lock (_executionStateLock)
            {
                if (_runningExecutionCount == 0)
                {
                    _runningToolName = requestedToolName;
                    _runningExecutionCount = 1;
                    _runningStartedTimestamp = _timestampProvider();
                    return ToolExecutionSessionEnterResult.Entered();
                }

                if (CanShareExecutionSlot(_runningToolName, requestedToolName))
                {
                    // Why not refresh the start timestamp: shared-slot re-entry is the same
                    // flight, so elapsed must keep measuring from the first Begin.
                    _runningExecutionCount++;
                    return ToolExecutionSessionEnterResult.Entered();
                }

                return ToolExecutionSessionEnterResult.Busy(
                    GetRunningToolNameInsideLock(),
                    GetRunningToolElapsedSecondsInsideLock());
            }
        }

        internal void Exit()
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
                _runningStartedTimestamp = 0;
            }
        }

        private static bool CanShareExecutionSlot(string runningToolName, string requestedToolName)
        {
            return runningToolName == UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE
                   && requestedToolName == UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE;
        }

        private string GetRunningToolNameInsideLock()
        {
            return string.IsNullOrWhiteSpace(_runningToolName)
                ? UnknownToolName
                : _runningToolName;
        }

        private int GetRunningToolElapsedSecondsInsideLock()
        {
            long elapsedTicks = _timestampProvider() - _runningStartedTimestamp;
            Debug.Assert(elapsedTicks >= 0, "monotonic session clock must not go backwards");
            return (int)(elapsedTicks / Stopwatch.Frequency);
        }
    }

    /// <summary>
    /// Reports whether a tool execution request passed admission and entered the session.
    /// </summary>
    internal readonly struct ToolExecutionSessionBeginResult
    {
        public readonly bool IsEntered;
        public readonly IUnityCliLoopTool Tool;
        public readonly string RunningToolName;
        public readonly int? RunningToolElapsedSeconds;

        private ToolExecutionSessionBeginResult(
            bool isEntered,
            IUnityCliLoopTool tool,
            string runningToolName,
            int? runningToolElapsedSeconds)
        {
            Debug.Assert(isEntered == (tool != null), "entered sessions must carry a tool");
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            Tool = tool;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
        }

        public static ToolExecutionSessionBeginResult Entered(IUnityCliLoopTool tool)
        {
            Debug.Assert(tool != null, "tool must not be null");

            return new ToolExecutionSessionBeginResult(true, tool, string.Empty, null);
        }

        public static ToolExecutionSessionBeginResult Busy(string runningToolName, int? runningToolElapsedSeconds = null)
        {
            return new ToolExecutionSessionBeginResult(false, null, runningToolName, runningToolElapsedSeconds);
        }
    }

    /// <summary>
    /// Reports whether a tool execution request entered the session or was rejected by the single-flight gate.
    /// </summary>
    internal readonly struct ToolExecutionSessionEnterResult
    {
        public readonly bool IsEntered;
        public readonly string RunningToolName;
        public readonly int? RunningToolElapsedSeconds;

        private ToolExecutionSessionEnterResult(bool isEntered, string runningToolName, int? runningToolElapsedSeconds)
        {
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
        }

        public static ToolExecutionSessionEnterResult Entered()
        {
            return new ToolExecutionSessionEnterResult(true, string.Empty, null);
        }

        public static ToolExecutionSessionEnterResult Busy(string runningToolName, int? runningToolElapsedSeconds = null)
        {
            return new ToolExecutionSessionEnterResult(false, runningToolName, runningToolElapsedSeconds);
        }
    }
}
