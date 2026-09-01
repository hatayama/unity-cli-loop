using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Formats CLI setup labels shared by Settings and Setup Wizard.
    /// </summary>
    public static class CliSetupLabelFormatter
    {
        // Why: uloop installed by Homebrew must be updated and removed through brew itself,
        // so the package offers no primary action for it.
        public const string HOMEBREW_MANAGED_BUTTON_TEXT = "Managed by Homebrew";
        public const string WINGET_MANAGED_BUTTON_TEXT = "Managed by winget";

        /// <summary>
        /// Returns the disabled primary-button label for a package-manager-owned CLI.
        /// </summary>
        public static string GetManagedButtonText(ManagedCliKind kind)
        {
            System.Diagnostics.Debug.Assert(kind != ManagedCliKind.None, "kind must identify a package manager");
            return kind == ManagedCliKind.Homebrew
                ? HOMEBREW_MANAGED_BUTTON_TEXT
                : WINGET_MANAGED_BUTTON_TEXT;
        }

        /// <summary>
        /// Formats the warning text that tells a package-manager user which command makes the CLI usable.
        /// </summary>
        /// <remarks>
        /// Why the line breaks: the command is not run by Unity, so the text has to say where it belongs,
        /// and the command must stay on its own line so it can be selected and copied without picking up
        /// the surrounding sentence.
        /// Why the version is compared here: an update is also required when the detected binary does not
        /// answer as the dispatcher, which happens at versions that already satisfy the requirement. Telling
        /// such a user to upgrade names a command that reports nothing to do, so those cases point at a
        /// reinstall instead and the text never claims a comparison that is not true.
        /// </remarks>
        public static string GetManagedUpgradeGuidanceText(
            ManagedCliKind kind,
            string cliVersion,
            string requiredCliVersion)
        {
            System.Diagnostics.Debug.Assert(kind != ManagedCliKind.None, "kind must identify a package manager");
            string managedDescription = kind == ManagedCliKind.Homebrew
                ? "Homebrew-managed"
                : "winget-managed";
            string reinstallCommand = kind == ManagedCliKind.Homebrew
                ? CliConstants.HOMEBREW_REINSTALL_COMMAND
                : CliConstants.WINGET_REINSTALL_COMMAND;
            string upgradeCommand = kind == ManagedCliKind.Homebrew
                ? CliConstants.HOMEBREW_UPGRADE_COMMAND
                : CliConstants.WINGET_UPGRADE_COMMAND;

            if (string.IsNullOrEmpty(cliVersion))
            {
                return managedDescription + " CLI did not report a version.\n"
                    + "Run this command in your terminal:\n"
                    + reinstallCommand;
            }

            if (CliVersionComparer.IsVersionGreaterThanOrEqual(cliVersion, requiredCliVersion))
            {
                return $"{managedDescription} CLI v{cliVersion} did not answer as the required uloop CLI.\n"
                    + "Run this command in your terminal:\n"
                    + reinstallCommand;
            }

            return $"{managedDescription} CLI v{cliVersion} does not meet the required v{requiredCliVersion}.\n"
                + "Run this command in your terminal:\n"
                + upgradeCommand;
        }

        public static string GetCliReplacementButtonText(string action, string cliVersion, string requiredCliVersion)
        {
            if (ShouldShowRequiredVersionText(cliVersion, requiredCliVersion))
            {
                return $"{action} CLI (v{requiredCliVersion} required)";
            }

            return $"{action} CLI (v{cliVersion} \u2192 v{requiredCliVersion})";
        }

        public static bool ShouldShowRequiredVersionText(string cliVersion, string requiredCliVersion)
        {
            if (string.IsNullOrEmpty(cliVersion) || string.IsNullOrEmpty(requiredCliVersion))
            {
                return false;
            }

            return CliVersionComparer.IsVersionEqual(cliVersion, requiredCliVersion);
        }
    }
}
