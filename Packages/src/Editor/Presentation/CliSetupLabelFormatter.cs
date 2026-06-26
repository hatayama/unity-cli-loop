using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Formats CLI setup labels shared by Settings and Setup Wizard.
    /// </summary>
    internal static class CliSetupLabelFormatter
    {
        public static string GetCliReplacementButtonText(string action, string cliVersion, string requiredCliVersion)
        {
            if (ShouldShowProtocolCompatibilityText(cliVersion, requiredCliVersion))
            {
                return $"{action} CLI (protocol v{CliConstants.REQUIRED_CLI_PROTOCOL_VERSION})";
            }

            return $"{action} CLI (v{cliVersion} \u2192 v{requiredCliVersion})";
        }

        public static bool ShouldShowProtocolCompatibilityText(string cliVersion, string requiredCliVersion)
        {
            if (string.IsNullOrEmpty(cliVersion) || string.IsNullOrEmpty(requiredCliVersion))
            {
                return false;
            }

            return CliVersionComparer.IsVersionEqual(cliVersion, requiredCliVersion);
        }
    }
}
