using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The isolation retry that follows a failed shim compile.
    /// </summary>
    internal sealed class HotReloadShimCompileFailureIsolationTrigger : IHotReloadIsolationTrigger
    {
        public string LogName => HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure;

        /// <summary>
        /// Rewrites the reason of every caller that only became unavailable because an added
        /// method was excluded from this retry, including callers reached over several hops.
        /// </summary>
        public void RewriteRetryOnlySkippedReasons(
            List<TransformWorkerSkippedDto> retryOnlyRows,
            IReadOnlyCollection<string> excludedAddedMethodKeys)
        {
            HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
            if (excludedAddedMethodKeys != null)
            {
                foreach (string key in excludedAddedMethodKeys)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        reachable.Add(key);
                    }
                }
            }

            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (TransformWorkerSkippedDto row in retryOnlyRows)
                {
                    if (!string.Equals(
                        row.reason,
                        HotReloadConstants.UnavailableAddedCallSkipReason,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(row.calledAddedMethodKey)
                        || !reachable.Contains(row.calledAddedMethodKey))
                    {
                        continue;
                    }

                    row.reason = HotReloadConstants.IsolatedAddedMethodCallerSkipReason;
                    progressed = true;
                    if (!string.IsNullOrEmpty(row.methodKey))
                    {
                        reachable.Add(row.methodKey);
                    }
                }
            }
        }
    }
}
