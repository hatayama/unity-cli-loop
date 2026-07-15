using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the CLI setup section, owns Settings primary-action mediation,
    /// and runs CLI install / path-setup / version-refresh workflows.
    /// </summary>
    internal sealed class UnityCliLoopSettingsCliSetupPresenter
    {
        private readonly UnityCliLoopSettingsWindowUI _view;
        private readonly CliSetupApplicationService _cliSetupApplicationService;

        private Func<UnityCliLoopSettingsSkillsSnapshot> _getSkillsSnapshot;
        private Action _refreshSkillsInstallStateInBackground;
        private Action<bool> _refreshAllSections;

        private bool _isInstallingCli;
        private bool _isRefreshingVersion;
        private bool _isRefreshingCliPathSetup;
        private bool _needsCliPathSetup;

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

        internal bool IsRefreshingVersion => _isRefreshingVersion;

        internal void BindCoordination(
            Func<UnityCliLoopSettingsSkillsSnapshot> getSkillsSnapshot,
            Action refreshSkillsInstallStateInBackground,
            Action<bool> refreshAllSections)
        {
            Debug.Assert(getSkillsSnapshot != null, "getSkillsSnapshot must not be null");
            Debug.Assert(
                refreshSkillsInstallStateInBackground != null,
                "refreshSkillsInstallStateInBackground must not be null");
            Debug.Assert(refreshAllSections != null, "refreshAllSections must not be null");

            _getSkillsSnapshot = getSkillsSnapshot
                ?? throw new ArgumentNullException(nameof(getSkillsSnapshot));
            _refreshSkillsInstallStateInBackground = refreshSkillsInstallStateInBackground
                ?? throw new ArgumentNullException(nameof(refreshSkillsInstallStateInBackground));
            _refreshAllSections = refreshAllSections
                ?? throw new ArgumentNullException(nameof(refreshAllSections));
        }

        internal void RefreshSection(bool includeSkillDirectoryChecks = true)
        {
            Debug.Assert(_getSkillsSnapshot != null, "BindCoordination must be called before RefreshSection");

            UnityCliLoopSettingsSkillsSnapshot skills = _getSkillsSnapshot();
            Update(
                _needsCliPathSetup,
                _isInstallingCli,
                _isRefreshingVersion,
                _isRefreshingCliPathSetup,
                includeSkillDirectoryChecks,
                skills.InstallSkillsFlat,
                skills.SelectedTargetInstallState,
                skills.SkillsTarget,
                skills.IsInstallingSkills);
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

        internal async Task RefreshCliVersionInBackground()
        {
            if (_cliSetupApplicationService.IsCliCheckCompleted())
            {
                return;
            }

            await _cliSetupApplicationService.RefreshCliVersionAsync(CancellationToken.None);
            RefreshCliPathSetupInBackground().Forget();
            RefreshSection();
            _refreshSkillsInstallStateInBackground();
        }

        internal async Task RefreshCliPathSetupInBackground()
        {
            if (_isRefreshingCliPathSetup)
            {
                return;
            }

            if (!ShouldCheckCliPathSetup())
            {
                _needsCliPathSetup = false;
                return;
            }

            _isRefreshingCliPathSetup = true;
            RefreshSection();

            try
            {
                bool isCliVisibleFromShell = await _cliSetupApplicationService.IsCliVisibleFromShellAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);
                _needsCliPathSetup = !isCliVisibleFromShell;
            }
            finally
            {
                _isRefreshingCliPathSetup = false;
                RefreshSection();
            }
        }

        internal async Task HandleRefreshCliVersion()
        {
            if (_isRefreshingVersion)
            {
                return;
            }

            _isRefreshingVersion = true;
            RefreshSection();

            try
            {
                Task forceRefresh = _cliSetupApplicationService.ForceRefreshCliVersionAsync(CancellationToken.None);
                Task minimumDelay = Task.Delay(500);
                await Task.WhenAll(forceRefresh, minimumDelay);
                RefreshCliPathSetupInBackground().Forget();
            }
            finally
            {
                _isRefreshingVersion = false;
                RefreshSection();
            }
        }

        internal async Task HandleInstallCli()
        {
            CliSetupPrimaryAction clickedAction = ResolveCurrentPrimaryButtonAction(_needsCliPathSetup);

            await RefreshCliPrimaryActionStateAsync(CancellationToken.None);
            CliSetupPrimaryAction refreshedAction = ResolveCurrentPrimaryButtonAction(_needsCliPathSetup);
            CliSetupPrimaryAction executableAction = ResolveExecutableCliPrimaryButtonAction(
                clickedAction,
                refreshedAction);
            if (executableAction == CliSetupPrimaryAction.None)
            {
                return;
            }

            if (executableAction == CliSetupPrimaryAction.RepairPath)
            {
                await HandleRepairCliPathSetup();
                return;
            }

            if (executableAction == CliSetupPrimaryAction.Uninstall)
            {
                await HandleUninstallCli();
                return;
            }

            bool wasCliInstalledBeforeInstall = _cliSetupApplicationService.IsCliInstalled();
            _needsCliPathSetup = false;
            _isInstallingCli = true;
            RefreshSection();

            try
            {
                CliInstallResult result = await _cliSetupApplicationService.InstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);

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
                        $"Failed to install uLoop CLI.\n\n{result.ErrorOutput}\n\n{manualInstallGuidance}",
                        "OK");
                    return;
                }

                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    _cliSetupApplicationService,
                    CancellationToken.None);
                await RefreshCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                _refreshAllSections(
                    CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(wasCliInstalledBeforeInstall));
            }
        }

        private async Task RefreshCliPrimaryActionStateAsync(CancellationToken ct)
        {
            _isRefreshingVersion = true;
            RefreshSection();

            try
            {
                await _cliSetupApplicationService.ForceRefreshCliVersionAsync(ct);
                await RefreshCliPathSetupAsync(ct);
            }
            finally
            {
                _isRefreshingVersion = false;
                RefreshSection();
            }
        }

        private async Task RefreshCliPathSetupAsync(CancellationToken ct)
        {
            if (!ShouldCheckCliPathSetup())
            {
                _needsCliPathSetup = false;
                return;
            }

            bool isCliVisibleFromShell = await _cliSetupApplicationService.IsCliVisibleFromShellAsync(
                UnityEngine.Application.platform,
                ct);
            _needsCliPathSetup = !isCliVisibleFromShell;
        }

        private async Task HandleRepairCliPathSetup()
        {
            _isInstallingCli = true;
            RefreshSection();

            try
            {
                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    _cliSetupApplicationService,
                    CancellationToken.None);
                await RefreshCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                _refreshAllSections(false);
            }
        }

        private async Task HandleUninstallCli()
        {
            if (!CliUninstallPrompt.ConfirmUninstall())
            {
                return;
            }

            _isInstallingCli = true;
            RefreshSection();

            try
            {
                CliInstallResult result = await _cliSetupApplicationService.UninstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog(
                        "Uninstallation Failed",
                        $"Failed to uninstall uLoop CLI.\n\n{result.ErrorOutput}",
                        "OK");
                    return;
                }
            }
            finally
            {
                _isInstallingCli = false;
                _refreshAllSections(true);
            }
        }

        private bool ShouldCheckCliPathSetup()
        {
            return ShouldCheckCliPathSetupForPlatform(
                UnityEngine.Application.platform,
                _cliSetupApplicationService.HasPackageOwnedCurrentUserInstall(UnityEngine.Application.platform));
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
