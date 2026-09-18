using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Picks the warning for a sibling pulled in to re-bind its active patches from the rows the
    /// reload wrote for it, or none when the sibling was re-applied.
    /// </summary>
    internal static class HotReloadSiblingRebindWarningSelector
    {
        private const string FileLevelMethodName = "(file)";

        // Why Failed is checked before Patched/Added: isolation leaves a sibling as Skipped when
        // its added-method callee failed to compile, and claiming that file was re-applied would
        // be false. Why Skipped-only is separate from Failed: nothing failed for such a sibling,
        // so the failed sentence would misdirect the reader.
        public static string SelectUnappliedWarningFormat(HotReloadFileProcessResult result)
        {
            Debug.Assert(result != null, "result must not be null.");

            if (result.Outcomes.Count == 0)
            {
                // A run stopped before it applied anything wrote no row for this file, so the
                // failed-rebind sentence would send the reader looking for rows never written.
                return HotReloadConstants.ActiveSiblingRebindSkippedWarningFormat;
            }

            if (WasRefusedBeforeChangingAnyPatch(result))
            {
                return HotReloadConstants.ActiveSiblingRebindRunRefusedWarningFormat;
            }

            bool sawApplied = false;
            bool sawOnlySkipped = true;
            foreach (HotReloadMethodOutcome outcome in result.Outcomes)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    return HotReloadConstants.ActiveSiblingRebindFailedWarningFormat;
                }

                sawApplied |= outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    || outcome.Kind == HotReloadMethodOutcomeKind.Added;
                sawOnlySkipped &= outcome.Kind == HotReloadMethodOutcomeKind.Skipped;
            }

            if (sawApplied)
            {
                return null;
            }

            return sawOnlySkipped
                ? HotReloadConstants.ActiveSiblingRebindSkippedOnlyWarningFormat
                : HotReloadConstants.ActiveSiblingRebindFailedWarningFormat;
        }

        // Why the revert count is checked too: unchanged patches are reverted before the shim
        // compile, and a later group failure only appends file-level rows without restoring them,
        // so file-level rows alone do not prove the sibling's active patches are unchanged.
        private static bool WasRefusedBeforeChangingAnyPatch(HotReloadFileProcessResult result)
        {
            if (result.RevertedUnchangedCount != 0)
            {
                return false;
            }

            foreach (HotReloadMethodOutcome outcome in result.Outcomes)
            {
                if (outcome.Kind != HotReloadMethodOutcomeKind.Failed || outcome.Method != FileLevelMethodName)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
