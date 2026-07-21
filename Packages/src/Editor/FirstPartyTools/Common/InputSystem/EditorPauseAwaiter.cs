#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Awaits the Editor entering Play Mode pause via <see cref="EditorApplication.pauseStateChanged"/>,
    /// so a wait can react to a pause the instant it happens instead of only noticing it the next
    /// time some other wait (a frame, a timeout) happens to resolve.
    /// </summary>
    /// <remarks>
    /// Why event-based: while Play Mode is paused, the Editor tick that drives frame-count-based
    /// waits (<see cref="EditorFrameWaiter"/>) can go a long time between observations, so a
    /// caller racing only a frame wait and a wall-clock timeout can miss a pause that started and
    /// ended entirely inside one such wait.
    /// </remarks>
    internal static class EditorPauseAwaiter
    {
        /// <summary>
        /// Must be called from the main thread: <see cref="EditorApplication.pauseStateChanged"/>
        /// is main-thread-only. Only reacts to a future transition into pause — a pause already
        /// in effect at subscribe time is caught instead by the caller's own timeout-side
        /// fallback re-check (see PressLifetimeIterationResolver's isPausedFallback), because
        /// reading <see cref="EditorApplication.isPaused"/> synchronously here is not safe at
        /// every call site (observed to throw when invoked while a PlayMode test's scene is
        /// still being loaded).
        /// </summary>
        public static Task WaitForPauseAsync(CancellationToken ct)
        {
            TaskCompletionSource<bool> completionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnPauseStateChanged(PauseState state)
            {
                if (state != PauseState.Paused)
                {
                    return;
                }

                EditorApplication.pauseStateChanged -= OnPauseStateChanged;
                completionSource.TrySetResult(true);
            }

            EditorApplication.pauseStateChanged += OnPauseStateChanged;

            CancellationTokenRegistration registration = ct.Register(() =>
            {
                EditorApplication.pauseStateChanged -= OnPauseStateChanged;
                completionSource.TrySetCanceled(ct);
            });

            _ = completionSource.Task.ContinueWith(
                _ => registration.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return completionSource.Task;
        }
    }
}
#endif
