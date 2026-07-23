using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Holds pure state-transition rules for the third-party tool migration wizard.
    /// </summary>
    internal static class ThirdPartyToolMigrationWizardStateRules
    {
        internal const int MigrationProgressUiUpdateIntervalMilliseconds = 100;

        internal static bool ShouldReportMigrationProgress(
            long lastReportTimestamp,
            long currentTimestamp,
            ThirdPartyToolMigrationProgress progress,
            long stopwatchFrequency,
            int updateIntervalMilliseconds)
        {
            Debug.Assert(currentTimestamp >= 0, "currentTimestamp must not be negative");
            Debug.Assert(stopwatchFrequency > 0, "stopwatchFrequency must be positive");
            Debug.Assert(updateIntervalMilliseconds >= 0, "updateIntervalMilliseconds must not be negative");

            if (lastReportTimestamp == 0)
            {
                return true;
            }

            if (progress.TotalItemCount > 0 && progress.ProcessedItemCount >= progress.TotalItemCount)
            {
                return true;
            }

            long elapsedTicks = currentTimestamp - lastReportTimestamp;
            long requiredTicks = stopwatchFrequency * updateIntervalMilliseconds / 1000;
            return elapsedTicks >= requiredTicks;
        }

        internal static bool ShouldApplyMigrationProgress(
            bool isCancellationRequested,
            bool hasActiveOperation)
        {
            return !isCancellationRequested && hasActiveOperation;
        }

        internal static bool ShouldRefreshAfterMigration(ThirdPartyToolMigrationResult result)
        {
            Debug.Assert(result.FileCount >= 0, "result file count must not be negative");

            return false;
        }

        internal static bool ShouldFinishMigrationOnMainThread(
            bool isCancellationRequested,
            ThirdPartyToolMigrationResult result)
        {
            Debug.Assert(result.FileCount >= 0, "result file count must not be negative");

            return !isCancellationRequested || result.Changed;
        }

        internal static bool ShouldRefreshAfterInterruptedMigration(
            bool isMigrationCompletionPending,
            bool isCancellationRequested)
        {
            return isMigrationCompletionPending && !isCancellationRequested;
        }

        /// <summary>
        /// The Migrate confirm dialog must not assert an exact file count that came only from
        /// compile-error seeds (auto-scan detected state): a cascading compile-skip could make the
        /// real full-scan write more files than the seed count, so an exact number there would
        /// understate what the user is about to approve. Only a verified full-scan count (RefreshUI)
        /// is safe to show as-is.
        /// </summary>
        internal static int GetMigrationConfirmDialogFileCount(
            bool hasVerifiedPendingFileCount,
            int pendingFileCount)
        {
            Debug.Assert(pendingFileCount >= 0, "pendingFileCount must not be negative");

            return hasVerifiedPendingFileCount ? pendingFileCount : 0;
        }

        /// <summary>
        /// Returns whether the temporary V3 migration skill note should be visible.
        /// </summary>
        internal static bool ShouldShowTemporarySkillNote(SkillInstallState installState)
        {
            // C# scan results are not a completion signal for this skill (docs/scripts remain
            // in scope), so only the install state decides whether the temporary note shows.
            return installState != SkillInstallState.Missing;
        }
    }
}
