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
        /// Formats the status text that tells a Homebrew user how to reach the required CLI version.
        /// </summary>
        public static string GetHomebrewUpgradeStatusText(string cliVersion, string requiredCliVersion)
        {
            return $"v{cliVersion} (requires v{requiredCliVersion}; run: {CliConstants.HOMEBREW_UPGRADE_COMMAND})";
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
