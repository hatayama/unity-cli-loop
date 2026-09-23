using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Owns the thread-safe single-flight state for tool execution sessions.
    /// </summary>
    internal sealed class ToolExecutionSession
    {
        private const string UnknownToolName = "unknown";
        // Shorter than the CLI's 10-second busy retry window, so a command that starts waiting
        // when the stuck request is first seen still gets in before its retries run out.
        internal const int CancelledLeaseGraceSeconds = 5;
        private static readonly long CancelledLeaseGraceTicks = CancelledLeaseGraceSeconds * Stopwatch.Frequency;

        private readonly object _executionStateLock = new();
        private readonly Func<long> _timestampProvider;
        private readonly HashSet<ToolExecutionLease> _activeLeases = new();
        private string _runningToolName;
        private long _runningStartedTimestamp;

        internal ToolExecutionSession(Func<long> timestampProvider = null)
        {
            _timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        }

        internal ToolExecutionSessionBeginResult Begin(UnityCliLoopToolRegistry registry, string toolName, CancellationToken ct)
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

            ToolExecutionSessionEnterResult enterResult = TryEnter(toolName, ct);
            if (!enterResult.IsEntered)
            {
                return ToolExecutionSessionBeginResult.Busy(
                    enterResult.RunningToolName,
                    enterResult.RunningToolElapsedSeconds);
            }

            return ToolExecutionSessionBeginResult.Entered(tool, enterResult.Lease);
        }

        internal ToolExecutionSessionEnterResult TryEnter(string requestedToolName, CancellationToken ct = default)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");

            ToolExecutionSessionEnterResult result;
            List<RevokedLeaseRecord> revokedLeases;
            lock (_executionStateLock)
            {
                revokedLeases = RevokeCancelledLeasesPastGraceInsideLock(requestedToolName);
                result = TryEnterInsideLock(requestedToolName, ct);
            }

            // Why not log inside the lock: the log call writes a file, and every other request
            // waits on this lock.
            LogRevokedLeases(revokedLeases);
            return result;
        }

        private ToolExecutionSessionEnterResult TryEnterInsideLock(string requestedToolName, CancellationToken ct)
        {
            if (_activeLeases.Count == 0)
            {
                _runningToolName = requestedToolName;
                _runningStartedTimestamp = _timestampProvider();
                return ToolExecutionSessionEnterResult.Entered(IssueLeaseInsideLock(requestedToolName, ct));
            }

            if (CanShareExecutionSlot(_runningToolName, requestedToolName))
            {
                // Why not refresh the start timestamp: shared-slot re-entry is the same
                // flight, so elapsed must keep measuring from the first Begin.
                return ToolExecutionSessionEnterResult.Entered(IssueLeaseInsideLock(requestedToolName, ct));
            }

            return ToolExecutionSessionEnterResult.Busy(
                GetRunningToolNameInsideLock(),
                GetRunningToolElapsedSecondsInsideLock());
        }

        // A request whose client has gone away can stay stuck on an await that ignores its
        // token, and its lease would then keep every other tool busy until the domain ends.
        // Only execute-dynamic-code leases are revoked: run-tests keeps cleaning up Play Mode for
        // up to 10 seconds after a cancel, so revoking other tools would let a command run in the
        // middle of that. The cancellation is timed from when a waiting request first sees it,
        // not from the cancel itself, because ct.Register callbacks would have to take this lock
        // and could deadlock against a Dispose.
        private List<RevokedLeaseRecord> RevokeCancelledLeasesPastGraceInsideLock(string requestedToolName)
        {
            if (_activeLeases.Count == 0 || CanShareExecutionSlot(_runningToolName, requestedToolName))
            {
                return null;
            }

            long now = _timestampProvider();
            List<RevokedLeaseRecord> revokedLeases = null;
            foreach (ToolExecutionLease lease in _activeLeases)
            {
                if (!lease.ObserveCancellationPastGrace(now, CancelledLeaseGraceTicks))
                {
                    continue;
                }

                revokedLeases ??= new List<RevokedLeaseRecord>();
                revokedLeases.Add(new RevokedLeaseRecord(lease, now));
            }

            if (revokedLeases == null)
            {
                return null;
            }

            foreach (RevokedLeaseRecord revokedLease in revokedLeases)
            {
                _activeLeases.Remove(revokedLease.Lease);
            }

            if (_activeLeases.Count == 0)
            {
                _runningToolName = null;
                _runningStartedTimestamp = 0;
            }

            return revokedLeases;
        }

        private static void LogRevokedLeases(List<RevokedLeaseRecord> revokedLeases)
        {
            if (revokedLeases == null)
            {
                return;
            }

            foreach (RevokedLeaseRecord revokedLease in revokedLeases)
            {
                VibeLogger.LogWarning(
                    "tool_execution_lease_revoked",
                    "Released the execution slot held by a cancelled request that did not finish within the grace period",
                    new
                    {
                        toolName = revokedLease.Lease.ToolName,
                        secondsSinceStart = revokedLease.SecondsSinceStart,
                        secondsSinceCancellationObserved = revokedLease.SecondsSinceCancellationObserved
                    },
                    includeStackTrace: false);
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

        private ToolExecutionLease IssueLeaseInsideLock(string toolName, CancellationToken ct)
        {
            ToolExecutionLease lease = new ToolExecutionLease(this, toolName, ct, _timestampProvider());
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
        private readonly CancellationToken _requestCancellation;
        private long? _cancellationObservedTimestamp;

        internal ToolExecutionLease(
            ToolExecutionSession session,
            string toolName,
            CancellationToken requestCancellation,
            long issuedTimestamp)
        {
            Debug.Assert(session != null, "session must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            _session = session;
            ToolName = toolName;
            _requestCancellation = requestCancellation;
            IssuedTimestamp = issuedTimestamp;
        }

        internal string ToolName { get; }

        internal long IssuedTimestamp { get; }

        internal long CancellationObservedTimestamp => _cancellationObservedTimestamp ?? 0;

        public void Dispose()
        {
            _session.Release(this);
        }

        // Records the first time a waiting request sees this lease's request cancelled, and
        // reports whether the lease has stayed cancelled for the whole grace period since then.
        // Called only under the session lock.
        internal bool ObserveCancellationPastGrace(long now, long graceTicks)
        {
            if (ToolName != UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE)
            {
                return false;
            }

            if (!_requestCancellation.IsCancellationRequested)
            {
                return false;
            }

            if (_cancellationObservedTimestamp == null)
            {
                _cancellationObservedTimestamp = now;
                return false;
            }

            return now - _cancellationObservedTimestamp.Value >= graceTicks;
        }
    }

    /// <summary>
    /// Captures what the revocation log needs about a lease the session took back.
    /// </summary>
    internal readonly struct RevokedLeaseRecord
    {
        public readonly ToolExecutionLease Lease;
        public readonly int SecondsSinceStart;
        public readonly int SecondsSinceCancellationObserved;

        public RevokedLeaseRecord(ToolExecutionLease lease, long revokedTimestamp)
        {
            Debug.Assert(lease != null, "lease must not be null");

            Lease = lease;
            SecondsSinceStart = (int)((revokedTimestamp - lease.IssuedTimestamp) / Stopwatch.Frequency);
            SecondsSinceCancellationObserved =
                (int)((revokedTimestamp - lease.CancellationObservedTimestamp) / Stopwatch.Frequency);
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
