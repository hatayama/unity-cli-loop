using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Shows the terminal PATH setup result without owning shell profile details.
    /// </summary>
    internal static class CliPathSetupPrompt
    {
        public static async Task<CliPathSetupFlowResult> EnsureVisibleAndShowResultAsync(
            RuntimePlatform platform,
            CancellationToken ct)
        {
            CliPathSetupFlowResult result =
                await CliSetupApplicationFacade.EnsureCliVisibleFromShellAsync(platform, ct);
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
                    + $"You can run this manually:\n{result.Plan.ManualCommand}",
                    "OK");
                return;
            }

            if (result.Status == CliPathSetupFlowStatus.AppliedButStillMissing)
            {
                EditorUtility.DisplayDialog(
                    "PATH Setup Still Needed",
                    "PATH setup was updated, but a fresh terminal still cannot find uloop.\n\n"
                    + $"Profile: {result.Plan.ConfigurationFilePath}\n\n"
                    + $"You can run this manually:\n{result.Plan.ManualCommand}",
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
                + $"Install directory: {plan.InstallDirectory}\n\n"
                + "Add the install directory to PATH in your shell profile.\n\n"
                + plan.ManualCommand;
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
