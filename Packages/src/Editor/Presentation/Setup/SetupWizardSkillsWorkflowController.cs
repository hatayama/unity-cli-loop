using System;
using System.Collections.Generic;
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
    /// Owns Setup Wizard skills-step workflow state and async install/refresh operations.
    /// </summary>
    internal sealed class SetupWizardSkillsWorkflowController
    {
        private readonly SkillsSetupPanelView _skillsSetupPanelView;
        private readonly SkillSetupUseCase _skillSetupUseCase;
        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private readonly CliSetupApplicationService _cliSetupApplicationService;
        private readonly Action _scheduleResizeToContent;

        private bool _isInstallingSkills;
        private bool _installSkillsFlat;
        private CancellationTokenSource _skillInstallStateRefreshCts;
        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private List<SkillSetupTargetInfo> _latestTargets = new();

        internal SetupWizardSkillsWorkflowController(
            SkillsSetupPanelView skillsSetupPanelView,
            SkillSetupUseCase skillSetupUseCase,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            CliSetupApplicationService cliSetupApplicationService,
            Action scheduleResizeToContent)
        {
            Debug.Assert(skillsSetupPanelView != null, "skillsSetupPanelView must not be null");
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");
            Debug.Assert(scheduleResizeToContent != null, "scheduleResizeToContent must not be null");

            _skillsSetupPanelView = skillsSetupPanelView
                ?? throw new ArgumentNullException(nameof(skillsSetupPanelView));
            _skillSetupUseCase = skillSetupUseCase
                ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
            _editorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
            _cliSetupApplicationService = cliSetupApplicationService
                ?? throw new ArgumentNullException(nameof(cliSetupApplicationService));
            _scheduleResizeToContent = scheduleResizeToContent
                ?? throw new ArgumentNullException(nameof(scheduleResizeToContent));

            _skillsSetupPanelView.OnInstallAllClicked += HandleInstallAllSkills;
            _skillsSetupPanelView.OnInstallSelectedClicked += HandleInstallSelectedSkills;
            _skillsSetupPanelView.OnRefreshClicked += RefreshSkillsSection;
            _skillsSetupPanelView.OnTargetChanged += HandleTargetChanged;
            _skillsSetupPanelView.OnGroupSkillsChanged += HandleGroupSkillsChanged;
        }

        internal void InitializeGroupSkillsToggle()
        {
            ApplyFlatSkillInstallPreference();
            _skillsSetupPanelView.UpdateGroupSkillsToggle(
                groupSkillsUnderUnityCliLoop: !_installSkillsFlat,
                isEnabled: false);
        }

        internal void ShowChecking()
        {
            _skillsSetupPanelView.UpdateGroupSkillsToggle(
                groupSkillsUnderUnityCliLoop: !_installSkillsFlat,
                isEnabled: false);
            _skillsSetupPanelView.ShowChecking();
        }

        internal void RefreshSkillsSection()
        {
            string cachedCliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            bool cliInstalled = IsCliInstalled(cachedCliVersion);
            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargetsFast(projectRoot);
            bool canManageSkills = SetupWizardWindow.CanManageSkills(cliInstalled);
            UpdateSkillsStep(canManageSkills, targets);
            BeginRefreshDisplayedSkillTargets(canManageSkills);
            _scheduleResizeToContent();
        }

        internal void ApplyFastSkillsState(bool cliInstalled)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargetsFast(projectRoot);
            bool canManageSkills = SetupWizardWindow.CanManageSkills(cliInstalled);
            UpdateSkillsStep(canManageSkills, targets);
            BeginRefreshDisplayedSkillTargets(canManageSkills);
        }

        internal void CancelSkillInstallStateRefresh()
        {
            if (_skillInstallStateRefreshCts == null)
            {
                return;
            }

            _skillInstallStateRefreshCts.Cancel();
            _skillInstallStateRefreshCts.Dispose();
            _skillInstallStateRefreshCts = null;
        }

        private List<SkillSetupTargetInfo> DetectDisplayedSkillTargets(string projectRoot)
        {
            return _skillSetupUseCase.DetectSkillTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat);
        }

        private List<SkillSetupTargetInfo> DetectDisplayedSkillTargetsFast(string projectRoot)
        {
            return _skillSetupUseCase.DetectSkillTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
        }

        private void BeginRefreshDisplayedSkillTargets(bool canManageSkills)
        {
            CancelSkillInstallStateRefresh();
            if (!canManageSkills || _isInstallingSkills)
            {
                return;
            }

            CancellationTokenSource cts = new();
            _skillInstallStateRefreshCts = cts;
            RefreshDisplayedSkillTargetsAsync(cts.Token).Forget();
        }

        private async Task RefreshDisplayedSkillTargetsAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets =
                await Task.Run(() => DetectDisplayedSkillTargets(projectRoot));
            if (ct.IsCancellationRequested)
            {
                return;
            }

            UpdateSkillsStep(canManageSkills: true, targets);
            _scheduleResizeToContent();
        }

        private void UpdateSkillsStep(
            bool canManageSkills,
            List<SkillSetupTargetInfo> targets)
        {
            _latestTargets = targets;
            List<SkillSetupTargetInfo> installableTargets =
                SkillsSetupPanelView.FilterInstallableSkillTargets(targets);
            bool groupSkillsUnderUnityCliLoop = !_installSkillsFlat;
            _skillsSetupPanelView.UpdateGroupSkillsToggle(
                groupSkillsUnderUnityCliLoop,
                canManageSkills && !_isInstallingSkills);
            _skillsSetupPanelView.UpdateStatusPanel(
                canManageSkills,
                installableTargets,
                groupSkillsUnderUnityCliLoop,
                _isInstallingSkills);

            SkillSetupTargetInfo selectedTargetInfo = SkillsSetupPanelView.GetSelectedSkillTargetInfo(
                targets,
                _skillsTarget,
                groupSkillsUnderUnityCliLoop);
            _skillsSetupPanelView.UpdateSelectedTargetInstall(
                _skillsTarget,
                selectedTargetInfo.InstallState,
                isCliInstalled: canManageSkills,
                _isInstallingSkills);
        }

        private void HandleInstallAllSkills()
        {
            HandleInstallSkillsAsync(isBulkInstall: true, CancellationToken.None).Forget();
        }

        private void HandleInstallSelectedSkills()
        {
            HandleInstallSkillsAsync(isBulkInstall: false, CancellationToken.None).Forget();
        }

        private async Task HandleInstallSkillsAsync(bool isBulkInstall, CancellationToken ct)
        {
            CancelSkillInstallStateRefresh();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargets(projectRoot);
            bool groupSkillsUnderUnityCliLoop = !_installSkillsFlat;
            List<SkillSetupTargetInfo> installableTargets = isBulkInstall
                ? SkillsSetupPanelView.FilterInstallableSkillTargets(targets)
                : SkillsSetupPanelView.BuildSingleTargetInstallList(
                    targets,
                    _skillsTarget,
                    groupSkillsUnderUnityCliLoop);
            if (installableTargets.Count == 0)
            {
                return;
            }

            bool shouldShowSkillsInstalledDialog =
                SkillInstallDialogPolicy.ShouldShowForInstallableTargets(installableTargets);
            _isInstallingSkills = true;
            UpdateSkillsStep(true, targets);

            try
            {
                await _skillSetupUseCase.InstallSkillFilesAsync(
                    installableTargets,
                    groupSkillsUnderUnityCliLoop,
                    ct);
                if (shouldShowSkillsInstalledDialog)
                {
                    EditorDialogHelper.ShowSkillsInstalledDialog();
                }
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshSkillsSection();
            }
        }

        private void HandleTargetChanged(SkillsTarget newTarget)
        {
            _skillsTarget = newTarget;
            string cachedCliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool canManageSkills = SetupWizardWindow.CanManageSkills(IsCliInstalled(cachedCliVersion));
            UpdateSkillsStep(canManageSkills, _latestTargets);
            _scheduleResizeToContent();
        }

        private void HandleGroupSkillsChanged(bool _)
        {
            ApplyFlatSkillInstallPreference();
            RefreshSkillsSection();
        }

        private void ApplyFlatSkillInstallPreference()
        {
            // Claude Code does not resolve nested skill folders, so setup keeps every editor target on the flat layout.
            _installSkillsFlat = SetupWizardWindow.ForceFlatSkillInstall;
            _editorSettingsPort.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private static bool IsCliInstalled(string cliVersion)
        {
            return !string.IsNullOrEmpty(cliVersion);
        }
    }
}
