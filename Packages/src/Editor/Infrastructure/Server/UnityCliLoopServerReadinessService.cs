using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Verifies that the project IPC bridge is ready before the server is published as started.
    /// </summary>
    internal sealed class UnityCliLoopServerReadinessService
    {
        private const int READINESS_IDLE_POLL_INTERVAL_MS = 250;

        private readonly UnityCliLoopServerLifecycleRegistryService _serverLifecycleRegistry;
        private readonly IUnityCliLoopServerReadinessProbe _readinessProbe;
        private readonly Func<bool> _isReadinessProbeBlocked;
        private readonly Func<int, CancellationToken, Task> _waitBeforeReadinessRetryAsync;
        private readonly int _readinessIdleTimeoutMilliseconds;

        internal UnityCliLoopServerReadinessService(
            UnityCliLoopServerLifecycleRegistryService serverLifecycleRegistry,
            IUnityCliLoopServerReadinessProbe readinessProbe,
            Func<bool> isReadinessProbeBlocked = null,
            Func<int, CancellationToken, Task> waitBeforeReadinessRetryAsync = null,
            int readinessIdleTimeoutMilliseconds = UnityCliLoopServerConfig.READINESS_PROBE_TIMEOUT_MS)
        {
            Debug.Assert(serverLifecycleRegistry != null, "serverLifecycleRegistry must not be null");
            Debug.Assert(readinessProbe != null, "readinessProbe must not be null");
            Debug.Assert(readinessIdleTimeoutMilliseconds > 0, "readinessIdleTimeoutMilliseconds must be positive");

            _serverLifecycleRegistry = serverLifecycleRegistry ?? throw new ArgumentNullException(nameof(serverLifecycleRegistry));
            _readinessProbe = readinessProbe ?? throw new ArgumentNullException(nameof(readinessProbe));
            _isReadinessProbeBlocked = isReadinessProbeBlocked ?? IsEditorBusyForReadinessProbe;
            _waitBeforeReadinessRetryAsync = waitBeforeReadinessRetryAsync ?? TimerDelay.Wait;
            _readinessIdleTimeoutMilliseconds = readinessIdleTimeoutMilliseconds;
        }

        private static bool IsEditorBusyForReadinessProbe()
        {
            return EditorApplication.isCompiling ||
                   EditorApplication.isUpdating ||
                   DomainReloadStateRegistry.IsDomainReloadInProgress();
        }

        internal async Task MarkServerReadyAsync(
            string reason,
            CancellationToken ct)
        {
            try
            {
                await WaitForEditorIdleBeforeReadinessProbeAsync(
                    ct,
                    _readinessIdleTimeoutMilliseconds);
                await ProbeReadinessWithTimeoutAsync(ct, UnityCliLoopServerConfig.READINESS_PROBE_TIMEOUT_MS);
            }
            catch (Exception ex)
            {
                string message = $"Unity CLI Loop server bound its project IPC endpoint, but readiness probe failed during {reason}. {ex.GetBaseException().Message}";
                throw new InvalidOperationException(message, ex);
            }

            _serverLifecycleRegistry.PublishServerStarted();
        }

        /// <summary>
        /// Waits until Unity is ready for a main-thread IPC readiness probe.
        /// </summary>
        private async Task WaitForEditorIdleBeforeReadinessProbeAsync(
            CancellationToken ct,
            int timeoutMilliseconds)
        {
            Debug.Assert(timeoutMilliseconds > 0, "timeoutMilliseconds must be positive");

            ct.ThrowIfCancellationRequested();

            int remainingMilliseconds = timeoutMilliseconds;
            while (_isReadinessProbeBlocked())
            {
                if (remainingMilliseconds <= 0)
                {
                    throw new TimeoutException(
                        $"Readiness probe timed out after {timeoutMilliseconds}ms while waiting for Unity editor idle.");
                }

                int delayMilliseconds = Math.Min(READINESS_IDLE_POLL_INTERVAL_MS, remainingMilliseconds);
                // Why: compile, import, and domain reload work can hold the editor thread after the
                // endpoint is bound, so readiness timeout must start only after Unity can answer IPC.
                await _waitBeforeReadinessRetryAsync(
                    delayMilliseconds,
                    ct);
                remainingMilliseconds -= delayMilliseconds;
                ct.ThrowIfCancellationRequested();
            }
        }

        internal async Task ProbeReadinessWithTimeoutAsync(
            CancellationToken ct,
            int timeoutMilliseconds)
        {
            Debug.Assert(timeoutMilliseconds > 0, "timeoutMilliseconds must be positive");

            using (CancellationTokenSource probeCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                Task probeTask = _readinessProbe.ProbeAsync(probeCancellation.Token);
                Task timeoutTask = TimerDelay.Wait(timeoutMilliseconds, ct);
                Task completedTask = await Task.WhenAny(probeTask, timeoutTask).ConfigureAwait(false);
                if (completedTask == probeTask)
                {
                    await probeTask.ConfigureAwait(false);
                    return;
                }

                probeCancellation.Cancel();
                ObserveTimedOutReadinessProbe(probeTask);
            }

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Readiness probe timed out after {timeoutMilliseconds}ms while waiting for project IPC warmup.");
        }

        private static void ObserveTimedOutReadinessProbe(Task probeTask)
        {
            _ = probeTask.ContinueWith(
                completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
