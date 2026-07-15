using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Owns Setup Wizard CLI-step workflow state and async install/repair operations.
    /// </summary>
    internal sealed class SetupWizardCliWorkflowController
    {
        private readonly SetupWizardCliStepPresenter _cliStepPresenter;
        private readonly CliSetupApplicationService _cliSetupApplicationService;
        private readonly Action<bool> _refreshUi;

        private bool _isInstallingCli;
        private bool _needsCliPathSetup;

        internal SetupWizardCliWorkflowController(
            VisualElement cliStatusIcon,
            Label cliStatusLabel,
            Button installCliButton,
            CliSetupApplicationService cliSetupApplicationService,
            Action<bool> refreshUi)
        {
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");
            Debug.Assert(refreshUi != null, "refreshUi must not be null");

            _cliSetupApplicationService = cliSetupApplicationService
                ?? throw new ArgumentNullException(nameof(cliSetupApplicationService));
            _refreshUi = refreshUi
                ?? throw new ArgumentNullException(nameof(refreshUi));

            _cliStepPresenter = new SetupWizardCliStepPresenter(
                cliStatusIcon,
                cliStatusLabel,
                installCliButton,
                HandleInstallCli);
        }

        internal void ShowChecking()
        {
            _cliStepPresenter.ShowChecking();
        }

        internal async Task<bool> RefreshAndUpdateAsync(CancellationToken ct)
        {
            await _cliSetupApplicationService.ForceRefreshCliVersionAsync(ct);
            _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(ct);
            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            UpdateFromCachedState(cliVersion);
            return IsCliInstalled(cliVersion);
        }

        private void UpdateFromCachedState(string cliVersion = null)
        {
            if (cliVersion == null)
            {
                cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            }

            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            string requiredCliVersion = GetMinimumRequiredCliVersion();
            bool cliInstalled = IsCliInstalled(cliVersion);
            _cliStepPresenter.Update(
                cliInstalled,
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion,
                _isInstallingCli,
                _needsCliPathSetup);
        }

        private void HandleInstallCli()
        {
            HandleInstallCliAsync(CancellationToken.None).Forget();
        }

        private async Task HandleInstallCliAsync(CancellationToken ct)
        {
            await RefreshCliPrimaryActionStateAsync(ct);

            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            CliSetupCompatibilityState state = EvaluateCliSetupCompatibilityForSetupWizard(
                cliVersion,
                cliIsDispatcher,
                GetMinimumRequiredCliVersion());
            if (SetupWizardWindow.ShouldRepairCliPathFromPrimaryButton(_needsCliPathSetup, state.NeedsUpdate))
            {
                await HandleRepairCliPathSetup(ct);
                return;
            }

            bool wasCliInstalledBeforeInstall = _cliSetupApplicationService.IsCliInstalled();
            _needsCliPathSetup = false;
            _isInstallingCli = true;
            _cliStepPresenter.Update(
                cliInstalled: false,
                cliVersion: null,
                cliIsDispatcher: false,
                requiredCliVersion: GetMinimumRequiredCliVersion(),
                isInstallingCli: _isInstallingCli,
                needsCliPathSetup: _needsCliPathSetup);

            try
            {
                CliInstallResult result = await _cliSetupApplicationService.InstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    ct);

                if (!result.Success)
                {
                    NativeCliInstallCommandLoadResult commandResult = _cliSetupApplicationService.GetGlobalCliInstallCommand(
                        UnityEngine.Application.platform,
                        true);
                    string manualInstallGuidance = commandResult.Success
                        ? commandResult.Command.ManualCommand
                        : commandResult.ErrorOutput;
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install uloop CLI.\n\n{result.ErrorOutput}\n\n"
                        + manualInstallGuidance,
                        "OK");
                    return;
                }

                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    _cliSetupApplicationService,
                    ct);
                _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(ct);
            }
            finally
            {
                _isInstallingCli = false;
                _refreshUi(CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(
                    wasCliInstalledBeforeInstall));
            }
        }

        private async Task RefreshCliPrimaryActionStateAsync(CancellationToken ct)
        {
            _cliStepPresenter.ShowRefreshingPrimaryAction();

            try
            {
                await _cliSetupApplicationService.ForceRefreshCliVersionAsync(ct);
                _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(ct);
            }
            finally
            {
                RefreshCliStepFromCachedState();
            }
        }

        private void RefreshCliStepFromCachedState()
        {
            UpdateFromCachedState();
        }

        private async Task HandleRepairCliPathSetup(CancellationToken ct)
        {
            _isInstallingCli = true;
            _cliStepPresenter.Update(
                cliInstalled: true,
                cliVersion: _cliSetupApplicationService.GetCachedCliVersion(),
                cliIsDispatcher: _cliSetupApplicationService.GetCachedCliIsDispatcher(),
                requiredCliVersion: GetMinimumRequiredCliVersion(),
                isInstallingCli: _isInstallingCli,
                needsCliPathSetup: _needsCliPathSetup);

            try
            {
                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    _cliSetupApplicationService,
                    ct);
                _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(ct);
            }
            finally
            {
                _isInstallingCli = false;
                _refreshUi(true);
            }
        }

        private async Task<bool> ShouldRepairCliPathSetupAsync(CancellationToken ct)
        {
            bool hasPackageOwnedCurrentUserInstall =
                _cliSetupApplicationService.HasPackageOwnedCurrentUserInstall(UnityEngine.Application.platform);
            if (!SetupWizardWindow.ShouldCheckCliPathSetupForSetupWizard(
                    UnityEngine.Application.platform,
                    hasPackageOwnedCurrentUserInstall))
            {
                return false;
            }

            bool isCliVisibleFromShell = await _cliSetupApplicationService.IsCliVisibleFromShellAsync(
                UnityEngine.Application.platform,
                ct);
            return !isCliVisibleFromShell;
        }

        private static CliSetupCompatibilityState EvaluateCliSetupCompatibilityForSetupWizard(
            string cliVersion,
            bool cliIsDispatcher,
            string requiredCliVersion)
        {
            return CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion);
        }

        private string GetMinimumRequiredCliVersion()
        {
            return _cliSetupApplicationService.GetMinimumRequiredCliVersion();
        }

        private static bool IsCliInstalled(string cliVersion)
        {
            return !string.IsNullOrEmpty(cliVersion);
        }
    }
}
