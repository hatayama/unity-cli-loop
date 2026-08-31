using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Shows the terminal PATH setup result without owning shell profile details.
    /// </summary>
    internal static class CliPathSetupPrompt
    {
        public static async Task<CliPathSetupFlowResult> EnsureVisibleAndShowResultAsync(
            RuntimePlatform platform,
            CliSetupApplicationService cliSetupApplicationService,
            CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(
                cliSetupApplicationService != null,
                "cliSetupApplicationService must not be null");

            CliPathSetupFlowResult result =
                await cliSetupApplicationService.EnsureCliVisibleFromShellAsync(platform, ct);
            ShowResult(result);
            return result;
        }

        internal static void ShowResult(CliPathSetupFlowResult result)
        {
            if (result.Status == CliPathSetupFlowStatus.AlreadyVisible)
            {
                return;
            }

            if (result.Status == CliPathSetupFlowStatus.ManualSetupRequired)
            {
                if (string.IsNullOrWhiteSpace(result.Plan.ManualCommand))
                {
                    EditorUtility.DisplayDialog(
                        "Finish uLoop CLI PATH Setup",
                        BuildManualSetupMessage(result.Plan),
                        "OK");
                    return;
                }

                bool copyCommand = EditorUtility.DisplayDialog(
                    "Finish uLoop CLI PATH Setup",
                    BuildManualSetupMessage(result.Plan),
                    "Copy Command",
                    "OK");
                if (copyCommand)
                {
                    EditorGUIUtility.systemCopyBuffer = result.Plan.ManualCommand;
                }

                return;
            }

            if (result.Status == CliPathSetupFlowStatus.Failed)
            {
                EditorUtility.DisplayDialog(
                    "PATH Setup Failed",
                    $"Could not update your shell profile.\n\n{result.ErrorOutput}\n\n"
                    + BuildManualCommandFallback(result.Plan),
                    "OK");
                return;
            }

            if (result.Status == CliPathSetupFlowStatus.AppliedButStillMissing)
            {
                EditorUtility.DisplayDialog(
                    "PATH Setup Still Needed",
                    "PATH setup was updated, but a fresh terminal still cannot find uloop.\n\n"
                    + $"Profile: {result.Plan.ConfigurationFilePath}\n\n"
                    + BuildManualCommandFallback(result.Plan),
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "PATH Setup Complete",
                BuildCompleteMessage(result),
                "OK");
        }

        private static string BuildManualSetupMessage(CliPathSetupPlan plan)
        {
            string shellName = string.IsNullOrWhiteSpace(plan.ShellName)
                ? "your shell"
                : plan.ShellName;
            return "The uLoop CLI was installed, but your terminal cannot find the uloop command yet.\n\n"
                + $"Detected shell: {shellName}\n"
                + BuildInstallDirectoryLine(plan)
                + "Add the install directory to PATH in your shell profile."
                + BuildOptionalManualCommand(plan);
        }

        private static string BuildInstallDirectoryLine(CliPathSetupPlan plan)
        {
            return string.IsNullOrWhiteSpace(plan.InstallDirectory)
                ? "Install directory: unavailable\n\n"
                : $"Install directory: {plan.InstallDirectory}\n\n";
        }

        private static string BuildOptionalManualCommand(CliPathSetupPlan plan)
        {
            return string.IsNullOrWhiteSpace(plan.ManualCommand)
                ? ""
                : "\n\n" + plan.ManualCommand;
        }

        private static string BuildManualCommandFallback(CliPathSetupPlan plan)
        {
            if (!string.IsNullOrWhiteSpace(plan.ManualCommand))
            {
                return $"You can run this manually:\n{plan.ManualCommand}";
            }

            return string.IsNullOrWhiteSpace(plan.InstallDirectory)
                ? "Could not determine the CLI install directory. Add the directory that contains uloop to PATH manually."
                : $"Add this directory to PATH in your shell profile:\n{plan.InstallDirectory}";
        }

        private static string BuildCompleteMessage(CliPathSetupFlowResult result)
        {
            string action = result.Status == CliPathSetupFlowStatus.AlreadyConfiguredAndVisible
                ? "Your shell profile already contains the PATH setup."
                : "PATH setup was updated.";
            return "The uLoop CLI is now available from a fresh terminal.\n\n"
                + action + "\n\n"
                + $"Profile: {result.Plan.ConfigurationFilePath}\n"
                + $"Line: {result.Plan.ConfigurationLine}";
        }
    }
}
