using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Chooses RecommendedNextAction for hot-reload apply responses that include Failed outcomes.
    /// </summary>
    internal static class HotReloadRecommendedNextAction
    {
        internal static string Resolve(
            bool hasFailure,
            int patchedTotal,
            int addedCount,
            int introducedTypeCount)
        {
            Debug.Assert(patchedTotal >= 0, "patchedTotal must not be negative.");
            Debug.Assert(addedCount >= 0, "addedCount must not be negative.");
            Debug.Assert(introducedTypeCount >= 0, "introducedTypeCount must not be negative.");

            if (!hasFailure)
            {
                return string.Empty;
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
