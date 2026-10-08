using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One kind of work the warm-up does ahead of the first run of a domain, such as loading the
    /// call sites of the target assemblies.
    /// </summary>
    internal interface IHotReloadWarmUpItem
    {
        /// <summary>Name as it appears in the hot_reload_warm_up_complete entry (snake_case).</summary>
        string Name { get; }

        /// <summary>
        /// Entered on the main thread. Throws OperationCanceledException before the next unit of work
        /// (one dll, one process start) once ct is cancelled, so an item that stopped early is never
        /// reported done; never observes ct in the middle of a unit.
        /// </summary>
        Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct);
    }
}
