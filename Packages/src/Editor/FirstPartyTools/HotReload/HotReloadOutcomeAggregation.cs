using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Run-level outcome counting, distinct-id merge, and inline-risk warning formatting.
    /// </summary>
    internal static class HotReloadOutcomeAggregation
    {
        internal static string FormatInlineRiskAggregatedWarning(
            int atRiskCount,
            int patchedTotal,
            IReadOnlyList<string> methodLabels)
        {
            return HotReloadJitInliningRisk.FormatAggregatedWarning(atRiskCount, patchedTotal, methodLabels);
        }

        // Duplicate file inputs process the same source twice, producing duplicates across
        // per-file result lists; aggregated warnings must name each pause-point id / method
        // label once even then. Methods and PatchedTotal keep reflecting raw patch
        // operations on purpose.
        internal static void AppendDistinct(List<string> target, IReadOnlyList<string> additions)
        {
            foreach (string addition in additions)
            {
                if (!target.Contains(addition))
                {
                    target.Add(addition);
                }
            }
        }

        // Why HashSet (not AppendDistinct): sibling const-drift warnings repeat once per
        // --files entry of the same assembly. AppendDistinct is the pause-point id / method
        // label merger and must stay dedicated to those lists.
        internal static List<string> DeduplicatePreserveOrder(IReadOnlyList<string> warnings)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> unique = new List<string>();
            for (int index = 0; index < warnings.Count; index++)
            {
                string warning = warnings[index];
                if (seen.Add(warning))
                {
                    unique.Add(warning);
                }
            }

            return unique;
        }

        internal static (int patchedCount, int failedCount, int skippedCount, int alreadyActiveCount, int addedCount)
            CountMethodOutcomeKinds(IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            int patchedCount = 0;
            int failedCount = 0;
            int skippedCount = 0;
            int alreadyActiveCount = 0;
            int addedCount = 0;
            for (int index = 0; index < outcomes.Count; index++)
            {
                HotReloadMethodOutcomeKind kind = outcomes[index].Kind;
                if (kind == HotReloadMethodOutcomeKind.Patched)
                {
                    patchedCount++;
                }
                else if (kind == HotReloadMethodOutcomeKind.Failed)
                {
                    failedCount++;
                }
                else if (kind == HotReloadMethodOutcomeKind.Skipped)
                {
                    skippedCount++;
                }
                else if (kind == HotReloadMethodOutcomeKind.AlreadyActive)
                {
                    alreadyActiveCount++;
                }
                else if (kind == HotReloadMethodOutcomeKind.Added)
                {
                    addedCount++;
                }
            }

            return (patchedCount, failedCount, skippedCount, alreadyActiveCount, addedCount);
        }
    }
}
