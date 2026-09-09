using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What drove an isolation retry. Why an interface rather than a trigger string: the retry
    /// path treats triggers differently in more than one place, and a new trigger would otherwise
    /// have to be added to every string comparison spread across that path.
    /// </summary>
    internal interface IHotReloadIsolationTrigger
    {
        /// The trigger name written to the isolation-retry log line.
        string LogName { get; }

        /// Adjusts the skip reasons that only the retry produced, before they become outcomes.
        void RewriteRetryOnlySkippedReasons(
            List<TransformWorkerSkippedDto> retryOnlyRows,
            IReadOnlyCollection<string> excludedAddedMethodKeys);
    }
}
