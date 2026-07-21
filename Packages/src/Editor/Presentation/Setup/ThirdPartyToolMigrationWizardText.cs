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
        internal const string CSharpMigrationDescriptionText =
            "Use this for C# custom tool source files that still use the V2 API. This section scans the Unity " +
            "project and can rewrite matching C# files automatically.";
        internal const string AiMigrationSkillSectionTitle = "AI Skill and Script Migration";
        internal const string AiMigrationSkillDescriptionText =
            "Install a temporary AI skill for updating SKILL.md, Markdown, shell scripts, and PowerShell scripts " +
            "that call uloop.\nThe skill teaches your AI agent how to search, inspect context, and update only " +
            "real V2 CLI usage. This window only installs or removes it.";
        internal const string AiMigrationSkillUsageFoldoutTitle = "Prompt for your AI agent";
        internal const string AiMigrationSkillTemporaryNoteText =
            "This skill is temporary. Remove it once your docs and scripts are migrated to V3 CLI syntax.";
        internal const string AiMigrationSkillPromptText =
            "Use the v3-cli-invocation-migration skill in this project to update Unity CLI Loop V2 CLI usage " +
            "for V3.\n\n" +
            "Scope:\n" +
            "- Check SKILL.md, Markdown, POSIX shell scripts, and PowerShell scripts.\n" +
            "- Migrate V2 boolean arguments, renamed first-party options, and removed commands.\n" +
            "- Read nearby context before editing. Do not change C# snippets, enum/member references, regex match properties, DTO properties, or non-uloop JSON.\n" +
            "- After editing, summarize changed files, remaining candidates, and any commands I should verify manually.";
        internal const string MigrationNotCheckedText = "C# source migration status has not been checked.";
        internal const string NoMigrationTargetsText = "No C# source structure migration is needed.";
        internal const string MigrationConfirmDialogTitle = "Migrate C# Sources?";
        internal const string MigrationConfirmDialogOkText = "Migrate";
        internal const string MigrationConfirmDialogCancelText = "Cancel";
        internal const string AutoScanProgressTitle = "Unity CLI Loop";
        internal const string AutoScanProgressDescription = "Checking for V3 custom tool migration targets...";
        private const string MigrationCheckingText = "Scanning C# source files for V3 custom tool API migration...";
        private const string MigrationApplyingText = "Migrating C# source files to V3 custom tool APIs...";
        private const string MigrationButtonReadyText = "Migrate";
        private const string MigrationButtonMigratingText = "Migrating...";
        private const string MigrationButtonNoTargetsText = "Nothing to migrate";
        private const string MigrationButtonCheckRequiredText = "Check required";
        private const string InstallMigrationSkillButtonText = "Install Migration Skill";
        private const string RemoveMigrationSkillButtonText = "Remove Migration Skill";
        private const string UpdatingMigrationSkillButtonText = "Updating...";
        private const string CopyMigrationSkillPromptButtonText = "Copy AI Prompt";

        internal static string GetMigrationStatusText(int fileCount)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");

            string noun = fileCount == 1 ? "file" : "files";
            string verb = fileCount == 1 ? "needs" : "need";
            return $"Found {fileCount} C# {noun} that {verb} V3 migration.";
        }

        internal static string GetMigrationConfirmDialogMessage(int fileCount)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");

            string noun = fileCount == 1 ? "file" : "files";
            return $"{fileCount} {noun} will be rewritten in place.\n\n" +
                "Commit or back up your project first (VCS recommended).";
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

        internal static string GetMigrationSkillPromptCopyButtonText()
        {
            return CopyMigrationSkillPromptButtonText;
        }
    }
}
