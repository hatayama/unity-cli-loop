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

        /// <summary>
        /// Formats the warning text that tells a Homebrew user how to reach the required CLI version.
        /// </summary>
        /// <remarks>
        /// Why the line breaks: the command is not run by Unity, so the text has to say where it belongs,
        /// and the command must stay on its own line so it can be selected and copied without picking up
        /// the surrounding sentence.
        /// </remarks>
        public static string GetHomebrewUpgradeGuidanceText(string cliVersion, string requiredCliVersion)
        {
            return $"Homebrew-managed CLI v{cliVersion} is older than the required v{requiredCliVersion}.\n"
                + "Run this command in your terminal:\n"
                + $"{CliConstants.HOMEBREW_UPGRADE_COMMAND}";
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
