using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads the compiled call sites of each target assembly into the call-site cache, the read a
    /// run's caller notes would otherwise make cold.
    /// </summary>
    internal sealed class HotReloadCallSiteWarmUpItem : IHotReloadWarmUpItem
    {
        public string Name => "call_sites";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            // Why taken here: the item is entered on the main thread, and the pool thread below
            // must not be the first to touch a Shared instance.
            HotReloadCompiledCallSiteCache cache = HotReloadCompiledCallSiteCache.Shared;
            return Task.Run(() => LoadCallSites(context.Targets, cache, ct));
        }

        /// <summary>Loads each target's dll until cancelled and returns how many it loaded.</summary>
        internal static int LoadCallSites(
            IReadOnlyList<HotReloadWarmUpTarget> targets,
            HotReloadCompiledCallSiteCache cache,
            CancellationToken ct)
        {
            int count = 0;
            foreach (HotReloadWarmUpTarget target in targets)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                cache.GetOrLoad(target.DllPath);
                count++;
            }

            return count;
        }
    }
}
