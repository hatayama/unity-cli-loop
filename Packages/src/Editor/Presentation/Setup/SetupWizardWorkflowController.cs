using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Owns Setup Wizard workflow orchestration across CLI and skills steps.
    /// </summary>
    internal sealed class SetupWizardWorkflowController
    {
        private readonly VisualElement _rootVisualElement;
        private readonly VisualElement _nodejsWarning;
        private readonly VisualElement _nodejsOk;
        private readonly Toggle _suppressAutoShowToggle;
        private readonly Toggle _suppressProjectAutoShowToggle;
        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private readonly IUnityCliLoopProjectSettingsPort _projectSettingsPort;
        private readonly Action _scheduleResizeToContent;
        private readonly SetupWizardCliWorkflowController _cliWorkflow;
        private readonly SetupWizardSkillsWorkflowController _skillsWorkflow;

        private IVisualElementScheduledItem _initialRefreshScheduledItem;

        internal SetupWizardWorkflowController(
            VisualElement rootVisualElement,
            VisualElement nodejsWarning,
            VisualElement nodejsOk,
            VisualElement cliStatusIcon,
            Label cliStatusLabel,
            Label cliHomebrewUpgradeMessage,
            Button installCliButton,
            VisualElement installProgressContainer,
            Label installProgressLabel,
            SkillsSetupPanelView skillsSetupPanelView,
            Toggle suppressAutoShowToggle,
            Toggle suppressProjectAutoShowToggle,
            SkillSetupUseCase skillSetupUseCase,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            IUnityCliLoopProjectSettingsPort projectSettingsPort,
            CliSetupApplicationService cliSetupApplicationService,
            Action scheduleResizeToContent)
        {
            Debug.Assert(rootVisualElement != null, "rootVisualElement must not be null");
            Debug.Assert(nodejsWarning != null, "nodejsWarning must not be null");
            Debug.Assert(nodejsOk != null, "nodejsOk must not be null");
            Debug.Assert(skillsSetupPanelView != null, "skillsSetupPanelView must not be null");
            Debug.Assert(suppressAutoShowToggle != null, "suppressAutoShowToggle must not be null");
            Debug.Assert(
                suppressProjectAutoShowToggle != null,
                "suppressProjectAutoShowToggle must not be null");
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            Debug.Assert(projectSettingsPort != null, "projectSettingsPort must not be null");
            Debug.Assert(scheduleResizeToContent != null, "scheduleResizeToContent must not be null");

            _rootVisualElement = rootVisualElement
                ?? throw new ArgumentNullException(nameof(rootVisualElement));
            _nodejsWarning = nodejsWarning ?? throw new ArgumentNullException(nameof(nodejsWarning));
            _nodejsOk = nodejsOk ?? throw new ArgumentNullException(nameof(nodejsOk));
            _suppressAutoShowToggle = suppressAutoShowToggle
                ?? throw new ArgumentNullException(nameof(suppressAutoShowToggle));
            _suppressProjectAutoShowToggle = suppressProjectAutoShowToggle
                ?? throw new ArgumentNullException(nameof(suppressProjectAutoShowToggle));
            _editorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
            _projectSettingsPort = projectSettingsPort
                ?? throw new ArgumentNullException(nameof(projectSettingsPort));
            _scheduleResizeToContent = scheduleResizeToContent
                ?? throw new ArgumentNullException(nameof(scheduleResizeToContent));

            _cliWorkflow = new SetupWizardCliWorkflowController(
                cliStatusIcon,
                cliStatusLabel,
                cliHomebrewUpgradeMessage,
                installCliButton,
                installProgressContainer,
                installProgressLabel,
                cliSetupApplicationService,
                RefreshUI);
            _skillsWorkflow = new SetupWizardSkillsWorkflowController(
                skillsSetupPanelView,
                skillSetupUseCase,
                editorSettingsPort,
                cliSetupApplicationService,
                scheduleResizeToContent);
        }

        internal void InitializeGroupSkillsToggle()
        {
            _skillsWorkflow.InitializeGroupSkillsToggle();
        }

        internal void ApplyInitialCheckingState()
        {
            RefreshAutoShowToggle();
            ViewDataBinder.SetVisible(_nodejsWarning, false);
            ViewDataBinder.SetVisible(_nodejsOk, false);
            _cliWorkflow.ShowChecking();
            _skillsWorkflow.ShowChecking();
        }

        internal void ScheduleInitialRefresh()
        {
            _initialRefreshScheduledItem?.Pause();
            _initialRefreshScheduledItem = _rootVisualElement.schedule.Execute(() => RefreshUI()).StartingIn(0);
        }

        internal void PauseInitialRefresh()
        {
            _initialRefreshScheduledItem?.Pause();
        }

        internal void CancelSkillInstallStateRefresh()
        {
            _skillsWorkflow.CancelSkillInstallStateRefresh();
        }

        internal void RefreshUI(bool refreshSkillsSection = true)
        {
            RefreshUIAsync(refreshSkillsSection, CancellationToken.None).Forget();
        }

        private void RefreshAutoShowToggle()
        {
            _suppressAutoShowToggle.SetValueWithoutNotify(_editorSettingsPort.GetSuppressSetupWizardAutoShow());
            _suppressProjectAutoShowToggle.SetValueWithoutNotify(
                _projectSettingsPort.GetSuppressSetupWizardAutoShow());
        }

        private async Task RefreshUIAsync(
            bool refreshSkillsSection,
            CancellationToken ct)
        {
            CancelSkillInstallStateRefresh();
            ct.ThrowIfCancellationRequested();
            RefreshAutoShowToggle();
            ViewDataBinder.SetVisible(_nodejsWarning, false);
            ViewDataBinder.SetVisible(_nodejsOk, false);
            _cliWorkflow.ShowChecking();
            if (refreshSkillsSection)
            {
                _skillsWorkflow.ShowChecking();
            }

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            ViewDataBinder.SetVisible(_nodejsWarning, false);
            ViewDataBinder.SetVisible(_nodejsOk, false);

            bool cliInstalled = await _cliWorkflow.RefreshAndUpdateAsync(ct);

            if (!refreshSkillsSection)
            {
                _scheduleResizeToContent();
                return;
            }

            _skillsWorkflow.ApplyFastSkillsState(cliInstalled);
            _scheduleResizeToContent();
        }
    }
}
