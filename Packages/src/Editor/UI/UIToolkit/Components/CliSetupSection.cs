using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    public class CliSetupSection
    {
        private readonly VisualElement _cliStatusIcon;
        private readonly Label _cliStatusLabel;
        private readonly Button _refreshCliVersionButton;
        private readonly Button _installCliButton;
        private readonly EnumField _skillsTargetField;
        private readonly Button _installSkillsButton;
        private readonly VisualElement _skillsSubsection;

        private CliSetupData _lastData;
        private bool _isTargetFieldInitialized;

        public event Action OnRefreshCliVersion;
        public event Action OnInstallCli;
        public event Action OnInstallSkills;
        public event Action<SkillsTarget> OnSkillsTargetChanged;

        public CliSetupSection(VisualElement root)
        {
            _cliStatusIcon = root.Q<VisualElement>("cli-status-icon");
            _cliStatusLabel = root.Q<Label>("cli-status-label");
            _refreshCliVersionButton = root.Q<Button>("refresh-cli-version-button");
            _installCliButton = root.Q<Button>("install-cli-button");
            _skillsTargetField = root.Q<EnumField>("skills-target-field");
            _installSkillsButton = root.Q<Button>("install-skills-button");
            _skillsSubsection = root.Q<VisualElement>("skills-subsection");
        }

        public void SetupBindings()
        {
            _refreshCliVersionButton.clicked += () => OnRefreshCliVersion?.Invoke();
            _installCliButton.clicked += () => OnInstallCli?.Invoke();
            _installSkillsButton.clicked += () => OnInstallSkills?.Invoke();
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

        private void UpdateSkillsSubsection(CliSetupData data)
        {
            bool enabled = data.IsCliInstalled && !data.IsChecking;
            _skillsSubsection.SetEnabled(enabled);
        }

        private void UpdateInstallSkillsButton(CliSetupData data)
        {
            if (data.IsInstallingSkills)
            {
                SetSkillsButton("Installing...", false);
                return;
            }

            if (!data.IsCliInstalled)
            {
                SetSkillsButton("Install Skills", false);
                return;
            }

            bool hasSkills = data.SelectedTarget switch
            {
                SkillsTarget.Claude => data.IsClaudeSkillsInstalled,
                SkillsTarget.Agents => data.IsAgentsSkillsInstalled,
                _ => false
            };

            string label = hasSkills ? "Update Skills" : "Install Skills";
            SetSkillsButton(label, true);
        }

        private void SetSkillsButton(string text, bool enabled)
        {
            _installSkillsButton.text = text;
            _installSkillsButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_installSkillsButton, "mcp-button--disabled", !enabled);
        }
    }
}
