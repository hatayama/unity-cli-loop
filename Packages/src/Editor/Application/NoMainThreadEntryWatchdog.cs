using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Leaves every request waiting for the main thread as long as it takes. Tests build the
    /// execution service without a watchdog; only the composition root wires the real one.
    /// </summary>
    internal sealed class NoMainThreadEntryWatchdog : IMainThreadEntryWatchdog
    {
        internal static readonly NoMainThreadEntryWatchdog Instance = new();

        private NoMainThreadEntryWatchdog()
        {
        }

        public Task GuardAsync(ToolExecutionLease lease, CancellationToken stop)
        {
            return Task.CompletedTask;
        }
    }
}
