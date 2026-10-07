using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One-word answer to "are the requested edits live now?" for an apply run. ReplacedByCompile
    /// is written by the CLI after a fallback compile succeeded and is listed here so the enum
    /// names every value a caller can see.
    /// </summary>
    internal enum HotReloadApplyOutcomeKind
    {
        // Something of the requested files is live, and none of their methods was Skipped.
        Applied = 0,

        // Something of the requested files is live, and some of their methods were Skipped.
        PartiallyApplied = 1,

        // Nothing of the requested files is live, and some of their methods were Skipped.
        NothingApplied = 2,

        // Nothing of the requested files is live, and none was Skipped: every method is
        // unchanged, or the files hold no method bodies.
        NothingToApply = 3,

        // A Failed row, siblings included: the same condition that turns Success false.
        Failed = 4,

        // Written by the CLI only.
        ReplacedByCompile = 5
    }

    /// <summary>
    /// Decides the <see cref="HotReloadApplyOutcomeKind"/> of an apply run.
    /// </summary>
    /// <remarks>
    /// Why the sibling rows are left out: a sibling file is re-applied on the run's own initiative,
    /// so its rows say nothing about the edit the caller asked about (the same split the message
    /// uses through HotReloadRequestedFileOutcomeSummary). A Failed row counts wherever it is,
    /// because Success already turns false for it.
    /// </remarks>
    internal static class HotReloadApplyOutcome
    {
        internal static HotReloadApplyOutcomeKind Decide(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> introducedTypes,
            HotReloadReappliedSiblingFiles siblingFiles,
            bool hasFailure)
        {
            Debug.Assert(methods != null, "methods must not be null.");
            Debug.Assert(introducedTypes != null, "introducedTypes must not be null.");
            Debug.Assert(siblingFiles != null, "siblingFiles must not be null.");
            if (hasFailure)
            {
                return HotReloadApplyOutcomeKind.Failed;
            }

            int liveCount = CountRequestedLiveMethods(methods, siblingFiles)
                + CountRequestedLiveTypes(introducedTypes, siblingFiles);
            bool anySkipped = HasRequestedSkippedMethod(methods, siblingFiles);
            if (liveCount == 0)
            {
                return anySkipped
                    ? HotReloadApplyOutcomeKind.NothingApplied
                    : HotReloadApplyOutcomeKind.NothingToApply;
            }

            return anySkipped
                ? HotReloadApplyOutcomeKind.PartiallyApplied
                : HotReloadApplyOutcomeKind.Applied;
        }

        // Why AlreadyActive counts: the earlier run's patch is still running, so the answer to
        // "is it live now?" is yes. Why Stale does not: it is what is left of a method the source
        // no longer declares, not the outcome of an edit, and StaleTotal reports it apart.
        private static int CountRequestedLiveMethods(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            HotReloadReappliedSiblingFiles siblingFiles)
        {
            int count = 0;
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome method = methods[index];
                if (siblingFiles.Contains(method.FilePath))
                {
                    continue;
                }

                if (method.Kind == HotReloadMethodOutcomeKind.Patched
                    || method.Kind == HotReloadMethodOutcomeKind.Added
                    || method.Kind == HotReloadMethodOutcomeKind.AlreadyActive)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRequestedLiveTypes(
            IReadOnlyList<HotReloadIntroducedTypeOutcome> introducedTypes,
            HotReloadReappliedSiblingFiles siblingFiles)
        {
            int count = 0;
            for (int index = 0; index < introducedTypes.Count; index++)
            {
                HotReloadIntroducedTypeOutcome type = introducedTypes[index];
                if (siblingFiles.Contains(type.OwnerProjectRelativePath))
                {
                    continue;
                }

                if (type.Kind == HotReloadIntroducedTypeOutcomeKind.Introduced
                    || type.Kind == HotReloadIntroducedTypeOutcomeKind.AlreadyActive)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasRequestedSkippedMethod(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            HotReloadReappliedSiblingFiles siblingFiles)
        {
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome method = methods[index];
                if (method.Kind == HotReloadMethodOutcomeKind.Skipped
                    && !siblingFiles.Contains(method.FilePath))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
