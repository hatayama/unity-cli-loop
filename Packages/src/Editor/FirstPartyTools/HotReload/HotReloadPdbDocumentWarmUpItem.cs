using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads the PDB document list of each target assembly, the list a run reads when it snapshots
    /// the state of a group.
    /// </summary>
    internal sealed class HotReloadPdbDocumentWarmUpItem : IHotReloadWarmUpItem
    {
        public string Name => "pdb_documents";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            // Why taken here: the Shared instance reads Application.dataPath when it is first
            // touched, which only the main thread may do.
            HotReloadPdbDocumentIndex index = HotReloadPdbDocumentIndex.Shared;
            return Task.Run(() => PreloadDocuments(context.Targets, index, ct));
        }

        /// <summary>Preloads each target's document list with the MVID a run would read; throws before the next target once cancelled.</summary>
        internal static void PreloadDocuments(
            IReadOnlyList<HotReloadWarmUpTarget> targets,
            HotReloadPdbDocumentIndex index,
            CancellationToken ct)
        {
            foreach (HotReloadWarmUpTarget target in targets)
            {
                // Why throw rather than stop: an item that returns normally is reported done.
                ct.ThrowIfCancellationRequested();
                string moduleVersionId = HotReloadAssemblyMvid.Read(target.DllPath);
                index.Preload(target.DllPath, target.PdbPath, moduleVersionId);
            }
        }
    }
}
