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
        private readonly IMainThreadEntryWatchdog _watchdog;

        internal UnityCliLoopToolExecutionService(
            IEditorRuntimeStatePort editorRuntimeStatePort,
            ToolExecutionSession executionSession = null,
            IMainThreadEntryWatchdog watchdog = null)
        {
            Debug.Assert(editorRuntimeStatePort != null, "editorRuntimeStatePort must not be null");

            _editorRuntimeStatePort = editorRuntimeStatePort ?? throw new ArgumentNullException(nameof(editorRuntimeStatePort));
            _executionSession = executionSession ?? new ToolExecutionSession();
            _watchdog = watchdog ?? NoMainThreadEntryWatchdog.Instance;
        }

        internal async Task<UnityCliLoopToolResponse> ExecuteToolAsync(
            UnityCliLoopToolRegistry registry,
            string toolName,
            JToken paramsToken,
            CancellationToken ct,
            bool cancellableByUser = true)
        {
            Debug.Assert(registry != null, "registry must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            ct.ThrowIfCancellationRequested();

            ToolExecutionSessionBeginResult beginResult = _executionSession.Begin(registry, toolName, ct, cancellableByUser);
            if (!beginResult.IsEntered)
            {
                throw CreateBusyException(
                    beginResult.RunningToolName,
                    toolName,
                    _editorRuntimeStatePort,
                    beginResult.RunningToolElapsedSeconds,
                    beginResult.RunningToolPhase?.ToString());
            }

            ToolExecutionLease lease = beginResult.Lease;
            CancellationToken executionToken = lease.ExecutionToken;
            try
            {
                // Why an async-flow observer instead of marking phases here: the request can also
                // wait for the main thread inside the tool, after its work is done (for example
                // execute-dynamic-code reading the pause state), and only the switch itself sees that.
                // The value set here flows into the tool and is restored for this method's caller.
                MainThreadWaitObservation.SetCurrent(new ToolExecutionLeaseWaitObserver(lease));
                // Why never disposed: the guard may still be between its stop check and its next poll when
                // the switch returns, and Task.Delay on a disposed token throws; a CTS without a timer holds
                // nothing to leak (same reasoning as the lease's explicit cancellation source).
                CancellationTokenSource watchdogStop = new CancellationTokenSource();
                // Not awaited: Cancel() below ends it, and awaiting it on the main thread would add a hop.
                // GuardAsync never throws, so the discarded task cannot hide a failure.
                _ = _watchdog.GuardAsync(lease, watchdogStop.Token);
                try
                {
                    await MainThreadSwitcher.SwitchToMainThread(executionToken);
                }
                finally
                {
                    watchdogStop.Cancel();
                }

                // CAS against the watchdog: whoever moves the state first decides whether the tool runs.
                // Why the state and not the token: a claimer that won the CAS may not have cancelled the
                // token yet, and the main thread can resume the switch from the Editor queue in that window.
                if (!lease.MarkToolEntered())
                {
                    throw CreateNotEnteredException(lease, toolName, ct);
                }

                executionToken.ThrowIfCancellationRequested();
                UnityCliLoopEditorStateGuard.Validate(toolName, _editorRuntimeStatePort);

                UnityCliLoopToolResponse response = await beginResult.Tool.ExecuteAsync(paramsToken, executionToken).ConfigureAwait(false);
                if (response == null)
                {
                    throw new InvalidOperationException($"Tool returned null response: {toolName}");
                }

                return response;
            }
            catch (OperationCanceledException) when (lease.WasCancelledForBlockedMainThread && !ct.IsCancellationRequested)
            {
                throw new UnityCliLoopMainThreadBlockedException(toolName, lease.BlockedMainThreadStallSeconds);
            }
            // Why convert: an explicit uloop cancel must reach the waiting CLI as its own error, while a
            // disconnect (the request token) still drops the connection because nobody is left to answer.
            catch (OperationCanceledException) when (lease.WasCancelledByUser && !ct.IsCancellationRequested)
            {
                throw new UnityCliLoopToolCancelledException(toolName);
            }
            finally
            {
                lease.Dispose();
            }
        }

        // Builds the outcome for a lease that the watchdog or uloop cancel claimed before the tool
        // entered; both claimers may still be about to cancel the token, so the state decides.
        private static Exception CreateNotEnteredException(ToolExecutionLease lease, string toolName, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                // Client gone: the plain cancellation drops the connection, as for any disconnect.
                return new OperationCanceledException(ct);
            }

            if (lease.WasCancelledForBlockedMainThread)
            {
                return new UnityCliLoopMainThreadBlockedException(toolName, lease.BlockedMainThreadStallSeconds);
            }

            return new UnityCliLoopToolCancelledException(toolName);
        }

        // Why convert here: Infrastructure cannot see the Domain result type, so the outcome leaves
        // in an Application type, the same way GetExecutionStatus does.
        internal UnityCliLoopCancelRequestResult CancelRunningTool()
        {
            return UnityCliLoopCancelRequestResult.From(_executionSession.CancelRunning());
        }

        // Why convert here: Infrastructure cannot see the Domain snapshot type, so the phase leaves
        // as its wire name, the same way the busy exception below carries it.
        internal UnityCliLoopExecutionStatus GetExecutionStatus()
        {
            ToolExecutionSessionSnapshot snapshot = _executionSession.GetSnapshot();
            if (!snapshot.IsBusy)
            {
                return UnityCliLoopExecutionStatus.Idle();
            }

            return UnityCliLoopExecutionStatus.Busy(
                snapshot.RunningToolName,
                snapshot.RunningToolElapsedSeconds,
                snapshot.RunningToolPhase.ToString());
        }

        internal static UnityCliLoopToolBusyException CreateBusyException(
            string runningToolName,
            string requestedToolName,
            IEditorRuntimeStatePort editorRuntimeStatePort,
            int? runningToolElapsedSeconds = null,
            string runningToolPhase = null)
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
                    runningToolElapsedSeconds,
                    runningToolPhase);
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
                    runningToolElapsedSeconds: runningToolElapsedSeconds,
                    runningToolPhase: runningToolPhase);
            }

            return new UnityCliLoopToolBusyException(
                runningToolName,
                requestedToolName,
                runningToolElapsedSeconds: runningToolElapsedSeconds,
                runningToolPhase: runningToolPhase);
        }
    }

    /// <summary>
    /// Forwards the main-thread waits of one request to the lease it holds, so a busy rejection can
    /// tell that the holder is only waiting for the Editor main thread.
    /// </summary>
    internal sealed class ToolExecutionLeaseWaitObserver : IMainThreadWaitObserver
    {
        private readonly ToolExecutionLease _lease;

        internal ToolExecutionLeaseWaitObserver(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            _lease = lease;
        }

        public void OnWaitStarted()
        {
            _lease.MarkMainThreadWaitStarted();
        }

        public void OnWaitEnded()
        {
            _lease.MarkMainThreadWaitEnded();
        }
    }
}
