using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Timer-based asynchronous delay for Unity Editor
    /// Provides async/await support for time-based delays that work regardless of Unity Editor state
    /// </summary>
    public static class TimerDelay
    {
        /// <summary>
        /// Wait for the specified number of milliseconds
        /// </summary>
        /// <param name="milliseconds">Milliseconds to wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Awaitable task</returns>
        public static Task Wait(int milliseconds, CancellationToken cancellationToken = default)
        {
            if (milliseconds <= 0)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new();
            
            Timer timer = null;
            timer = new Timer(_ =>
            {
                timer?.Dispose();
                timer = null;
                if (!cancellationToken.IsCancellationRequested)
                {
                    tcs.SetResult(true);
                }
                else
                {
                    tcs.SetCanceled();
                }
            }, null, milliseconds, Timeout.Infinite);

            // Handle cancellation
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    timer?.Dispose();
                    timer = null;
                    tcs.TrySetCanceled();
                });
            }

            return tcs.Task;
        }

        /// <summary>
        /// Wait for the specified number of milliseconds, then execute action on main thread
        /// </summary>
        /// <param name="milliseconds">Milliseconds to wait</param>
        /// <param name="action">Action to execute on main thread</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Awaitable task</returns>
        public static Task WaitThenExecuteOnMainThread(int milliseconds, Action action, CancellationToken cancellationToken = default)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (milliseconds <= 0)
            {
                EditorApplication.delayCall += () => action();
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new();
            
            Timer timer = null;
            timer = new Timer(_ =>
            {
                timer?.Dispose();
                timer = null;
                if (!cancellationToken.IsCancellationRequested)
                {
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            action();
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    };
                }
                else
                {
                    tcs.SetCanceled();
                }
            }, null, milliseconds, Timeout.Infinite);

            // Handle cancellation
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    timer?.Dispose();
                    timer = null;
                    tcs.TrySetCanceled();
                });
            }

            return tcs.Task;
        }
    }
}