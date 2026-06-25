using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    public class CliSetupSection
    {
        private readonly VisualElement _cliStatusIcon;
        private readonly VisualElement _globalCliStatusRow;
        private readonly Label _cliStatusLabel;
        private readonly Button _refreshCliVersionButton;
        private readonly Button _installCliButton;
        private readonly VisualElement _globalCliSeparator;
        private readonly EnumField _skillsTargetField;
        private readonly Button _refreshSkillsStateButton;
        private readonly VisualElement _groupSkillsRow;
        private readonly Toggle _groupSkillsToggle;
        private readonly Label _groupSkillsLabel;
        private readonly VisualElement _projectCliVersionRow;
        private readonly Toggle _projectCliVersionToggle;
        private readonly Button _installSkillsButton;
        private readonly VisualElement _skillsSubsection;

        private CliSetupData _lastData;
        private bool _isTargetFieldInitialized;

        public event Action OnRefreshCliVersion;
        public event Action OnInstallCli;
        public event Action OnInstallSkills;
        public event Action OnRefreshSkillsState;
        public event Action<SkillsTarget> OnSkillsTargetChanged;
        public event Action<bool> OnGroupSkillsChanged;
        public event Action<bool> OnUseProjectCliVersionChanged;

        public CliSetupSection(VisualElement root)
        {
            _cliStatusIcon = root.Q<VisualElement>("cli-status-icon");
            _globalCliStatusRow = root.Q<VisualElement>("global-cli-status-row");
            _cliStatusLabel = root.Q<Label>("cli-status-label");
            _refreshCliVersionButton = root.Q<Button>("refresh-cli-version-button");
            _installCliButton = root.Q<Button>("install-cli-button");
            _globalCliSeparator = root.Q<VisualElement>("global-cli-separator");
            _skillsTargetField = root.Q<EnumField>("skills-target-field");
            _refreshSkillsStateButton = root.Q<Button>("refresh-skills-state-button");
            _groupSkillsRow = root.Q<VisualElement>("group-skills-row");
            _groupSkillsToggle = root.Q<Toggle>("group-skills-toggle");
            _groupSkillsLabel = root.Q<Label>("group-skills-label");
            _projectCliVersionRow = root.Q<VisualElement>("project-cli-version-row");
            _projectCliVersionToggle = root.Q<Toggle>("project-cli-version-toggle");
            _installSkillsButton = root.Q<Button>("install-skills-button");
            _skillsSubsection = root.Q<VisualElement>("skills-subsection");
        }

        public void SetupBindings()
        {
            _refreshCliVersionButton.clicked += () => OnRefreshCliVersion?.Invoke();
            _installCliButton.clicked += () => OnInstallCli?.Invoke();
            _installSkillsButton.clicked += () => OnInstallSkills?.Invoke();
            _refreshSkillsStateButton.clicked += () => OnRefreshSkillsState?.Invoke();
            _groupSkillsToggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                OnGroupSkillsChanged?.Invoke(evt.newValue);
            });
            _groupSkillsRow.RegisterCallback<ClickEvent>(HandleGroupSkillsRowClicked);
            _projectCliVersionToggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                OnUseProjectCliVersionChanged?.Invoke(evt.newValue);
            });
            _projectCliVersionRow.RegisterCallback<ClickEvent>(HandleProjectCliVersionRowClicked);
        }

        public void Update(CliSetupData data)
        {
            if (_lastData != null && _lastData.Equals(data))
            {
                return;
            }

            _lastData = data;

            UpdateCliStatus(data);
            UpdateGlobalCliVisibility(data);
            UpdateRefreshButton(data);
            UpdateInstallCliButton(data);
            InitializeTargetFieldIfNeeded(data);
            UpdateRefreshSkillsButton(data);
            UpdateGroupSkillsToggle(data);
            UpdateProjectCliVersionToggle(data);
            UpdateSkillsSubsection(data);
            UpdateInstallSkillsButton(data);
        }

        private void UpdateCliStatus(CliSetupData data)
        {
            if (data.IsChecking)
            {
                ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--installed", false);
                ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--not-installed", false);
                _cliStatusLabel.text = "uloop-cli: Checking...";
                return;
            }

            ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--installed", data.IsCliInstalled);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--not-installed", !data.IsCliInstalled);

            if (data.IsCliInstalled && data.CliVersion != null)
            {
                _cliStatusLabel.text = $"uloop-cli: v{data.CliVersion}";
                return;
            }

            _cliStatusLabel.text = "uloop-cli: Not installed";
        }

        private void UpdateRefreshButton(CliSetupData data)
        {
            _refreshCliVersionButton.SetEnabled(!data.IsChecking);
        }

        private void UpdateGlobalCliVisibility(CliSetupData data)
        {
            bool visible = !data.UseProjectCliVersion;
            ViewDataBinder.SetVisible(_globalCliStatusRow, visible);
            ViewDataBinder.SetVisible(_installCliButton, visible);
            ViewDataBinder.SetVisible(_globalCliSeparator, visible);
        }

        private void UpdateInstallCliButton(CliSetupData data)
        {
            if (data.IsChecking)
            {
                SetCliButton("Checking...", false);
                return;
            }

            if (data.IsInstallingCli)
            {
                SetCliButton("Installing...", false);
                return;
            }

            if (!data.IsCliInstalled)
            {
                SetCliButton("Install CLI", true);
                return;
            }

            if (data.NeedsUpdate)
            {
                SetCliButton($"Update CLI (v{data.CliVersion} \u2192 v{data.PackageVersion})", true);
                return;
            }

            if (data.NeedsDowngrade)
            {
                SetCliButton($"Downgrade CLI (v{data.CliVersion} \u2192 v{data.PackageVersion})", true);
                return;
            }

            SetCliButton("Up to date", false);
        }

        private void SetCliButton(string text, bool enabled)
        {
            _installCliButton.text = text;
            _installCliButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_installCliButton, "mcp-button--disabled", !enabled);
        }

        private void InitializeTargetFieldIfNeeded(CliSetupData data)
        {
            if (!_isTargetFieldInitialized)
            {
                _skillsTargetField.Init(data.SelectedTarget);
                _skillsTargetField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is SkillsTarget newValue)
                    {
                        OnSkillsTargetChanged?.Invoke(newValue);
                    }
                });
                _isTargetFieldInitialized = true;
            }
            else
            {
                ViewDataBinder.UpdateEnumField(_skillsTargetField, data.SelectedTarget);
            }
        }

        private void UpdateRefreshSkillsButton(CliSetupData data)
        {
            bool canManageSkills = CanManageSkills(data);
            bool enabled = canManageSkills
                && !data.IsChecking
                && !data.IsInstallingCli
                && !data.IsInstallingSkills;
            _refreshSkillsStateButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_refreshSkillsStateButton, "mcp-button--disabled", !enabled);
        }

        private void UpdateGroupSkillsToggle(CliSetupData data)
        {
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            ViewDataBinder.UpdateToggle(_groupSkillsToggle, data.GroupSkillsUnderUnityCliLoop);
            _groupSkillsToggle.SetEnabled(CanManageSkills(data)
                && !data.IsChecking
                && !data.IsInstallingCli
                && !data.IsInstallingSkills);
        }

        private void UpdateProjectCliVersionToggle(CliSetupData data)
        {
            ViewDataBinder.UpdateToggle(_projectCliVersionToggle, data.UseProjectCliVersion);
            _projectCliVersionToggle.SetEnabled(!data.IsChecking
                && !data.IsInstallingCli
                && !data.IsInstallingSkills);
        }

        private void UpdateSkillsSubsection(CliSetupData data)
        {
            bool enabled = CanManageSkills(data) && !data.IsChecking && !data.IsInstallingCli;
            _skillsSubsection.SetEnabled(enabled);
        }

        private void UpdateInstallSkillsButton(CliSetupData data)
        {
            string label = GetInstallSkillsButtonText(
                CanManageSkills(data),
                data.IsInstallingSkills,
                data.SelectedTargetInstallState);
            bool enabled = IsInstallSkillsButtonEnabled(
                CanManageSkills(data),
                data.IsInstallingSkills,
                data.IsChecking || data.IsInstallingCli,
                data.SelectedTargetInstallState);
            SetSkillsButton(label, enabled);
        }

        private static bool CanManageSkills(CliSetupData data)
        {
            return data.IsCliInstalled || data.UseProjectCliVersion;
        }

        private void SetSkillsButton(string text, bool enabled)
        {
            _installSkillsButton.text = text;
            _installSkillsButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_installSkillsButton, "mcp-button--disabled", !enabled);
        }

        internal static string GetInstallSkillsButtonText(
            bool isCliInstalled,
            bool isInstallingSkills,
            SkillInstallState installState)
        {
            if (isInstallingSkills)
            {
                return "Installing...";
            }

            if (!isCliInstalled)
            {
                return "Install Skills";
            }

            return installState switch
            {
                SkillInstallState.Checking => "Checking...",
                SkillInstallState.Installed => "Installed",
                SkillInstallState.Outdated => "Update Skills",
                _ => "Install Skills"
            };
        }

        internal static bool IsInstallSkillsButtonEnabled(
            bool isCliInstalled,
            bool isInstallingSkills,
            bool isChecking,
            SkillInstallState installState)
        {
            if (!isCliInstalled || isInstallingSkills || isChecking)
            {
                return false;
            }

            return installState switch
            {
                SkillInstallState.Checking => false,
                SkillInstallState.Installed => false,
                _ => true
            };
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
            OnGroupSkillsChanged?.Invoke(newValue);
        }

        private void HandleProjectCliVersionRowClicked(ClickEvent evt)
        {
            evt.StopPropagation();
            if (!_projectCliVersionToggle.enabledSelf)
            {
                return;
            }

            if (evt.target is VisualElement targetElement && _projectCliVersionToggle.Contains(targetElement))
            {
                return;
            }

            bool newValue = !_projectCliVersionToggle.value;
            _projectCliVersionToggle.SetValueWithoutNotify(newValue);
            OnUseProjectCliVersionChanged?.Invoke(newValue);
        }
    }
}
