using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class DynamicCodeExecutionScheduler : IDisposable
    {
        private const int BusyHandoffWindowMilliseconds = 50;
        private const int CancelledPrewarmHandoffWindowMilliseconds = 500;

        private readonly Action _disposeResources;
        private readonly DynamicCodeExecutionSchedulerHooks _hooks;
        private readonly int _busyHandoffWindowMilliseconds;
        private readonly int _cancelledPrewarmHandoffWindowMilliseconds;
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
            int cancelledPrewarmHandoffWindowMilliseconds = CancelledPrewarmHandoffWindowMilliseconds)
        {
            _disposeResources = disposeResources ?? throw new ArgumentNullException(nameof(disposeResources));
            _hooks = hooks ?? new DynamicCodeExecutionSchedulerHooks();
            _busyHandoffWindowMilliseconds = busyHandoffWindowMilliseconds;
            _cancelledPrewarmHandoffWindowMilliseconds = cancelledPrewarmHandoffWindowMilliseconds;
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

        public Task ShutdownAsync()
        {
            Dispose();
            return _shutdownCompletionSource.Task;
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
