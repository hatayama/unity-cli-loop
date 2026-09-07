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
            int committedTypeCount)
        {
            Debug.Assert(patchedTotal >= 0, "patchedTotal must not be negative.");
            Debug.Assert(addedCount >= 0, "addedCount must not be negative.");
            Debug.Assert(committedTypeCount >= 0, "committedTypeCount must not be negative.");

            if (!hasFailure)
            {
                return string.Empty;
            }

            // Why the types count toward a partial apply: they stay loaded whatever the methods
            // did, so a run that activated one did apply part of what was asked.
            if (patchedTotal + addedCount + committedTypeCount > 0)
            {
                return HotReloadConstants.PartialApplyRecommendedNextAction;
            }

            return HotReloadConstants.FailedWithNoApplyRecommendedNextAction;
        }
    }
}
