using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Creates user-facing copy for the third-party tool migration wizard.
    /// </summary>
    internal static class ThirdPartyToolMigrationWizardText
    {
        internal const string MigrationNotCheckedText = "Migration status has not been checked.";
        internal const string NoMigrationTargetsText = "No V3 custom tool migration is needed.";
        private const string MigrationCheckingText = "Scanning project for V3 custom tool migration...";
        private const string MigrationApplyingText = "Migrating project files for V3 custom tools...";
        private const string MigrationButtonReadyText = "Migrate";
        private const string MigrationButtonMigratingText = "Migrating...";
        private const string MigrationButtonNoTargetsText = "Nothing to migrate";
        private const string MigrationButtonCheckRequiredText = "Check required";

        internal static string GetMigrationStatusText(int fileCount)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");

            string noun = fileCount == 1 ? "file" : "files";
            string verb = fileCount == 1 ? "needs" : "need";
            string subject = fileCount == 1 ? "this file still uses" : "these files still use";
            string objectPronoun = fileCount == 1 ? "it" : "them";

            return $"{fileCount} {noun} {verb} V3 custom tool migration.\n" +
                $"The Unity Console is showing errors because {subject} the old custom tool API.\n\n" +
                $"Click Migrate to update {objectPronoun} automatically. " +
                "The errors should disappear after migration.";
        }

        internal static string GetMigrationProgressText(
            ThirdPartyToolMigrationProgress progress,
            bool isMigrating)
        {
            string statusText = isMigrating ? MigrationApplyingText : MigrationCheckingText;
            if (progress.TotalItemCount <= 0)
            {
                return statusText;
            }

            return $"{statusText}\n" +
                $"{progress.ProcessedItemCount}/{progress.TotalItemCount} steps complete.";
        }

        internal static string GetMigrationButtonText(
            bool isMigrating,
            bool hasMigrationTargets,
            bool hasCheckedMigrationStatus)
        {
            if (!hasCheckedMigrationStatus)
            {
                return MigrationButtonCheckRequiredText;
            }

            if (isMigrating)
            {
                return MigrationButtonMigratingText;
            }

            return hasMigrationTargets ? MigrationButtonReadyText : MigrationButtonNoTargetsText;
        }
    }
}
