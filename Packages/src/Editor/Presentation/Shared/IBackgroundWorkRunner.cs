using System;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Runs blocking presentation work off the editor main thread.
    /// </summary>
    internal interface IBackgroundWorkRunner
    {
        Task<T> RunAsync<T>(Func<T> work);

        Task<T> RunTaskAsync<T>(Func<Task<T>> work);
    }
}
