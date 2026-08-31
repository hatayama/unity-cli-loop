using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Tracks active server recovery tasks and applies retry backoff for transient recovery failures.
    /// </summary>
    internal sealed class UnityCliLoopServerRecoveryTrackingService
    {
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly Func<int, CancellationToken, Task> _waitBeforeRecoveryRetryAsync;
        private Task _currentRecoveryTask;
        private bool _isCurrentRecoveryTracked;

        internal UnityCliLoopServerRecoveryTrackingService(
            ISessionFlagsRepository sessionFlagsRepository,
            Func<int, CancellationToken, Task> waitBeforeRecoveryRetryAsync = null)
        {
            Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");

            _sessionFlagsRepository = sessionFlagsRepository ?? throw new ArgumentNullException(nameof(sessionFlagsRepository));
            _waitBeforeRecoveryRetryAsync = waitBeforeRecoveryRetryAsync ?? TimerDelay.Wait;
        }

        /// <summary>
        /// Current recovery task. Completion signals that recovery activity ended; failures are carried by
        /// logs and session state so UI awaiters do not need try-catch around this task.
        /// </summary>
        internal Task RecoveryTask => _currentRecoveryTask;

        internal Task ScheduleStartupRecovery(
            Action<Action> scheduleDelayCall,
            Func<Task> restoreServerState)
        {
            Debug.Assert(scheduleDelayCall != null, "scheduleDelayCall must not be null");
            Debug.Assert(restoreServerState != null, "restoreServerState must not be null");

            TaskCompletionSource<bool> scheduledRecoveryCompletionSource = null;
            Task scheduledRecoveryTask = ScheduleRecoveryTask(() =>
            {
                scheduledRecoveryCompletionSource = new TaskCompletionSource<bool>();
                return scheduledRecoveryCompletionSource.Task;
            }, isTrackedRecovery: false);
            if (scheduledRecoveryCompletionSource == null)
            {
                return scheduledRecoveryTask;
            }

            scheduleDelayCall(() =>
            {
                Task restoreTask;
                try
                {
                    restoreTask = restoreServerState();
                }
                catch (Exception ex)
                {
                    CompleteScheduledStartupRecovery(Task.FromException(ex), scheduledRecoveryCompletionSource);
                    return;
                }

                if (restoreTask.IsCompleted)
                {
                    CompleteScheduledStartupRecovery(restoreTask, scheduledRecoveryCompletionSource);
                    return;
                }

                _ = restoreTask.ContinueWith(task =>
                {
                    CompleteScheduledStartupRecovery(task, scheduledRecoveryCompletionSource);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());
            });

            return scheduledRecoveryTask;
        }

        private void CompleteScheduledStartupRecovery(
            Task restoreTask,
            TaskCompletionSource<bool> scheduledRecoveryCompletionSource)
        {
            if (ReferenceEquals(_currentRecoveryTask, scheduledRecoveryCompletionSource.Task))
            {
                _currentRecoveryTask = null;
                _isCurrentRecoveryTracked = false;
            }

            if (restoreTask.IsCanceled)
            {
                scheduledRecoveryCompletionSource.SetResult(true);
                return;
            }

            if (restoreTask.IsFaulted)
            {
                string message = $"Failed to restore server: {restoreTask.Exception?.GetBaseException().Message}";
                // Why: startup restore failures are awaited by UI-facing paths and must be visible
                // in normal user builds where VibeLogger is compiled out without ULOOP_DEBUG.
                Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] {message}");
                VibeLogger.LogError("server_startup_restore_failed",
                    message);
                scheduledRecoveryCompletionSource.SetResult(true);
                return;
            }

            scheduledRecoveryCompletionSource.SetResult(true);
        }

        internal Task ScheduleTrackedRecovery(Func<Task> recoveryAction)
        {
            Debug.Assert(recoveryAction != null, "recoveryAction must not be null");

            Task trackedRecoveryTask = null;
            Task recoveryTask = ScheduleRecoveryTask(() =>
            {
                trackedRecoveryTask = ExecuteTrackedRecoveryAsync(recoveryAction);
                return CreateNonFaultingRecoveryTask(trackedRecoveryTask);
            }, isTrackedRecovery: true);
            if (trackedRecoveryTask != null)
            {
                _ = ClearTrackedRecoveryWhenCompleteAsync(recoveryTask);
            }

            return recoveryTask;
        }

        private Task CreateNonFaultingRecoveryTask(Task recoveryTask)
        {
            Debug.Assert(recoveryTask != null, "recoveryTask must not be null");

            return recoveryTask.ContinueWith(task =>
            {
                if (!task.IsFaulted)
                {
                    return;
                }

                // Why: the public RecoveryTask is an activity-finished signal; the underlying
                // recovery failure is already logged and must be observed to avoid finalizer noise.
                _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private Task ScheduleRecoveryTask(Func<Task> createRecoveryTask, bool isTrackedRecovery)
        {
            Debug.Assert(createRecoveryTask != null, "createRecoveryTask must not be null");

            Task activeRecoveryTask = GetActiveRecoveryTask();
            if (activeRecoveryTask != null && ShouldJoinActiveRecovery(isTrackedRecovery))
            {
                // Why: startup recovery has no domain-reload bookkeeping, and duplicate tracked
                // recovery loops recheck current server state, so these triggers add no new work.
                return activeRecoveryTask;
            }

            Task recoveryTask = createRecoveryTask();
            _currentRecoveryTask = recoveryTask;
            _isCurrentRecoveryTracked = isTrackedRecovery;
            return recoveryTask;
        }

        private bool ShouldJoinActiveRecovery(bool isTrackedRecovery)
        {
            if (!isTrackedRecovery)
            {
                // Why: startup restore is an idempotent IfNeeded recovery path, so any active
                // recovery task already owns the same server-state restoration work.
                return true;
            }

            // Why: after-domain-reload tracked recovery carries CompleteDomainReload bookkeeping,
            // so it must supersede a startup placeholder instead of joining it.
            return _isCurrentRecoveryTracked;
        }

        private Task GetActiveRecoveryTask()
        {
            Task currentRecoveryTask = _currentRecoveryTask;
            if (currentRecoveryTask == null || currentRecoveryTask.IsCompleted)
            {
                return null;
            }

            return currentRecoveryTask;
        }

        /// <summary>
        /// Runs a recovery action, retrying with backoff so one transient failure
        /// (e.g. readiness timeout during a heavy import) does not leave the server
        /// down until the next domain reload.
        /// </summary>
        internal async Task ExecuteTrackedRecoveryAsync(Func<Task> recoveryAction)
        {
            Debug.Assert(recoveryAction != null, "recoveryAction must not be null");

            int failedAttemptCount = 0;
            while (true)
            {
                try
                {
                    await recoveryAction();
                    return;
                }
                catch (Exception ex)
                {
                    if (failedAttemptCount >= UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS.Length)
                    {
                        string message = $"Unity CLI Loop server recovery failed before the bridge became ready. {ex.GetBaseException().Message}";
                        // Why: the thrown exception ends in an unobserved task and VibeLogger is
                        // compiled out without ULOOP_DEBUG, so without this console entry an
                        // unrecoverable server (uloop unreachable) would be completely silent.
                        Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] {message}");
                        VibeLogger.LogError(
                            "server_recovery_failed",
                            message);
                        _sessionFlagsRepository.ClearServerSession();
                        throw new InvalidOperationException(message, ex);
                    }

                    int delayMilliseconds = UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS[failedAttemptCount];
                    failedAttemptCount++;
                    VibeLogger.LogWarning(
                        "server_recovery_retry_scheduled",
                        $"Recovery attempt {failedAttemptCount} failed; retrying in {delayMilliseconds}ms. {ex.GetBaseException().Message}");
                    await _waitBeforeRecoveryRetryAsync(delayMilliseconds, CancellationToken.None);

                    // Why: an explicit Stop Server issued during the backoff must win over
                    // automatic recovery, otherwise the retry would silently restart the server.
                    if (_sessionFlagsRepository.GetIsServerManuallyStopped())
                    {
                        VibeLogger.LogInfo(
                            "server_recovery_retry_abandoned",
                            "Recovery retry abandoned because the server was manually stopped.");
                        return;
                    }
                }
            }
        }

        private async Task ClearTrackedRecoveryWhenCompleteAsync(Task recoveryTask)
        {
            Debug.Assert(recoveryTask != null, "recoveryTask must not be null");

            try
            {
                await recoveryTask;
            }
            finally
            {
                if (ReferenceEquals(_currentRecoveryTask, recoveryTask))
                {
                    _currentRecoveryTask = null;
                    _isCurrentRecoveryTracked = false;
                }
            }
        }
    }
}
