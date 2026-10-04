using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Shows the modal dialogs that the settings window and setup wizard workflows open.
    /// </summary>
    internal interface IPresentationDialogs
    {
        void ShowMessage(string title, string message);

        bool Confirm(string title, string message, string ok, string cancel);

        void ShowSkillsInstalled();

        bool ConfirmCliUninstall();

        Task<CliPathSetupFlowResult> EnsureCliVisibleAndShowResultAsync(
            RuntimePlatform platform,
            CliSetupApplicationService cliSetupApplicationService,
            CancellationToken ct);
    }
}
