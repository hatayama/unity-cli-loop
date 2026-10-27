using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Text and enablement rules for the skill install location picker shared by Settings and Setup Wizard.
    /// </summary>
    internal static class SkillsInstallRootText
    {
        internal const string UnavailableDialogTitle = "No Git Repository";

        internal const string UnavailableDialogMessage =
            "No Git repository contains this Unity project, so skills cannot be installed at the Git " +
            "repository root. Choose \"Unity Project Root\" as the install location.";

        internal const string UnavailableLocationText = "No Git repository contains this Unity project.";

        internal static string BuildLocationText(SkillInstallRootSelection selection)
        {
            return selection.IsSelectedRootAvailable
                ? $"Skills are installed under {selection.SelectedRootPath}"
                : UnavailableLocationText;
        }

        // With no Git repository the picker offers nothing else, so it stays disabled unless the
        // saved choice is the unavailable Git root, which the user must be able to change back.
        internal static bool IsPickerEnabled(SkillInstallRootSelection selection, bool canEdit)
        {
            return canEdit
                && (selection.IsGitRepositoryRootAvailable
                    || selection.SelectedRoot == SkillInstallRoot.GitRepositoryRoot);
        }
    }
}
