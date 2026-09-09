using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The isolation retry that follows the signature-change gate.
    /// </summary>
    internal sealed class HotReloadSignatureChangeGateIsolationTrigger : IHotReloadIsolationTrigger
    {
        public string LogName => HotReloadConstants.VibeLogIsolationTriggerSignatureChangeGate;

        /// <summary>
        /// Leaves the retry-only skip reasons as the worker reported them. Why nothing happens
        /// here: this retry isolates gated replacements rather than a compile failure, so an
        /// indirect caller of an excluded added method is genuinely unavailable and must keep
        /// saying so.
        /// </summary>
        public void RewriteRetryOnlySkippedReasons(
            List<TransformWorkerSkippedDto> retryOnlyRows,
            IReadOnlyCollection<string> excludedAddedMethodKeys)
        {
        }
    }
}
