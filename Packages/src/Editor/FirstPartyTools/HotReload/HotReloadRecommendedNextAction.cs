using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Chooses RecommendedNextAction for hot-reload apply responses.
    /// </summary>
    internal static class HotReloadRecommendedNextAction
    {
        internal static string Resolve(
            bool hasFailure,
            int patchedTotal,
            int addedCount,
            int introducedTypeCount,
            bool allRequestedSkipped,
            HotReloadFailureKinds failureKinds)
        {
            Debug.Assert(patchedTotal >= 0, "patchedTotal must not be negative.");
            Debug.Assert(addedCount >= 0, "addedCount must not be negative.");
            Debug.Assert(introducedTypeCount >= 0, "introducedTypeCount must not be negative.");

            if (!hasFailure)
            {
                // Why a next action without a failure: every method of the requested files was
                // Skipped, so the run answers Success while none of the asked-for edits are live.
                return allRequestedSkipped
                    ? HotReloadConstants.RequestedFilesAllSkippedRecommendedNextAction
                    : string.Empty;
            }

            Debug.Assert(
                failureKinds != HotReloadFailureKinds.None,
                "A failed run must report what kinds of failure it had.");

            // Why the types count toward a partial apply: a type this run introduced stays
            // loaded whatever the methods did, so the run applied part of what was asked.
            bool applied = patchedTotal + addedCount + introducedTypeCount > 0;
            List<string> sentences = new List<string>();
            string leadingSentence = ChooseLeadingSentence(failureKinds, applied);
            if (leadingSentence != null)
            {
                sentences.Add(leadingSentence);
            }

            // Why said apart: next to the fix advice, the rows that only need a rerun would
            // otherwise read as more to fix.
            if (NeedsAFix(failureKinds) && Has(failureKinds, HotReloadFailureKinds.EditorNotReady))
            {
                sentences.Add(HotReloadConstants.EditorNotReadyAppendedRecommendedNextAction);
            }

            if (Has(failureKinds, HotReloadFailureKinds.VirtualPlayer))
            {
                sentences.Add(HotReloadConstants.VirtualPlayerRecommendedNextAction);
            }
            else if (Has(failureKinds, HotReloadFailureKinds.CompiledAssemblyMissing))
            {
                sentences.Add(HotReloadConstants.CompiledAssemblyMissingRecommendedNextAction);
            }

            return string.Join(" ", sentences);
        }

        // Why a failure to fix leads: fixing it and rerunning is what the reader does first, and a
        // busy Editor or a missing assembly only adds to that. Without one, the busy Editor's
        // advice replaces the fix advice, because there is nothing to fix.
        private static string ChooseLeadingSentence(HotReloadFailureKinds failureKinds, bool applied)
        {
            if (NeedsAFix(failureKinds))
            {
                return applied
                    ? HotReloadConstants.PartialApplyRecommendedNextAction
                    : HotReloadConstants.FailedWithNoApplyRecommendedNextAction;
            }

            if (Has(failureKinds, HotReloadFailureKinds.EditorNotReady))
            {
                return applied
                    ? HotReloadConstants.EditorNotReadyAfterPartialApplyRecommendedNextAction
                    : HotReloadConstants.EditorNotReadyRecommendedNextAction;
            }

            // Why only after an apply: a missing assembly's own sentence says all there is to
            // do, and the partial-apply sentence only adds the revert the applied patches allow.
            return applied ? HotReloadConstants.PartialApplyRecommendedNextAction : null;
        }

        // Why a failure without a kind counts as one to fix: that is the advice every failure got
        // before the kinds existed, so a failure that lost its kind still gets a next action.
        private static bool NeedsAFix(HotReloadFailureKinds failureKinds)
        {
            return failureKinds == HotReloadFailureKinds.None
                || Has(failureKinds, HotReloadFailureKinds.Declaration);
        }

        private static bool Has(HotReloadFailureKinds failureKinds, HotReloadFailureKinds kind)
        {
            return (failureKinds & kind) != 0;
        }
    }
}
