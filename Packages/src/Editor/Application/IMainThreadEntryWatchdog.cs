using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Guards one request between taking the command slot and entering its tool code: the guard
    /// may cancel the lease while the request still waits for the Editor main thread, and must
    /// stop doing anything once the stop token is cancelled.
    /// </summary>
    internal interface IMainThreadEntryWatchdog
    {
        Task GuardAsync(ToolExecutionLease lease, CancellationToken stop);
    }
}
