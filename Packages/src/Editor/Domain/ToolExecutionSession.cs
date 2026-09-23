using System;
using System.Collections.Generic;
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
        private readonly HashSet<ToolExecutionLease> _activeLeases = new();
        private string _runningToolName;
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

            return ToolExecutionSessionBeginResult.Entered(tool, enterResult.Lease);
        }

        internal ToolExecutionSessionEnterResult TryEnter(string requestedToolName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");

            lock (_executionStateLock)
            {
                if (_activeLeases.Count == 0)
                {
                    _runningToolName = requestedToolName;
                    _runningStartedTimestamp = _timestampProvider();
                    return ToolExecutionSessionEnterResult.Entered(IssueLeaseInsideLock());
                }

                if (CanShareExecutionSlot(_runningToolName, requestedToolName))
                {
                    // Why not refresh the start timestamp: shared-slot re-entry is the same
                    // flight, so elapsed must keep measuring from the first Begin.
                    return ToolExecutionSessionEnterResult.Entered(IssueLeaseInsideLock());
                }

                return ToolExecutionSessionEnterResult.Busy(
                    GetRunningToolNameInsideLock(),
                    GetRunningToolElapsedSecondsInsideLock());
            }
        }

        // Returns the slot a lease holds. Only a lease that is still active gives anything back,
        // so a second Dispose of the same lease cannot release a slot a later holder owns.
        internal void Release(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            lock (_executionStateLock)
            {
                if (!_activeLeases.Remove(lease))
                {
                    return;
                }

                if (_activeLeases.Count > 0)
                {
                    return;
                }

                _runningToolName = null;
                _runningStartedTimestamp = 0;
            }
        }

        private ToolExecutionLease IssueLeaseInsideLock()
        {
            ToolExecutionLease lease = new ToolExecutionLease(this);
            _activeLeases.Add(lease);
            return lease;
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
    /// Holds one entry of the execution slot; disposing it gives the entry back to its session.
    /// </summary>
    internal sealed class ToolExecutionLease : IDisposable
    {
        private readonly ToolExecutionSession _session;

        internal ToolExecutionLease(ToolExecutionSession session)
        {
            Debug.Assert(session != null, "session must not be null");

            _session = session;
        }

        public void Dispose()
        {
            _session.Release(this);
        }
    }

    /// <summary>
    /// Reports whether a tool execution request passed admission and entered the session.
    /// </summary>
    internal readonly struct ToolExecutionSessionBeginResult
    {
        public readonly bool IsEntered;
        public readonly IUnityCliLoopTool Tool;
        public readonly ToolExecutionLease Lease;
        public readonly string RunningToolName;
        public readonly int? RunningToolElapsedSeconds;

        private ToolExecutionSessionBeginResult(
            bool isEntered,
            IUnityCliLoopTool tool,
            ToolExecutionLease lease,
            string runningToolName,
            int? runningToolElapsedSeconds)
        {
            Debug.Assert(isEntered == (tool != null), "entered sessions must carry a tool");
            Debug.Assert(isEntered == (lease != null), "entered sessions must carry a lease");
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            Tool = tool;
            Lease = lease;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
        }

        public static ToolExecutionSessionBeginResult Entered(IUnityCliLoopTool tool, ToolExecutionLease lease)
        {
            Debug.Assert(tool != null, "tool must not be null");
            Debug.Assert(lease != null, "lease must not be null");

            return new ToolExecutionSessionBeginResult(true, tool, lease, string.Empty, null);
        }

        public static ToolExecutionSessionBeginResult Busy(string runningToolName, int? runningToolElapsedSeconds = null)
        {
            return new ToolExecutionSessionBeginResult(false, null, null, runningToolName, runningToolElapsedSeconds);
        }
    }

    /// <summary>
    /// Reports whether a tool execution request entered the session or was rejected by the single-flight gate.
    /// </summary>
    internal readonly struct ToolExecutionSessionEnterResult
    {
        public readonly bool IsEntered;
        public readonly ToolExecutionLease Lease;
        public readonly string RunningToolName;
        public readonly int? RunningToolElapsedSeconds;

        private ToolExecutionSessionEnterResult(
            bool isEntered,
            ToolExecutionLease lease,
            string runningToolName,
            int? runningToolElapsedSeconds)
        {
            Debug.Assert(isEntered == (lease != null), "entered sessions must carry a lease");
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            Lease = lease;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
        }

        public static ToolExecutionSessionEnterResult Entered(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            return new ToolExecutionSessionEnterResult(true, lease, string.Empty, null);
        }

        public static ToolExecutionSessionEnterResult Busy(string runningToolName, int? runningToolElapsedSeconds = null)
        {
            return new ToolExecutionSessionEnterResult(false, null, runningToolName, runningToolElapsedSeconds);
        }
    }
}
