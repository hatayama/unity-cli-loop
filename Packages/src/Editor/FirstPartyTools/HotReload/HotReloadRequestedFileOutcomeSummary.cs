using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Separates the outcomes of the files a run was asked about from those of the sibling files
    /// it pulled in to re-apply earlier changes.
    /// </summary>
    internal static class HotReloadRequestedFileOutcomeSummary
    {
        // Why the siblings are excluded: a sibling file is re-applied on its own initiative, so
        // its Patched/Added rows would hide that nothing asked for in this run was applied.
        // Why an empty FilePath is skipped: an outcome that belongs to no file, such as a
        // file-level row, says nothing about the requested files. A Failed one of those is
        // already reported by the failure path, which runs before this conclusion is used.
        public static bool AreAllRequestedOutcomesSkipped(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyCollection<string> reappliedSiblingPaths,
            Func<string, string> toProjectRelativeScriptPath)
        {
            Debug.Assert(methods != null, "methods must not be null.");
            Debug.Assert(reappliedSiblingPaths != null, "reappliedSiblingPaths must not be null.");
            Debug.Assert(
                toProjectRelativeScriptPath != null,
                "toProjectRelativeScriptPath must not be null.");

            HotReloadReappliedSiblingFiles siblingFiles =
                new HotReloadReappliedSiblingFiles(reappliedSiblingPaths, toProjectRelativeScriptPath);
            int requestedCount = 0;
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = methods[index];
                if (string.IsNullOrEmpty(outcome.FilePath))
                {
                    continue;
                }

                if (siblingFiles.Contains(outcome.FilePath))
                {
                    continue;
                }

                requestedCount++;
                if (outcome.Kind != HotReloadMethodOutcomeKind.Skipped)
                {
                    return false;
                }
            }

            return requestedCount > 0;
        }

        /// <summary>
        /// Counts the Patched and Added rows that belong to a sibling file the run pulled in to
        /// re-apply its earlier changes, as opposed to the files the caller asked about.
        /// </summary>
        // Why only Patched and Added: the sibling paths list every pulled-in file whatever
        // happened to it, and only these two kinds are part of the counts the message reports.
        public static int CountReappliedSiblingOutcomes(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyCollection<string> reappliedSiblingPaths,
            Func<string, string> toProjectRelativeScriptPath)
        {
            Debug.Assert(methods != null, "methods must not be null.");
            Debug.Assert(reappliedSiblingPaths != null, "reappliedSiblingPaths must not be null.");
            Debug.Assert(
                toProjectRelativeScriptPath != null,
                "toProjectRelativeScriptPath must not be null.");

            HotReloadReappliedSiblingFiles siblingFiles =
                new HotReloadReappliedSiblingFiles(reappliedSiblingPaths, toProjectRelativeScriptPath);
            int count = 0;
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = methods[index];
                if (outcome.Kind != HotReloadMethodOutcomeKind.Patched
                    && outcome.Kind != HotReloadMethodOutcomeKind.Added)
                {
                    continue;
                }

                if (siblingFiles.Contains(outcome.FilePath))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
