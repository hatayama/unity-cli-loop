using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads the referenced-method set of every dll that references a target assembly, the sets a
    /// run's caller scan consults before it decides which of those dlls to read.
    /// </summary>
    internal sealed class HotReloadReferencedMethodSetWarmUpItem : IHotReloadWarmUpItem
    {
        private readonly HotReloadCompiledCallers _compiledCallers;

        internal HotReloadReferencedMethodSetWarmUpItem(HotReloadCompiledCallers compiledCallers)
        {
            Debug.Assert(compiledCallers != null, "compiledCallers must not be null.");
            _compiledCallers = compiledCallers;
        }

        public string Name => "referenced_method_sets";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            List<string> dllPaths = CollectDllPaths(context.Targets);
            return _compiledCallers.WarmUpReferencedMethodSetsAsync(dllPaths, ct);
        }

        /// <summary>The referencing dlls of every target, in target order; never a target's own dll.</summary>
        internal static List<string> CollectDllPaths(IReadOnlyList<HotReloadWarmUpTarget> targets)
        {
            List<string> dllPaths = new List<string>();
            foreach (HotReloadWarmUpTarget target in targets)
            {
                dllPaths.AddRange(target.ReferencingDllPaths);
            }

            return dllPaths;
        }
    }
}
