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

        /// <summary>
        /// Tells the guard that the CLI found a modal dialog holding the Editor main thread while its
        /// toolName request waited, so a request of that name that has not entered its tool code is
        /// failed without the full wait. An empty name counts as no report. Safe to call from any
        /// thread, and does nothing for a request whose tool code has started.
        /// </summary>
        void ReportBlockingDialog(string toolName);
    }
}
