using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads the compiled call sites of each target assembly into the call-site cache, the read a
    /// run's caller notes would otherwise make cold.
    /// </summary>
    internal sealed class HotReloadCallSiteWarmUpItem : IHotReloadWarmUpItem
    {
        private readonly HotReloadCompiledCallers _compiledCallers;

        internal HotReloadCallSiteWarmUpItem(HotReloadCompiledCallers compiledCallers)
        {
            Debug.Assert(compiledCallers != null, "compiledCallers must not be null.");
            _compiledCallers = compiledCallers;
        }

        public string Name => "call_sites";

        public Task RunAsync(HotReloadWarmUpContext context, CancellationToken ct)
        {
            List<string> dllPaths = new List<string>();
            foreach (HotReloadWarmUpTarget target in context.Targets)
            {
                dllPaths.Add(target.DllPath);
            }

            return _compiledCallers.WarmUpCallSitesAsync(dllPaths, ct);
        }
    }
}
