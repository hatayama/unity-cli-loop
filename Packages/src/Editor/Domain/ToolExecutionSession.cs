using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        internal ToolExecutionSessionBeginResult Begin(
            UnityCliLoopToolRegistry registry,
            string toolName,
            CancellationToken ct,
            bool cancellableByUser = true)
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

            ToolExecutionSessionEnterResult enterResult = TryEnter(toolName, ct, cancellableByUser);
            if (!enterResult.IsEntered)
            {
                return ToolExecutionSessionBeginResult.Busy(
                    enterResult.RunningToolName,
                    enterResult.RunningToolElapsedSeconds,
                    enterResult.RunningToolPhase);
            }

            return ToolExecutionSessionBeginResult.Entered(tool, enterResult.Lease);
        }

        internal ToolExecutionSessionEnterResult TryEnter(
            string requestedToolName,
            CancellationToken ct = default,
            bool cancellableByUser = true)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");

            ToolExecutionSessionEnterResult result;
            List<RevokedLeaseRecord> revokedLeases;
            lock (_executionStateLock)
            {
                revokedLeases = RevokeCancelledLeasesPastGraceInsideLock(requestedToolName);
                result = TryEnterInsideLock(requestedToolName, ct, cancellableByUser);
            }

            // Why not log inside the lock: the log call writes a file, and every other request
            // waits on this lock.
            LogRevokedLeases(revokedLeases);
            return result;
        }

        private ToolExecutionSessionEnterResult TryEnterInsideLock(
            string requestedToolName,
            CancellationToken ct,
            bool cancellableByUser)
        {
            if (_activeLeases.Count == 0)
            {
                _runningToolName = requestedToolName;
                _runningStartedTimestamp = _timestampProvider();
                return ToolExecutionSessionEnterResult.Entered(IssueLeaseInsideLock(requestedToolName, ct, cancellableByUser));
            }

            if (CanShareExecutionSlot(_runningToolName, requestedToolName))
            {
                // Why not refresh the start timestamp: shared-slot re-entry is the same
                // flight, so elapsed must keep measuring from the first Begin.
                return ToolExecutionSessionEnterResult.Entered(IssueLeaseInsideLock(requestedToolName, ct, cancellableByUser));
            }

            return ToolExecutionSessionEnterResult.Busy(
                GetRunningToolNameInsideLock(),
                GetRunningToolElapsedSecondsInsideLock(),
                GetRunningToolPhaseInsideLock());
        }

        // Why every lease must wait: in a shared slot, one lease that is not waiting means tool
        // code may be running, and calling the whole slot "waiting" would hide that.
        private ToolExecutionPhase GetRunningToolPhaseInsideLock()
        {
            foreach (ToolExecutionLease lease in _activeLeases)
            {
                if (!lease.IsWaitingForMainThread)
                {
                    return ToolExecutionPhase.Executing;
                }
            }

            return ToolExecutionPhase.WaitingForMainThread;
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

        // Reads the slot for status reporting without entering it. Why not TryEnter: it takes the
        // slot, and its revocation pass starts the grace timer of a cancelled lease, so asking for
        // the status would change when that lease is taken back.
        internal ToolExecutionSessionSnapshot GetSnapshot()
        {
            lock (_executionStateLock)
            {
                if (_activeLeases.Count == 0)
                {
                    return ToolExecutionSessionSnapshot.Idle();
                }

                return ToolExecutionSessionSnapshot.Busy(
                    GetRunningToolNameInsideLock(),
                    GetRunningToolElapsedSecondsInsideLock(),
                    GetRunningToolPhaseInsideLock());
            }
        }

        // Fires the explicit cancellation of every lease whose request may be cancelled by uloop
        // cancel. Why the leases are copied under the lock and cancelled outside it: Cancel() runs
        // the cancelled requests' continuations and their lease Release on this thread, and Release
        // takes this lock.
        internal ToolCancelRequestResult CancelRunning()
        {
            List<ToolExecutionLease> targets;
            ToolCancelRequestResult result;
            lock (_executionStateLock)
            {
                if (_activeLeases.Count == 0)
                {
                    return ToolCancelRequestResult.NothingRunning();
                }

                string toolName = GetRunningToolNameInsideLock();
                int elapsedSeconds = GetRunningToolElapsedSecondsInsideLock();
                targets = _activeLeases.Where(lease => lease.CancellableByUser).ToList();
                if (targets.Count == 0)
                {
                    return ToolCancelRequestResult.NotCancellable(toolName, elapsedSeconds);
                }

                result = ToolCancelRequestResult.Cancelled(toolName, elapsedSeconds);
            }

            foreach (ToolExecutionLease lease in targets)
            {
                lease.CancelByUser();
            }

            return result;
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

        private ToolExecutionLease IssueLeaseInsideLock(string toolName, CancellationToken ct, bool cancellableByUser)
        {
            ToolExecutionLease lease = new ToolExecutionLease(this, toolName, ct, _timestampProvider(), cancellableByUser);
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
        // Why never disposed: it has no timer, so it holds nothing to leak, and leaving it alive
        // lets a uloop cancel that races the lease's Release call Cancel() without throwing.
        private readonly CancellationTokenSource _explicitCancellation = new();
        private readonly CancellationTokenSource _executionCancellation;
        private long? _cancellationObservedTimestamp;
        // A count, not a flag: one request can have several switches queued at once (WhenAll).
        private int _pendingMainThreadWaits;
        // Why a state and not a flag: the watchdog and the main thread race for the moment the tool
        // enters; a CAS from Waiting decides who won, so a tool that started is never reported as blocked.
        private const int EntryStateWaiting = 0;
        private const int EntryStateEntered = 1;
        private const int EntryStateBlockedMainThread = 2;
        private const int EntryStateCancelledByUserBeforeEntry = 3;
        private int _entryState = EntryStateWaiting;
        private double _blockedMainThreadStallSeconds;

        internal ToolExecutionLease(
            ToolExecutionSession session,
            string toolName,
            CancellationToken requestCancellation,
            long issuedTimestamp,
            bool cancellableByUser)
        {
            Debug.Assert(session != null, "session must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            _session = session;
            ToolName = toolName;
            _executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellation,
                _explicitCancellation.Token);
            IssuedTimestamp = issuedTimestamp;
            CancellableByUser = cancellableByUser;
        }

        // False for calls that did not come from the CLI: their callers manage cancellation with
        // their own token, and uloop cancel only stops what a CLI asked for.
        internal bool CancellableByUser { get; }

        // Fires when the request is cancelled (client disconnect, server stop) or uloop cancel
        // fires this lease. Tools run with this token instead of the raw request token.
        internal CancellationToken ExecutionToken => _executionCancellation.Token;

        // A blocked-main-thread cancel uses the same token but is not a user cancel. The state is read
        // too because a uloop cancel can have claimed the lease before its Cancel() has run.
        internal bool WasCancelledByUser
        {
            get
            {
                int state = Volatile.Read(ref _entryState);
                if (state == EntryStateBlockedMainThread)
                {
                    return false;
                }

                return state == EntryStateCancelledByUserBeforeEntry || _explicitCancellation.IsCancellationRequested;
            }
        }

        internal bool WasCancelledForBlockedMainThread => Volatile.Read(ref _entryState) == EntryStateBlockedMainThread;

        internal double BlockedMainThreadStallSeconds => Volatile.Read(ref _blockedMainThreadStallSeconds);

        internal string ToolName { get; }

        internal long IssuedTimestamp { get; }

        internal long CancellationObservedTimestamp => _cancellationObservedTimestamp ?? 0;

        internal bool IsWaitingForMainThread => Volatile.Read(ref _pendingMainThreadWaits) > 0;

        // Called from the main-thread switch path of this lease's request, which may run on any
        // thread and must not throw, so this only updates the counter.
        internal void MarkMainThreadWaitStarted()
        {
            Interlocked.Increment(ref _pendingMainThreadWaits);
        }

        internal void MarkMainThreadWaitEnded()
        {
            Interlocked.Decrement(ref _pendingMainThreadWaits);
        }

        // Runs other requests' continuations and this lease's Release synchronously on the
        // calling thread (MainThreadSwitcher registers on the token), so never call it under the
        // session lock or inside an activity hold.
        internal void CancelByUser()
        {
            Interlocked.CompareExchange(ref _entryState, EntryStateCancelledByUserBeforeEntry, EntryStateWaiting);
            _explicitCancellation.Cancel();
        }

        // Called by the execution service right after the first main-thread switch returns. False means
        // the lease was claimed first (blocked main thread or uloop cancel) and the tool must not run;
        // the caller decides by WasCancelledForBlockedMainThread / WasCancelledByUser, not by the token,
        // which the claimer may not have cancelled yet.
        internal bool MarkToolEntered()
        {
            return Interlocked.CompareExchange(ref _entryState, EntryStateEntered, EntryStateWaiting) == EntryStateWaiting;
        }

        // The decision half of TryCancelForBlockedMainThread: records the stall and claims the lease.
        // Returns false when the tool already entered or uloop cancel already claimed the lease.
        // Split from the cancel so the moment between the claim and the cancel exists as a unit.
        internal bool TryClaimBlockedMainThread(double stallSeconds)
        {
            Debug.Assert(stallSeconds >= 0, "stallSeconds must not be negative");

            // Written before the CAS: the CAS is a full fence, so whoever sees the claim sees the value.
            Volatile.Write(ref _blockedMainThreadStallSeconds, stallSeconds);
            return Interlocked.CompareExchange(ref _entryState, EntryStateBlockedMainThread, EntryStateWaiting) == EntryStateWaiting;
        }

        // Runs other requests' continuations synchronously on the calling thread (same note as
        // CancelByUser), so never call it under the session lock or inside an activity hold.
        internal bool TryCancelForBlockedMainThread(double stallSeconds)
        {
            if (!TryClaimBlockedMainThread(stallSeconds))
            {
                return false;
            }

            _explicitCancellation.Cancel();
            return true;
        }

        public void Dispose()
        {
            _session.Release(this);
            _executionCancellation.Dispose();
        }

        // Records the first time a waiting request sees this lease cancelled, by its client or by
        // uloop cancel, and reports whether the lease has stayed cancelled for the whole grace
        // period since then. Called only under the session lock.
        internal bool ObserveCancellationPastGrace(long now, long graceTicks)
        {
            if (ToolName != UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE)
            {
                return false;
            }

            if (!ExecutionToken.IsCancellationRequested)
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
        public readonly ToolExecutionPhase? RunningToolPhase;

        private ToolExecutionSessionBeginResult(
            bool isEntered,
            IUnityCliLoopTool tool,
            ToolExecutionLease lease,
            string runningToolName,
            int? runningToolElapsedSeconds,
            ToolExecutionPhase? runningToolPhase)
        {
            Debug.Assert(isEntered == (tool != null), "entered sessions must carry a tool");
            Debug.Assert(isEntered == (lease != null), "entered sessions must carry a lease");
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            Tool = tool;
            Lease = lease;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
            RunningToolPhase = runningToolPhase;
        }

        public static ToolExecutionSessionBeginResult Entered(IUnityCliLoopTool tool, ToolExecutionLease lease)
        {
            Debug.Assert(tool != null, "tool must not be null");
            Debug.Assert(lease != null, "lease must not be null");

            return new ToolExecutionSessionBeginResult(true, tool, lease, string.Empty, null, null);
        }

        public static ToolExecutionSessionBeginResult Busy(
            string runningToolName,
            int? runningToolElapsedSeconds,
            ToolExecutionPhase? runningToolPhase)
        {
            return new ToolExecutionSessionBeginResult(
                false,
                null,
                null,
                runningToolName,
                runningToolElapsedSeconds,
                runningToolPhase);
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
        public readonly ToolExecutionPhase? RunningToolPhase;

        private ToolExecutionSessionEnterResult(
            bool isEntered,
            ToolExecutionLease lease,
            string runningToolName,
            int? runningToolElapsedSeconds,
            ToolExecutionPhase? runningToolPhase)
        {
            Debug.Assert(isEntered == (lease != null), "entered sessions must carry a lease");
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            Lease = lease;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
            RunningToolPhase = runningToolPhase;
        }

        public static ToolExecutionSessionEnterResult Entered(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            return new ToolExecutionSessionEnterResult(true, lease, string.Empty, null, null);
        }

        public static ToolExecutionSessionEnterResult Busy(
            string runningToolName,
            int? runningToolElapsedSeconds,
            ToolExecutionPhase? runningToolPhase)
        {
            return new ToolExecutionSessionEnterResult(false, null, runningToolName, runningToolElapsedSeconds, runningToolPhase);
        }
    }

    /// <summary>
    /// Read-only view of the execution slot for status reporting, taken without entering the slot.
    /// </summary>
    internal readonly struct ToolExecutionSessionSnapshot
    {
        public readonly bool IsBusy;
        public readonly string RunningToolName;
        public readonly int RunningToolElapsedSeconds;
        public readonly ToolExecutionPhase RunningToolPhase;

        private ToolExecutionSessionSnapshot(
            bool isBusy,
            string runningToolName,
            int runningToolElapsedSeconds,
            ToolExecutionPhase runningToolPhase)
        {
            IsBusy = isBusy;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
            RunningToolPhase = runningToolPhase;
        }

        public static ToolExecutionSessionSnapshot Idle()
        {
            return new ToolExecutionSessionSnapshot(false, null, 0, ToolExecutionPhase.Executing);
        }

        public static ToolExecutionSessionSnapshot Busy(
            string runningToolName,
            int runningToolElapsedSeconds,
            ToolExecutionPhase runningToolPhase)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace");

            return new ToolExecutionSessionSnapshot(true, runningToolName, runningToolElapsedSeconds, runningToolPhase);
        }
    }

    /// <summary>
    /// Tells a busy rejection whether the slot holder may be running tool code or is only waiting
    /// for the Editor main thread. Member names are the wire values of runningToolPhase.
    /// </summary>
    internal enum ToolExecutionPhase
    {
        Executing,
        WaitingForMainThread
    }
}
