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
        private readonly Label _managedUpgradeMessage;
        private readonly Button _installButton;

        internal SetupWizardCliStepPresenter(
            VisualElement statusIcon,
            Label statusLabel,
            Label homebrewUpgradeMessage,
            Button installButton,
            System.Action onInstallClicked)
        {
            Debug.Assert(statusIcon != null, "statusIcon must not be null");
            Debug.Assert(statusLabel != null, "statusLabel must not be null");
            Debug.Assert(homebrewUpgradeMessage != null, "homebrewUpgradeMessage must not be null");
            Debug.Assert(installButton != null, "installButton must not be null");
            Debug.Assert(onInstallClicked != null, "onInstallClicked must not be null");

            _statusIcon = statusIcon ?? throw new System.ArgumentNullException(nameof(statusIcon));
            _statusLabel = statusLabel ?? throw new System.ArgumentNullException(nameof(statusLabel));
            _managedUpgradeMessage = homebrewUpgradeMessage
                ?? throw new System.ArgumentNullException(nameof(homebrewUpgradeMessage));
            // Why: the warning carries a command to run, so it must be copyable.
            // UI Toolkit only starts a selection on a focusable text element.
            _managedUpgradeMessage.focusable = true;
            _managedUpgradeMessage.selection.isSelectable = true;
            _installButton = installButton ?? throw new System.ArgumentNullException(nameof(installButton));
            _installButton.clicked += onInstallClicked
                ?? throw new System.ArgumentNullException(nameof(onInstallClicked));
        }

        internal void ShowChecking()
        {
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--success", false);
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--pending", true);
            _statusLabel.text = "Checking...";
            UpdateManagedUpgradeMessage(
                isVisible: false,
                managedCliKind: ManagedCliKind.None,
                cliVersion: null,
                requiredCliVersion: null);
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
            bool needsCliPathSetup,
            ManagedCliKind managedCliKind)
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
                managedCliKind,
                cliVersion,
                requiredCliVersion);
            bool cliVersionMatched = state.IsCompatible && cliInstalled;
            bool buttonEnabled = IsCliButtonEnabledForSetupWizard(
                cliInstalled,
                cliVersionMatched,
                needsCliPathSetup,
                isInstallingCli,
                isChecking: false,
                managedCliKind);

            bool cliCompatible = cliInstalled && cliVersionMatched;
            _statusLabel.text = GetCliStatusTextForSetupWizard(
                cliInstalled,
                cliCompatible,
                cliVersion,
                requiredCliVersion);
            UpdateManagedUpgradeMessage(
                ManagedCliPolicy.ShouldShowUpgradeGuidance(managedCliKind, cliCompatible),
                managedCliKind,
                cliVersion,
                requiredCliVersion);
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--success", cliCompatible);
            ViewDataBinder.ToggleClass(_statusIcon, "setup-status-icon--pending", !cliCompatible);
            _installButton.SetEnabled(buttonEnabled);
            _installButton.text = buttonText;
        }

        private void UpdateManagedUpgradeMessage(
            bool isVisible,
            ManagedCliKind managedCliKind,
            string cliVersion,
            string requiredCliVersion)
        {
            _managedUpgradeMessage.text = isVisible
                ? CliSetupLabelFormatter.GetManagedUpgradeGuidanceText(
                    managedCliKind,
                    cliVersion,
                    requiredCliVersion)
                : string.Empty;
            ViewDataBinder.ToggleClass(_managedUpgradeMessage, "setup-warning-message--visible", isVisible);
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
            ManagedCliKind managedCliKind,
            string cliVersion,
            string requiredCliVersion)
        {
            if (isChecking)
            {
                return "Checking...";
            }

            if (managedCliKind != ManagedCliKind.None)
            {
                if (isInstallingCli)
                {
                    return "Fixing PATH...";
                }

                return needsCliPathSetup
                    ? "Fix PATH"
                    : CliSetupLabelFormatter.GetManagedButtonText(managedCliKind);
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
            bool isChecking,
            ManagedCliKind managedCliKind)
        {
            if (isInstallingCli || isChecking)
            {
                return false;
            }

            if (managedCliKind != ManagedCliKind.None)
            {
                return needsCliPathSetup;
            }

            return !cliInstalled || !cliVersionMatched || needsCliPathSetup;
        }
    }
}
