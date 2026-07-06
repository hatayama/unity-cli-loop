using System;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the CLI setup section in the Unity CLI Loop settings window.
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

            bool isCliInstalled = cliVersion != null || needsCliPathSetup;
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
