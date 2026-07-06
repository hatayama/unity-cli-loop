using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the setup wizard CLI installation step.
    /// </summary>
    internal sealed class SetupWizardCliStepPresenter
    {
        private readonly VisualElement _statusIcon;
        private readonly Label _statusLabel;
        private readonly Button _installButton;

        internal SetupWizardCliStepPresenter(
            VisualElement statusIcon,
            Label statusLabel,
            Button installButton,
            System.Action onInstallClicked)
        {
            Debug.Assert(statusIcon != null, "statusIcon must not be null");
            Debug.Assert(statusLabel != null, "statusLabel must not be null");
            Debug.Assert(installButton != null, "installButton must not be null");
            Debug.Assert(onInstallClicked != null, "onInstallClicked must not be null");

            _statusIcon = statusIcon ?? throw new System.ArgumentNullException(nameof(statusIcon));
            _statusLabel = statusLabel ?? throw new System.ArgumentNullException(nameof(statusLabel));
            _installButton = installButton ?? throw new System.ArgumentNullException(nameof(installButton));
            _installButton.clicked += onInstallClicked
                ?? throw new System.ArgumentNullException(nameof(onInstallClicked));
        }

        internal void ShowChecking()
        {
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--success", false);
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--pending", true);
            _statusLabel.text = "Checking...";
            _installButton.SetEnabled(false);
            _installButton.text = "Checking...";
        }

        internal void ShowRefreshingPrimaryAction()
        {
            _installButton.SetEnabled(false);
            _installButton.text = "Checking...";
        }

        internal void Update(
            bool cliInstalled,
            string cliVersion,
            bool cliIsDispatcher,
            string requiredCliVersion,
            bool isInstallingCli,
            bool needsCliPathSetup)
        {
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion);
            string buttonText = GetCliButtonTextForSetupWizard(
                cliInstalled,
                isInstallingCli,
                false,
                state.NeedsUpdate,
                needsCliPathSetup,
                cliVersion,
                requiredCliVersion);
            bool cliVersionMatched = state.IsCompatible && cliInstalled;
            bool buttonEnabled = IsCliButtonEnabledForSetupWizard(
                cliInstalled,
                cliVersionMatched,
                needsCliPathSetup,
                isInstallingCli,
                isChecking: false);

            bool cliCompatible = cliInstalled && cliVersionMatched;
            _statusLabel.text = GetCliStatusTextForSetupWizard(
                cliInstalled,
                cliCompatible,
                cliVersion,
                requiredCliVersion);
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--success", cliCompatible);
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--pending", !cliCompatible);
            _installButton.SetEnabled(buttonEnabled);
            _installButton.text = buttonText;
        }

        internal static string GetCliStatusTextForSetupWizard(
            bool cliInstalled,
            bool cliCompatible,
            string cliVersion,
            string requiredCliVersion)
        {
            if (!cliInstalled)
            {
                return "Not installed";
            }

            if (cliCompatible)
            {
                return $"v{cliVersion}";
            }

            if (CliSetupLabelFormatter.ShouldShowRequiredVersionText(cliVersion, requiredCliVersion))
            {
                return $"v{cliVersion} (update required)";
            }

            return $"v{cliVersion} (requires v{requiredCliVersion})";
        }

        internal static string GetCliButtonTextForSetupWizard(
            bool cliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool needsCliPathSetup,
            string cliVersion,
            string requiredCliVersion)
        {
            if (isChecking)
            {
                return "Checking...";
            }

            if (isInstallingCli)
            {
                if (CliSetupPrimaryActionPolicy.ShouldRepairCliPath(needsCliPathSetup, needsUpdate))
                {
                    return "Fixing PATH...";
                }

                return "Installing...";
            }

            if (needsUpdate)
            {
                return CliSetupLabelFormatter.GetCliReplacementButtonText("Update", cliVersion, requiredCliVersion);
            }

            if (needsCliPathSetup)
            {
                return "Fix PATH";
            }

            if (!cliInstalled)
            {
                return "Install CLI";
            }

            return "Installed";
        }

        internal static bool IsCliButtonEnabledForSetupWizard(
            bool cliInstalled,
            bool cliVersionMatched,
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isChecking)
        {
            return !isInstallingCli && !isChecking && (!cliInstalled || !cliVersionMatched || needsCliPathSetup);
        }
    }
}
