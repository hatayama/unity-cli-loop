using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Creates user-facing copy for the third-party tool migration wizard.
    /// </summary>
    internal static class ThirdPartyToolMigrationWizardText
    {
        internal const string CSharpMigrationSectionTitle = "C# Source Structure Migration";
        internal const string AiMigrationSkillSectionTitle = "AI Skill and Script Migration";
        internal const string AiMigrationSkillDescriptionText =
            "Install a temporary AI skill for updating SKILL.md, Markdown, shell scripts, and PowerShell scripts " +
            "that call uloop.\nThe skill gives your AI agent a checklist and detection scripts; this window only " +
            "installs or removes it.";
        internal const string MigrationNotCheckedText = "C# source migration status has not been checked.";
        internal const string NoMigrationTargetsText = "No C# source structure migration is needed.";
        private const string MigrationCheckingText = "Scanning C# source files for V3 custom tool API migration...";
        private const string MigrationApplyingText = "Migrating C# source files to V3 custom tool APIs...";
        private const string MigrationButtonReadyText = "Migrate";
        private const string MigrationButtonMigratingText = "Migrating...";
        private const string MigrationButtonNoTargetsText = "Nothing to migrate";
        private const string MigrationButtonCheckRequiredText = "Check required";
        private const string InstallMigrationSkillButtonText = "Install Migration Skill";
        private const string RemoveMigrationSkillButtonText = "Remove Migration Skill";
        private const string UpdatingMigrationSkillButtonText = "Updating...";

        internal static string GetMigrationStatusText(int fileCount)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");

            string noun = fileCount == 1 ? "file" : "files";
            string verb = fileCount == 1 ? "needs" : "need";
            string subject = fileCount == 1 ? "this file still uses" : "these files still use";
            string objectPronoun = fileCount == 1 ? "it" : "them";

            return $"{fileCount} {noun} {verb} V3 C# source structure migration.\n" +
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

        internal static string GetMigrationSkillButtonText(
            bool isUpdating,
            SkillInstallState installState)
        {
            if (isUpdating)
            {
                return UpdatingMigrationSkillButtonText;
            }

            return installState == SkillInstallState.Installed || installState == SkillInstallState.Outdated
                ? RemoveMigrationSkillButtonText
                : InstallMigrationSkillButtonText;
        }
    }
}
