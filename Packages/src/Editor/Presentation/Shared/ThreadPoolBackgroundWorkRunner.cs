using System;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Runs presentation background work on the thread pool.
    /// </summary>
    internal sealed class ThreadPoolBackgroundWorkRunner : IBackgroundWorkRunner
    {
        // No token is passed to Task.Run: callers check their token after the await and return quietly,
        // whereas a cancelled Task.Run would throw and log from fire-and-forget refreshes.
        public Task<T> RunAsync<T>(Func<T> work)
        {
            return Task.Run(work);
        }

        public Task<T> RunTaskAsync<T>(Func<Task<T>> work)
        {
            return Task.Run(work);
        }
    }
}
