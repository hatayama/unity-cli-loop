using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schedules Dynamic Code Execution work at the point the owning workflow expects.
    /// </summary>
    internal sealed class DynamicCodeExecutionScheduler : IDisposable
    {
        private const int BusyHandoffWindowMilliseconds = 50;
        private const int CancelledPrewarmHandoffWindowMilliseconds = 500;
        private const int DefaultShutdownTimeoutMilliseconds = 5000;

        private readonly Action _disposeResources;
        private readonly DynamicCodeExecutionSchedulerHooks _hooks;
        private readonly int _busyHandoffWindowMilliseconds;
        private readonly int _cancelledPrewarmHandoffWindowMilliseconds;
        private readonly int _shutdownTimeoutMilliseconds;
        private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
        private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
        private readonly TaskCompletionSource<bool> _shutdownCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _disposeLock = new();
        private readonly object _executionStateLock = new();
        private readonly object _backgroundPrewarmTransitionLock = new();
        private CancellationTokenSource _backgroundPrewarmCancellationTokenSource;
        private bool _backgroundPrewarmInProgress;
        private bool _resourcesDisposed;
        private bool _disposed;

        public DynamicCodeExecutionScheduler(
            Action disposeResources,
            DynamicCodeExecutionSchedulerHooks hooks = null,
            int busyHandoffWindowMilliseconds = BusyHandoffWindowMilliseconds,
            int cancelledPrewarmHandoffWindowMilliseconds = CancelledPrewarmHandoffWindowMilliseconds,
            int shutdownTimeoutMilliseconds = DefaultShutdownTimeoutMilliseconds)
        {
            Debug.Assert(busyHandoffWindowMilliseconds > 0, "busyHandoffWindowMilliseconds must be positive");
            Debug.Assert(
                cancelledPrewarmHandoffWindowMilliseconds > 0,
                "cancelledPrewarmHandoffWindowMilliseconds must be positive");
            Debug.Assert(shutdownTimeoutMilliseconds > 0, "shutdownTimeoutMilliseconds must be positive");

            _disposeResources = disposeResources ?? throw new ArgumentNullException(nameof(disposeResources));
            _hooks = hooks ?? new DynamicCodeExecutionSchedulerHooks();
            _busyHandoffWindowMilliseconds = busyHandoffWindowMilliseconds;
            _cancelledPrewarmHandoffWindowMilliseconds = cancelledPrewarmHandoffWindowMilliseconds;
            _shutdownTimeoutMilliseconds = shutdownTimeoutMilliseconds;
        }

        public void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DynamicCodeExecutionScheduler));
            }
        }

        public async Task<T> RunForegroundAsync<T>(
            Func<CancellationToken, Task<T>> action,
            Func<T> createBusyResult,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            bool entered = await _executionSemaphore.WaitAsync(0, cancellationToken);
            if (!entered)
            {
                await _hooks.InvokeAfterBusySemaphoreProbeFailedAsync();
                if (!TryCancelBackgroundPrewarm())
                {
                    entered = await TryAcquireAfterBusyHandoffAsync(cancellationToken);
                    if (!entered)
                    {
                        return createBusyResult();
                    }
                }
                else
                {
                    entered = await TryAcquireAfterBusyHandoffAsync(
                        cancellationToken,
                        _cancelledPrewarmHandoffWindowMilliseconds);
                    if (!entered)
                    {
                        return createBusyResult();
                    }
                }
            }

            CancellationTokenSource executionCancellationTokenSource = null;
            try
            {
                _hooks.AfterSemaphoreEntered?.Invoke();
                ThrowIfDisposed();
                executionCancellationTokenSource =
                    CreateExecutionCancellationTokenSource(cancellationToken);
                SetExecutionState(false, executionCancellationTokenSource);
                return await action(executionCancellationTokenSource.Token);
            }
            finally
            {
                ClearExecutionState(false, executionCancellationTokenSource);
                executionCancellationTokenSource?.Dispose();
                DisposeResourcesIfRequested();
                _executionSemaphore.Release();
            }
        }

        public async Task<(bool Entered, T Result)> TryRunIfIdleAsync<T>(
            bool yieldToForegroundRequests,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!yieldToForegroundRequests)
            {
                return await TryRunWithoutForegroundYieldAsync(action, cancellationToken);
            }

            bool entered;
            CancellationTokenSource executionCancellationTokenSource = null;
            lock (_backgroundPrewarmTransitionLock)
            {
                entered = _executionSemaphore.Wait(0);
                if (!entered)
                {
                    return (false, default);
                }

                ThrowIfDisposed();
                executionCancellationTokenSource =
                    CreateExecutionCancellationTokenSource(cancellationToken);
                SetExecutionState(true, executionCancellationTokenSource);
            }

            try
            {
                await _hooks.InvokeAfterBackgroundExecutionStatePublishedAsync();
                _hooks.AfterSemaphoreEntered?.Invoke();
                T result = await action(executionCancellationTokenSource.Token);
                return (true, result);
            }
            finally
            {
                ClearExecutionState(true, executionCancellationTokenSource);
                executionCancellationTokenSource?.Dispose();
                DisposeResourcesIfRequested();
                _executionSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetimeCancellationTokenSource.Cancel();
            if (!_executionSemaphore.Wait(0))
            {
                return;
            }

            try
            {
                DisposeResourcesIfRequested();
            }
            finally
            {
                _executionSemaphore.Release();
            }
        }

        /// <summary>
        /// Cancels in-flight work and waits for resource dispose up to the shutdown timeout.
        /// Why timeout then TrySetResult: waiters must unblock even when user code ignores
        /// cancellation; pool dispose stays deferred to the running action's finally.
        /// </summary>
        public async Task ShutdownAsync()
        {
            Dispose();
            if (_shutdownCompletionSource.Task.IsCompleted)
            {
                return;
            }

            using CancellationTokenSource timeoutCancellationTokenSource = new();
            Task delayTask = Task.Delay(
                _shutdownTimeoutMilliseconds,
                timeoutCancellationTokenSource.Token);
            // Why observe via ContinueWith: canceling Delay leaves a canceled task; observing it
            // avoids unobserved-task noise without a try/catch around await.
            _ = delayTask.ContinueWith(
                _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            Task completedTask = await Task.WhenAny(_shutdownCompletionSource.Task, delayTask);
            if (completedTask == _shutdownCompletionSource.Task)
            {
                timeoutCancellationTokenSource.Cancel();
                return;
            }

            _hooks.InvokeLogWarning(
                "Dynamic code scheduler shutdown drain timed out after " +
                _shutdownTimeoutMilliseconds +
                "ms; executor pool dispose is deferred until the running action reaches its finally.");
            _shutdownCompletionSource.TrySetResult(true);
        }

        private async Task<(bool Entered, T Result)> TryRunWithoutForegroundYieldAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            bool entered = await _executionSemaphore.WaitAsync(0, cancellationToken);
            if (!entered)
            {
                return (false, default);
            }

            CancellationTokenSource executionCancellationTokenSource = null;
            try
            {
                _hooks.AfterSemaphoreEntered?.Invoke();
                ThrowIfDisposed();
                executionCancellationTokenSource =
                    CreateExecutionCancellationTokenSource(cancellationToken);
                SetExecutionState(false, executionCancellationTokenSource);
                T result = await action(executionCancellationTokenSource.Token);
                return (true, result);
            }
            finally
            {
                ClearExecutionState(false, executionCancellationTokenSource);
                executionCancellationTokenSource?.Dispose();
                DisposeResourcesIfRequested();
                _executionSemaphore.Release();
            }
        }

        private bool TryCancelBackgroundPrewarm()
        {
            lock (_executionStateLock)
            {
                if (!_backgroundPrewarmInProgress || _backgroundPrewarmCancellationTokenSource == null)
                {
                    return false;
                }

                _backgroundPrewarmCancellationTokenSource.Cancel();
                return true;
            }
        }

        private async Task<bool> TryAcquireAfterBusyHandoffAsync(
            CancellationToken cancellationToken,
            int handoffWindowMilliseconds = -1)
        {
            if (handoffWindowMilliseconds < 0)
            {
                handoffWindowMilliseconds = _busyHandoffWindowMilliseconds;
            }

            using CancellationTokenSource handoffCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellationTokenSource.Token);
            handoffCancellationTokenSource.CancelAfter(handoffWindowMilliseconds);

            try
            {
                await _executionSemaphore.WaitAsync(handoffCancellationTokenSource.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested ||
                    _lifetimeCancellationTokenSource.IsCancellationRequested)
                {
                    throw;
                }

                return false;
            }
        }

        private CancellationTokenSource CreateExecutionCancellationTokenSource(
            CancellationToken cancellationToken)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellationTokenSource.Token);
        }

        private void SetExecutionState(
            bool yieldToForegroundRequests,
            CancellationTokenSource executionCancellationTokenSource)
        {
            lock (_executionStateLock)
            {
                _backgroundPrewarmInProgress = yieldToForegroundRequests;
                _backgroundPrewarmCancellationTokenSource = yieldToForegroundRequests
                    ? executionCancellationTokenSource
                    : null;
            }
        }

        private void ClearExecutionState(
            bool yieldToForegroundRequests,
            CancellationTokenSource executionCancellationTokenSource)
        {
            if (!yieldToForegroundRequests)
            {
                return;
            }

            lock (_executionStateLock)
            {
                if (!ReferenceEquals(_backgroundPrewarmCancellationTokenSource, executionCancellationTokenSource))
                {
                    return;
                }

                _backgroundPrewarmInProgress = false;
                _backgroundPrewarmCancellationTokenSource = null;
            }
        }

        private void DisposeResourcesIfRequested()
        {
            if (!_disposed)
            {
                return;
            }

            lock (_disposeLock)
            {
                if (_resourcesDisposed)
                {
                    return;
                }

                _resourcesDisposed = true;
            }

            _disposeResources();
            _lifetimeCancellationTokenSource.Dispose();
            _shutdownCompletionSource.TrySetResult(true);
        }
    }
}
