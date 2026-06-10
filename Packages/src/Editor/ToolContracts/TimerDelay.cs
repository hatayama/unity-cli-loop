using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Provides wall-clock delay operations that do not require Unity Editor update frames.
    /// </summary>
    public static class TimerDelay
    {
        /// <summary>
        /// Waits for wall-clock time without depending on Unity Editor update callbacks.
        /// </summary>
        /// <param name="milliseconds">Milliseconds to wait</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Awaitable task</returns>
        public static Task Wait(int milliseconds, CancellationToken ct = default)
        {
            if (milliseconds <= 0)
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            TimerDelayState state = new TimerDelayState(ct);
            Timer timer = new Timer(state.CompleteFromTimer, null, milliseconds, Timeout.Infinite);
            state.AssignTimer(timer);

            if (ct.CanBeCanceled)
            {
                CancellationTokenRegistration registration = ct.Register(state.CancelFromToken);
                state.AssignRegistration(registration);
            }

            return state.Task;
        }

        /// <summary>
        /// Waits for wall-clock time, then executes an action after the next Editor update.
        /// </summary>
        /// <param name="milliseconds">Milliseconds to wait</param>
        /// <param name="action">Action to execute on main thread</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Awaitable task</returns>
        public static async Task WaitThenExecuteOnMainThread(int milliseconds, Action action, CancellationToken ct = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            SynchronizationContext synchronizationContext = SynchronizationContext.Current;
            Debug.Assert(
                synchronizationContext != null,
                "WaitThenExecuteOnMainThread must start from Unity's main-thread synchronization context.");
            if (synchronizationContext == null)
            {
                throw new InvalidOperationException(
                    "WaitThenExecuteOnMainThread must start from Unity's main-thread synchronization context.");
            }

            await Wait(milliseconds, ct);
            await EditorFrameWaiter.WaitFramesAsync(1, ct);
            action();
        }
    }

    /// <summary>
    /// Owns Timer and cancellation registration disposal for one wall-clock wait.
    /// </summary>
    internal sealed class TimerDelayState
    {
        private readonly TaskCompletionSource<bool> _completionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _ct;
        private Timer _timer;
        private CancellationTokenRegistration _registration;
        private int _isCompleted;

        public TimerDelayState(CancellationToken ct)
        {
            _ct = ct;
        }

        public Task Task => _completionSource.Task;

        public void AssignTimer(Timer timer)
        {
            _timer = timer;
            DisposeTimerIfAlreadyCompleted(timer);
        }

        public void AssignRegistration(CancellationTokenRegistration registration)
        {
            _registration = registration;
            if (Interlocked.CompareExchange(ref _isCompleted, 0, 0) != 0)
            {
                registration.Dispose();
            }
        }

        public void CompleteFromTimer(object state)
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _timer?.Dispose();
            _registration.Dispose();
            _completionSource.TrySetResult(true);
        }

        public void CancelFromToken()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _timer?.Dispose();
            _registration.Dispose();
            _completionSource.TrySetCanceled(_ct);
        }

        private void DisposeTimerIfAlreadyCompleted(Timer timer)
        {
            if (Interlocked.CompareExchange(ref _isCompleted, 0, 0) == 0)
            {
                return;
            }

            timer.Dispose();
        }
    }
}
