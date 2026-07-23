using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;
// Domain: IUnityCliLoopEditorSettingsPort / SkillSetupUseCase live across Application+Domain contracts used below.

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
        private readonly IUnityCliLoopEditorSettingsPort _editorSettingsPort;
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
            Button installCliButton,
            VisualElement installProgressContainer,
            Label installProgressLabel,
            VisualElement groupSkillsRow,
            EnumField skillsTargetField,
            Toggle groupSkillsToggle,
            Label groupSkillsLabel,
            VisualElement skillsTargetRow,
            VisualElement skillsTargetList,
            VisualElement skillsStatusDivider,
            Label skillsStatusLabel,
            Button installSkillsButton,
            Toggle suppressAutoShowToggle,
            SkillSetupUseCase skillSetupUseCase,
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            CliSetupApplicationService cliSetupApplicationService,
            Action scheduleResizeToContent,
            string lastSeenSetupWizardVersionBeforeOpen)
        {
            Debug.Assert(rootVisualElement != null, "rootVisualElement must not be null");
            Debug.Assert(nodejsWarning != null, "nodejsWarning must not be null");
            Debug.Assert(nodejsOk != null, "nodejsOk must not be null");
            Debug.Assert(suppressAutoShowToggle != null, "suppressAutoShowToggle must not be null");
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            Debug.Assert(scheduleResizeToContent != null, "scheduleResizeToContent must not be null");

            _rootVisualElement = rootVisualElement
                ?? throw new ArgumentNullException(nameof(rootVisualElement));
            _nodejsWarning = nodejsWarning ?? throw new ArgumentNullException(nameof(nodejsWarning));
            _nodejsOk = nodejsOk ?? throw new ArgumentNullException(nameof(nodejsOk));
            _suppressAutoShowToggle = suppressAutoShowToggle
                ?? throw new ArgumentNullException(nameof(suppressAutoShowToggle));
            _editorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
            _scheduleResizeToContent = scheduleResizeToContent
                ?? throw new ArgumentNullException(nameof(scheduleResizeToContent));

            _cliWorkflow = new SetupWizardCliWorkflowController(
                cliStatusIcon,
                cliStatusLabel,
                installCliButton,
                installProgressContainer,
                installProgressLabel,
                cliSetupApplicationService,
                RefreshUI);
            _skillsWorkflow = new SetupWizardSkillsWorkflowController(
                groupSkillsRow,
                skillsTargetField,
                groupSkillsToggle,
                groupSkillsLabel,
                skillsTargetRow,
                skillsTargetList,
                skillsStatusDivider,
                skillsStatusLabel,
                installSkillsButton,
                skillSetupUseCase,
                editorSettingsPort,
                cliSetupApplicationService,
                scheduleResizeToContent,
                lastSeenSetupWizardVersionBeforeOpen);
        }

        internal void InitializeSkillsTargetField()
        {
            _skillsWorkflow.InitializeSkillsTargetField();
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
