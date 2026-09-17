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
            bool allRequestedSkipped)
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

            // Why the types count toward a partial apply: a type this run introduced stays
            // loaded whatever the methods did, so the run applied part of what was asked.
            if (patchedTotal + addedCount + introducedTypeCount > 0)
            {
                return HotReloadConstants.PartialApplyRecommendedNextAction;
            }

            return HotReloadConstants.FailedWithNoApplyRecommendedNextAction;
        }
    }
}
