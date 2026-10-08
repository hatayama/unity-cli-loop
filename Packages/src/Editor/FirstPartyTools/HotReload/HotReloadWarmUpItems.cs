using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The items the production warm-up runs, in order.
    /// </summary>
    internal static class HotReloadWarmUpItems
    {
        /// <remarks>
        /// Why costliest first: a run that comes mid-way waits for one item and reuses its result,
        /// so the order decides only what is lost when the warm-up does not finish, and the
        /// costliest cold read is the most worth having. Why the PDB documents last as well: their
        /// lookup throws an exception no item catches when the PDB is from another build than the
        /// dll, which stops the remaining items, and last there are none left to lose.
        /// </remarks>
        internal static IReadOnlyList<IHotReloadWarmUpItem> CreateProduction()
        {
            return new IHotReloadWarmUpItem[]
            {
                new HotReloadCallSiteWarmUpItem(),
                new HotReloadReferencedMethodSetWarmUpItem(),
                new HotReloadPdbDocumentWarmUpItem()
            };
        }
    }
}
