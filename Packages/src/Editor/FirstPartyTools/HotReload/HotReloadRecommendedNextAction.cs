using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Chooses RecommendedNextAction for hot-reload apply responses that include Failed outcomes.
    /// </summary>
    internal static class HotReloadRecommendedNextAction
    {
        internal static string Resolve(bool hasFailure, int patchedTotal, int addedCount)
        {
            Debug.Assert(patchedTotal >= 0, "patchedTotal must not be negative.");
            Debug.Assert(addedCount >= 0, "addedCount must not be negative.");

            if (!hasFailure)
            {
                return string.Empty;
            }

            if (patchedTotal + addedCount > 0)
            {
                return HotReloadConstants.PartialApplyRecommendedNextAction;
            }

            return HotReloadConstants.FailedWithNoApplyRecommendedNextAction;
        }
    }
}
