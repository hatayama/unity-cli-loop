using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads the referenced-method set of every dll that references a target assembly, the sets a
    /// run's caller scan consults before it decides which of those dlls to read.
    /// </summary>
    internal sealed class HotReloadReferencedMethodSetWarmUpItem : IHotReloadWarmUpItem
    {
        public string Name => "referenced_method_sets";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            // Why taken here: the Shared instance reads Application.dataPath when it is first
            // touched, which only the main thread may do.
            HotReloadReferencedMethodIndex index = HotReloadReferencedMethodIndex.Shared;
            return Task.Run(() => PreloadSets(context.Targets, index, ct));
        }

        /// <summary>Preloads each referencing dll's set until cancelled.</summary>
        internal static void PreloadSets(
            IReadOnlyList<HotReloadWarmUpTarget> targets,
            HotReloadReferencedMethodIndex index,
            CancellationToken ct)
        {
            foreach (HotReloadWarmUpTarget target in targets)
            {
                foreach (string dllPath in target.ReferencingDllPaths)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    index.Preload(dllPath);
                }
            }
        }
    }
}
