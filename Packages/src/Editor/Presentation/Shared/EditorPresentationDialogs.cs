using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Opens the real editor dialogs for the settings window and setup wizard workflows.
    /// </summary>
    internal sealed class EditorPresentationDialogs : IPresentationDialogs
    {
        public void ShowMessage(string title, string message)
        {
            EditorUtility.DisplayDialog(title, message, "OK");
        }

        public bool Confirm(string title, string message, string ok, string cancel)
        {
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        public void ShowSkillsInstalled()
        {
            EditorDialogHelper.ShowSkillsInstalledDialog();
        }

        public bool ConfirmCliUninstall()
        {
            return CliUninstallPrompt.ConfirmUninstall();
        }

        public Task<CliPathSetupFlowResult> EnsureCliVisibleAndShowResultAsync(
            RuntimePlatform platform,
            CliSetupApplicationService cliSetupApplicationService,
            CancellationToken ct)
        {
            return CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(platform, cliSetupApplicationService, ct);
        }
    }
}
