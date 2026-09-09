using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs hot reload for a set of files end to end. The tool depends on this rather than on the
    /// production pipeline, so a test can put the tool through a run it decides the result of.
    /// </summary>
    internal interface IHotReloadOrchestrator
    {
        Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile = null);
    }
}
