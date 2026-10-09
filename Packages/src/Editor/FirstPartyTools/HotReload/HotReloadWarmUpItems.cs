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
        /// dll, which stops the remaining items, and last there are none left to lose. Why the
        /// publicized copies first: rewriting and writing the image of an edited assembly is the
        /// costliest step of a cold run. Why the transform worker before the PDB documents: its cold
        /// start is the cheapest of the costly items on a large project (0.5–1 s), and the PDB
        /// documents must stay last. Cecil's resolution and IO exceptions count as a failed item
        /// and do not stop the items after it; any other exception does, but a run with the same
        /// input makes the same call and fails the same way.
        /// </remarks>
        internal static IReadOnlyList<IHotReloadWarmUpItem> CreateProduction()
        {
            return new IHotReloadWarmUpItem[]
            {
                new HotReloadPublicizedTargetWarmUpItem(),
                new HotReloadCallSiteWarmUpItem(),
                new HotReloadReferencedMethodSetWarmUpItem(),
                new HotReloadTransformWorkerWarmUpItem(TransformWorkerHost.Shared.RunAsync),
                new HotReloadPdbDocumentWarmUpItem()
            };
        }
    }
}
