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

        // Why a dedicated sibling list (not a global HashSet over all warnings): duplicate
        // file inputs must still emit the same own-file warning twice. Sibling-derived
        // strings are appended after own-file warnings, ordinal-deduped among themselves,
        // and skipped when the own-file list already contains the exact text so holder +
        // referencing stays one warning in either file order.
        internal static void AppendSiblingDerivedWarnings(
            List<string> ownFileWarnings,
            IReadOnlyList<string> siblingDerivedWarnings)
        {
            Debug.Assert(ownFileWarnings != null, "ownFileWarnings must not be null.");
            Debug.Assert(siblingDerivedWarnings != null, "siblingDerivedWarnings must not be null.");

            HashSet<string> seen = new HashSet<string>(ownFileWarnings, StringComparer.Ordinal);
            for (int index = 0; index < siblingDerivedWarnings.Count; index++)
            {
                string warning = siblingDerivedWarnings[index];
                if (string.IsNullOrEmpty(warning))
                {
                    continue;
                }

                if (seen.Add(warning))
                {
                    ownFileWarnings.Add(warning);
                }
            }
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
