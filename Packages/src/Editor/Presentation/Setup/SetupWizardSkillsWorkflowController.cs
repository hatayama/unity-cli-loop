using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.UIElements;
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
        private readonly VisualElement _groupSkillsRow;
        private readonly EnumField _skillsTargetField;
        private readonly Toggle _groupSkillsToggle;
        private readonly Label _groupSkillsLabel;
        private readonly SetupWizardSkillsStepPresenter _skillsStepPresenter;
        private readonly SkillSetupUseCase _skillSetupUseCase;
        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private readonly CliSetupApplicationService _cliSetupApplicationService;
        private readonly Action _scheduleResizeToContent;

        private bool _isInstallingSkills;
        private bool _isSkillsTargetFieldInitialized;
        private bool _shouldUseFirstInstallSkillsUi;
        private bool _installSkillsFlat;
        private CancellationTokenSource _skillInstallStateRefreshCts;
        private SkillsTarget _skillsTarget = SkillsTarget.Claude;

        internal SetupWizardSkillsWorkflowController(
            VisualElement groupSkillsRow,
            EnumField skillsTargetField,
            Toggle groupSkillsToggle,
            Label groupSkillsLabel,
            VisualElement skillsTargetRow,
            VisualElement skillsTargetList,
            VisualElement skillsStatusDivider,
            Label skillsStatusLabel,
            Button installSkillsButton,
            SkillSetupUseCase skillSetupUseCase,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            CliSetupApplicationService cliSetupApplicationService,
            Action scheduleResizeToContent,
            string lastSeenSetupWizardVersionBeforeOpen)
        {
            Debug.Assert(groupSkillsRow != null, "groupSkillsRow must not be null");
            Debug.Assert(skillsTargetField != null, "skillsTargetField must not be null");
            Debug.Assert(groupSkillsToggle != null, "groupSkillsToggle must not be null");
            Debug.Assert(groupSkillsLabel != null, "groupSkillsLabel must not be null");
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");
            Debug.Assert(scheduleResizeToContent != null, "scheduleResizeToContent must not be null");

            _groupSkillsRow = groupSkillsRow ?? throw new ArgumentNullException(nameof(groupSkillsRow));
            _skillsTargetField = skillsTargetField
                ?? throw new ArgumentNullException(nameof(skillsTargetField));
            _groupSkillsToggle = groupSkillsToggle
                ?? throw new ArgumentNullException(nameof(groupSkillsToggle));
            _groupSkillsLabel = groupSkillsLabel
                ?? throw new ArgumentNullException(nameof(groupSkillsLabel));
            _skillSetupUseCase = skillSetupUseCase
                ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
            _editorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
            _cliSetupApplicationService = cliSetupApplicationService
                ?? throw new ArgumentNullException(nameof(cliSetupApplicationService));
            _scheduleResizeToContent = scheduleResizeToContent
                ?? throw new ArgumentNullException(nameof(scheduleResizeToContent));

            _skillsStepPresenter = new SetupWizardSkillsStepPresenter(
                skillsTargetRow,
                skillsTargetList,
                skillsStatusDivider,
                skillsStatusLabel,
                installSkillsButton,
                HandleInstallSkills);

            InitializeFirstInstallSkillsUiState(lastSeenSetupWizardVersionBeforeOpen);
        }

        internal void InitializeSkillsTargetField()
        {
            if (_isSkillsTargetFieldInitialized) return;

            _skillsTargetField.Init(_skillsTarget);
            _skillsTargetField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillsTarget newTarget)
                {
                    _skillsTarget = newTarget;
                    RefreshSkillsSection();
                }
            });
            _isSkillsTargetFieldInitialized = true;
        }

        internal void InitializeGroupSkillsToggle()
        {
            ApplyFlatSkillInstallPreference();
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            _groupSkillsToggle.SetValueWithoutNotify(!_installSkillsFlat);
            _groupSkillsToggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                ApplyFlatSkillInstallPreference();
                RefreshSkillsSection();
            });
            _groupSkillsLabel.RegisterCallback<ClickEvent>(HandleGroupSkillsRowClicked);
        }

        internal void ShowChecking()
        {
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            _groupSkillsToggle.SetEnabled(false);
            _skillsStepPresenter.ShowChecking(_shouldUseFirstInstallSkillsUi);
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

        private void InitializeFirstInstallSkillsUiState(string lastSeenSetupWizardVersionBeforeOpen)
        {
            _shouldUseFirstInstallSkillsUi = SetupWizardWindow.ShouldUseFirstInstallSkillsUi(
                lastSeenSetupWizardVersionBeforeOpen);
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
            _groupSkillsToggle.SetEnabled(canManageSkills && !_isInstallingSkills);
            _skillsStepPresenter.Update(
                canManageSkills,
                targets,
                _shouldUseFirstInstallSkillsUi,
                _skillsTarget,
                !_installSkillsFlat,
                _isInstallingSkills);
        }

        private void HandleInstallSkills()
        {
            HandleInstallSkillsAsync(CancellationToken.None).Forget();
        }

        private async Task HandleInstallSkillsAsync(CancellationToken ct)
        {
            CancelSkillInstallStateRefresh();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargets(projectRoot);
            List<SkillSetupTargetInfo> installableTargets = _shouldUseFirstInstallSkillsUi
                ? SetupWizardSkillsStepPresenter.GetFirstInstallableSkillTargets(
                    targets,
                    _skillsTarget,
                    !_installSkillsFlat)
                : SetupWizardSkillsStepPresenter.FilterInstallableSkillTargets(targets);
            if (installableTargets.Count == 0) return;

            bool shouldShowSkillsInstalledDialog =
                SkillInstallDialogPolicy.ShouldShowForInstallableTargets(installableTargets);
            _isInstallingSkills = true;
            UpdateSkillsStep(true, targets);

            try
            {
                await _skillSetupUseCase.InstallSkillFilesAsync(
                    installableTargets,
                    !_installSkillsFlat,
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

        private void HandleGroupSkillsRowClicked(ClickEvent evt)
        {
            evt.StopPropagation();
            if (!_groupSkillsToggle.enabledSelf)
            {
                return;
            }

            if (evt.target is VisualElement targetElement && _groupSkillsToggle.Contains(targetElement))
            {
                return;
            }

            bool newValue = !_groupSkillsToggle.value;
            _groupSkillsToggle.SetValueWithoutNotify(newValue);
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
