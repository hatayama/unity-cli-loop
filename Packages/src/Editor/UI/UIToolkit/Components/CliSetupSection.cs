using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop
{
    public class CliSetupSection
    {
        private readonly VisualElement _cliStatusIcon;
        private readonly Label _cliStatusLabel;
        private readonly Button _refreshCliVersionButton;
        private readonly Button _installCliButton;
        private readonly EnumField _skillsTargetField;
        private readonly Button _refreshSkillsStateButton;
        private readonly VisualElement _groupSkillsRow;
        private readonly Toggle _groupSkillsToggle;
        private readonly Label _groupSkillsLabel;
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

        public CliSetupSection(VisualElement root)
        {
            _cliStatusIcon = root.Q<VisualElement>("cli-status-icon");
            _cliStatusLabel = root.Q<Label>("cli-status-label");
            _refreshCliVersionButton = root.Q<Button>("refresh-cli-version-button");
            _installCliButton = root.Q<Button>("install-cli-button");
            _skillsTargetField = root.Q<EnumField>("skills-target-field");
            _refreshSkillsStateButton = root.Q<Button>("refresh-skills-state-button");
            _groupSkillsRow = root.Q<VisualElement>("group-skills-row");
            _groupSkillsToggle = root.Q<Toggle>("group-skills-toggle");
            _groupSkillsLabel = root.Q<Label>("group-skills-label");
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
        }

        public void Update(CliSetupData data)
        {
            if (_lastData != null && _lastData.Equals(data))
            {
                return;
            }

            _lastData = data;

            UpdateCliStatus(data);
            UpdateRefreshButton(data);
            UpdateInstallCliButton(data);
            InitializeTargetFieldIfNeeded(data);
            UpdateRefreshSkillsButton(data);
            UpdateGroupSkillsToggle(data);
            UpdateSkillsSubsection(data);
            UpdateInstallSkillsButton(data);
        }

        private void UpdateCliStatus(CliSetupData data)
        {
            if (data.IsChecking)
            {
                ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--installed", false);
                ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--not-installed", false);
                _cliStatusLabel.text = "CLI: Checking...";
                return;
            }

            ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--installed", data.IsCliInstalled);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "mcp-cli-status-icon--not-installed", !data.IsCliInstalled);

            if (data.IsCliInstalled && data.CliVersion != null)
            {
                _cliStatusLabel.text = $"CLI: v{data.CliVersion}";
                return;
            }

            _cliStatusLabel.text = "CLI: Not installed";
        }

        private void UpdateRefreshButton(CliSetupData data)
        {
            _refreshCliVersionButton.SetEnabled(!data.IsChecking);
        }

        private void UpdateInstallCliButton(CliSetupData data)
        {
            string label = GetInstallCliButtonText(
                data.IsCliInstalled,
                data.IsInstallingCli,
                data.IsChecking,
                data.NeedsUpdate,
                data.NeedsDowngrade,
                data.CanUninstallCli,
                data.CliVersion,
                data.PackageVersion);
            bool enabled = IsInstallCliButtonEnabled(
                data.IsInstallingCli,
                data.IsChecking);
            SetCliButton(label, enabled);
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
            bool enabled = data.IsCliInstalled && !data.IsChecking && !data.IsInstallingSkills;
            _refreshSkillsStateButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_refreshSkillsStateButton, "mcp-button--disabled", !enabled);
        }

        private void UpdateGroupSkillsToggle(CliSetupData data)
        {
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            ViewDataBinder.UpdateToggle(_groupSkillsToggle, data.GroupSkillsUnderUnityCliLoop);
            _groupSkillsToggle.SetEnabled(data.IsCliInstalled && !data.IsChecking && !data.IsInstallingSkills);
        }

        private void UpdateSkillsSubsection(CliSetupData data)
        {
            bool enabled = data.IsCliInstalled && !data.IsChecking;
            _skillsSubsection.SetEnabled(enabled);
        }

        private void UpdateInstallSkillsButton(CliSetupData data)
        {
            string label = GetInstallSkillsButtonText(
                data.IsCliInstalled,
                data.IsInstallingSkills,
                data.SelectedTargetInstallState);
            bool enabled = IsInstallSkillsButtonEnabled(
                data.IsCliInstalled,
                data.IsInstallingSkills,
                data.IsChecking,
                data.SelectedTargetInstallState);
            SetSkillsButton(label, enabled);
        }

        private void SetSkillsButton(string text, bool enabled)
        {
            _installSkillsButton.text = text;
            _installSkillsButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_installSkillsButton, "mcp-button--disabled", !enabled);
        }

        internal static string GetInstallCliButtonText(
            bool isCliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool needsDowngrade,
            bool canUninstallCli,
            string cliVersion,
            string packageVersion)
        {
            if (isChecking)
            {
                return "Checking...";
            }

            bool isUninstallAction = IsUninstallCliAction(isCliInstalled, needsUpdate, needsDowngrade, canUninstallCli);
            if (isInstallingCli)
            {
                return isUninstallAction ? "Uninstalling..." : "Installing...";
            }

            if (!isCliInstalled)
            {
                return "Install CLI";
            }

            if (needsUpdate)
            {
                return $"Update CLI (v{cliVersion} \u2192 v{packageVersion})";
            }

            if (needsDowngrade)
            {
                return $"Downgrade CLI (v{cliVersion} \u2192 v{packageVersion})";
            }

            return canUninstallCli ? "Uninstall CLI" : "Install CLI";
        }

        internal static bool IsInstallCliButtonEnabled(
            bool isInstallingCli,
            bool isChecking)
        {
            return !isInstallingCli && !isChecking;
        }

        internal static bool IsUninstallCliAction(
            bool isCliInstalled,
            bool needsUpdate,
            bool needsDowngrade,
            bool canUninstallCli)
        {
            return canUninstallCli && isCliInstalled && !needsUpdate && !needsDowngrade;
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
    }
}
