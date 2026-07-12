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
        internal const int MaxMigrationTargetPathsInStatus = 50;

        internal static int GetMigrationTargetPathsOverflowCount(int totalPathCount, int maxPaths)
        {
            Debug.Assert(totalPathCount >= 0, "totalPathCount must not be negative");
            Debug.Assert(maxPaths > 0, "maxPaths must be positive");

            if (totalPathCount <= maxPaths)
            {
                return 0;
            }

            return totalPathCount - maxPaths;
        }

        internal static string NormalizeDisplayPathSeparators(string path)
        {
            Debug.Assert(path != null, "path must not be null");

            // Status text must stay Windows-safe: never leave backslashes in displayed relative paths.
            return path.Replace('\\', '/');
        }

        internal static string ToProjectRelativeDisplayPath(string filePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string relativePath = System.IO.Path.GetRelativePath(
                System.IO.Path.GetFullPath(projectRoot),
                System.IO.Path.GetFullPath(filePath));
            return NormalizeDisplayPathSeparators(relativePath);
        }

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
    }
}
