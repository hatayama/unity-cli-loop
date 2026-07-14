using System;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the CLI setup section and owns Settings primary-action mediation decisions.
    /// </summary>
    internal sealed class UnityCliLoopSettingsCliSetupPresenter
    {
        private readonly UnityCliLoopSettingsWindowUI _view;
        private readonly CliSetupApplicationService _cliSetupApplicationService;

        internal UnityCliLoopSettingsCliSetupPresenter(
            UnityCliLoopSettingsWindowUI view,
            CliSetupApplicationService cliSetupApplicationService)
        {
            Debug.Assert(view != null, "view must not be null");
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");

            _view = view ?? throw new ArgumentNullException(nameof(view));
            _cliSetupApplicationService = cliSetupApplicationService
                ?? throw new ArgumentNullException(nameof(cliSetupApplicationService));
        }

        internal void Update(
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isRefreshingVersion,
            bool isRefreshingCliPathSetup,
            bool includeSkillDirectoryChecks,
            bool installSkillsFlat,
            SkillInstallState selectedTargetInstallState,
            SkillsTarget skillsTarget,
            bool isInstallingSkills)
        {
            CliSetupData cliData = CreateCliSetupData(
                needsCliPathSetup,
                isInstallingCli,
                isRefreshingVersion,
                isRefreshingCliPathSetup,
                includeSkillDirectoryChecks,
                installSkillsFlat,
                selectedTargetInstallState,
                skillsTarget,
                isInstallingSkills);
            _view.UpdateCliSetup(cliData);
        }

        internal CliSetupPrimaryAction ResolveCurrentPrimaryButtonAction(bool needsCliPathSetup)
        {
            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            string cliExecutablePath = _cliSetupApplicationService.GetCachedCliExecutablePath();
            bool canUninstallCli = _cliSetupApplicationService.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            string requiredCliVersion = _cliSetupApplicationService.GetMinimumRequiredCliVersion();
            return ResolveCliPrimaryButtonAction(
                needsCliPathSetup,
                cliVersion,
                cliIsDispatcher,
                canUninstallCli,
                requiredCliVersion);
        }

        internal static bool ShouldUninstallCliFromPrimaryButton(
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli,
            string requiredCliVersion)
        {
            bool isCliInstalled = !string.IsNullOrEmpty(cliVersion);
            bool needsUpdate = IsCliUpdateNeeded(cliVersion, cliIsDispatcher, requiredCliVersion);
            return CliSetupPrimaryActionPolicy.ShouldUninstallCli(
                isCliInstalled,
                needsUpdate,
                canUninstallCli);
        }

        internal static CliSetupPrimaryAction ResolveCliPrimaryButtonAction(
            bool needsCliPathSetup,
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli,
            string requiredCliVersion)
        {
            bool needsUpdate = IsCliUpdateNeeded(cliVersion, cliIsDispatcher, requiredCliVersion);
            bool isCliInstalled = !string.IsNullOrEmpty(cliVersion);
            return CliSetupPrimaryActionPolicy.ResolveSettingsPrimaryAction(
                needsCliPathSetup,
                needsUpdate,
                isCliInstalled,
                canUninstallCli);
        }

        internal static CliSetupPrimaryAction ResolveExecutableCliPrimaryButtonAction(
            CliSetupPrimaryAction clickedAction,
            CliSetupPrimaryAction refreshedAction)
        {
            return CliSetupPrimaryActionPolicy.ResolveExecutableSettingsAction(
                clickedAction,
                refreshedAction);
        }

        internal static bool ShouldRepairCliPathFromPrimaryButton(
            bool needsCliPathSetup,
            bool needsUpdate)
        {
            return CliSetupPrimaryActionPolicy.ShouldRepairCliPath(needsCliPathSetup, needsUpdate);
        }

        internal static bool ShouldCheckCliPathSetupForPlatform(
            RuntimePlatform platform,
            bool hasPackageOwnedCurrentUserInstall)
        {
            return CliPathSetupCheckPolicy.ShouldCheck(
                isWindowsEditor: platform == RuntimePlatform.WindowsEditor,
                hasPackageOwnedCurrentUserInstall);
        }

        internal static bool IsCliUpdateNeeded(
            string cliVersion,
            bool cliIsDispatcher,
            string requiredCliVersion)
        {
            return CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion).NeedsUpdate;
        }

        private CliSetupData CreateCliSetupData(
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isRefreshingVersion,
            bool isRefreshingCliPathSetup,
            bool includeSkillDirectoryChecks,
            bool installSkillsFlat,
            SkillInstallState selectedTargetInstallState,
            SkillsTarget skillsTarget,
            bool isInstallingSkills)
        {
            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            string cliExecutablePath = _cliSetupApplicationService.GetCachedCliExecutablePath();
            string requiredCliVersion = _cliSetupApplicationService.GetMinimumRequiredCliVersion();

            bool isCliInstalled = !string.IsNullOrEmpty(cliVersion) || needsCliPathSetup;
            bool canUninstallCli = _cliSetupApplicationService.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            bool isChecking = !_cliSetupApplicationService.IsCliCheckCompleted()
                || isRefreshingVersion
                || isRefreshingCliPathSetup
                || !includeSkillDirectoryChecks;
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion);
            bool groupSkillsUnderUnityCliLoop = !installSkillsFlat;
            SkillInstallState displayedTargetInstallState = includeSkillDirectoryChecks
                ? selectedTargetInstallState
                : SkillInstallState.Checking;

            return new CliSetupData(
                isCliInstalled,
                cliVersion,
                requiredCliVersion,
                state.NeedsUpdate,
                canUninstallCli,
                needsCliPathSetup,
                isInstallingCli,
                isChecking,
                isClaudeSkillsInstalled: false,
                isAgentsSkillsInstalled: false,
                isCursorSkillsInstalled: false,
                isGeminiSkillsInstalled: false,
                isCodexSkillsInstalled: false,
                isAntigravitySkillsInstalled: false,
                displayedTargetInstallState,
                skillsTarget,
                groupSkillsUnderUnityCliLoop,
                isInstallingSkills);
        }
    }
}
