using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Builds the CLI Setup section of the Unity Editor UI.
    /// </summary>
    public class CliSetupSection
    {
        private readonly VisualElement _cliStatusIcon;
        private readonly Label _cliStatusLabel;
        private readonly Label _cliManagedUpgradeMessage;
        private readonly Button _refreshCliVersionButton;
        private readonly Button _installCliButton;
        private readonly CliInstallProgressView _installProgressView;
        private readonly VisualElement _skillsSubsection;
        private readonly SkillsSetupPanelView _skillsSetupPanelView;

        private CliSetupData _lastData;

        public event Action OnRefreshCliVersion;
        public event Action OnInstallCli;
        public event Action OnInstallSkills;
        public event Action OnInstallAllSkills;
        public event Action OnRefreshSkillsState;
        public event Action<SkillsTarget> OnSkillsTargetChanged;
        public event Action<bool> OnGroupSkillsChanged;

        public CliSetupSection(VisualElement root)
        {
            _cliStatusIcon = root.Q<VisualElement>("cli-status-icon");
            _cliStatusLabel = root.Q<Label>("cli-status-label");
            _cliManagedUpgradeMessage = root.Q<Label>("cli-homebrew-upgrade-message");
            // Why: the warning carries a command to run, so it must be copyable.
            // UI Toolkit only starts a selection on a focusable text element.
            _cliManagedUpgradeMessage.focusable = true;
            _cliManagedUpgradeMessage.selection.isSelectable = true;
            _refreshCliVersionButton = root.Q<Button>("refresh-cli-version-button");
            _installCliButton = root.Q<Button>("install-cli-button");
            _installProgressView = new CliInstallProgressView(
                root.Q<VisualElement>("cli-install-progress"),
                _installCliButton,
                root.Q<Label>("cli-install-progress-label"));
            _skillsSubsection = root.Q<VisualElement>("skills-subsection");

            VisualElement skillsSetupPanel = root.Q<VisualElement>("skills-setup-panel");
            Debug.Assert(skillsSetupPanel != null, "skills-setup-panel must not be null");
            Button refreshSkillsStateButton = root.Q<Button>("refresh-skills-state-button");
            Debug.Assert(refreshSkillsStateButton != null, "refresh-skills-state-button must not be null");
            _skillsSetupPanelView = new SkillsSetupPanelView(
                skillsSetupPanel ?? throw new ArgumentNullException(nameof(skillsSetupPanel)),
                refreshSkillsStateButton ?? throw new ArgumentNullException(nameof(refreshSkillsStateButton)));
        }

        public void SetupBindings()
        {
            _refreshCliVersionButton.clicked += () => OnRefreshCliVersion?.Invoke();
            _installCliButton.clicked += () => OnInstallCli?.Invoke();
            _skillsSetupPanelView.OnInstallSelectedClicked += () => OnInstallSkills?.Invoke();
            _skillsSetupPanelView.OnInstallAllClicked += () => OnInstallAllSkills?.Invoke();
            _skillsSetupPanelView.OnRefreshClicked += () => OnRefreshSkillsState?.Invoke();
            _skillsSetupPanelView.OnTargetChanged += value => OnSkillsTargetChanged?.Invoke(value);
            _skillsSetupPanelView.OnGroupSkillsChanged += value => OnGroupSkillsChanged?.Invoke(value);
        }

        public void ShowInstallProgress() => _installProgressView.Show();

        public void ReportInstallProgressLine(string line) => _installProgressView.SetDetailLine(line);

        public void HideInstallProgress() => _installProgressView.Hide();

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
            UpdateSkillsSubsection(data);
            UpdateSkillsPanel(data);
        }

        private void UpdateCliStatus(CliSetupData data)
        {
            bool isInstalledIconVisible = !data.IsChecking && data.IsCliInstalled;
            bool isNotInstalledIconVisible = !data.IsChecking && !data.IsCliInstalled;
            ViewDataBinder.ToggleClass(
                _cliStatusIcon,
                "unity-cli-loop-cli-status-icon--installed",
                isInstalledIconVisible);
            ViewDataBinder.ToggleClass(
                _cliStatusIcon,
                "unity-cli-loop-cli-status-icon--not-installed",
                isNotInstalledIconVisible);

            _cliStatusLabel.text = GetCliStatusText(
                data.IsChecking,
                data.IsCliInstalled,
                data.CliVersion);
            UpdateManagedUpgradeMessage(data);
        }

        private void UpdateManagedUpgradeMessage(CliSetupData data)
        {
            bool isCliUsable = !string.IsNullOrEmpty(data.CliVersion) && !data.NeedsUpdate;
            bool isVisible = !data.IsChecking
                && ManagedCliPolicy.ShouldShowUpgradeGuidance(data.ManagedCliKind, isCliUsable);
            _cliManagedUpgradeMessage.text = isVisible
                ? CliSetupLabelFormatter.GetManagedUpgradeGuidanceText(
                    data.ManagedCliKind,
                    data.CliVersion,
                    data.RequiredCliVersion)
                : string.Empty;
            ViewDataBinder.ToggleClass(
                _cliManagedUpgradeMessage,
                "unity-cli-loop-warning-message--visible",
                isVisible);
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
                data.CanUninstallCli,
                data.NeedsCliPathSetup,
                data.ManagedCliKind,
                data.CliVersion,
                data.RequiredCliVersion);
            bool enabled = IsInstallCliButtonEnabled(
                data.IsInstallingCli,
                data.IsChecking,
                data.ManagedCliKind,
                data.NeedsCliPathSetup);
            bool isUninstallStyle = !data.NeedsCliPathSetup && IsUninstallCliAction(
                data.IsCliInstalled,
                data.NeedsUpdate,
                data.CanUninstallCli);
            bool useDisabledStyle = !enabled || isUninstallStyle;
            SetCliButton(label, enabled, useDisabledStyle);
        }

        private void SetCliButton(string text, bool enabled, bool useDisabledStyle)
        {
            _installCliButton.text = text;
            _installCliButton.SetEnabled(enabled);
            ViewDataBinder.ToggleClass(_installCliButton, "unity-cli-loop-button--disabled", useDisabledStyle);
        }

        private void UpdateSkillsSubsection(CliSetupData data)
        {
            _skillsSubsection.SetEnabled(data.IsCliInstalled);
        }

        private void UpdateSkillsPanel(CliSetupData data)
        {
            _skillsSetupPanelView.UpdateGroupSkillsToggle(
                data.GroupSkillsUnderUnityCliLoop,
                data.IsCliInstalled && !data.IsInstallingSkills);

            if (data.IsSkillStateChecking)
            {
                _skillsSetupPanelView.ShowChecking();
                return;
            }

            List<SkillSetupTargetInfo> installableTargets = data.InstallableSkillTargets == null
                ? new List<SkillSetupTargetInfo>()
                : data.InstallableSkillTargets.ToList();
            _skillsSetupPanelView.UpdateStatusPanel(
                data.IsCliInstalled,
                installableTargets,
                data.GroupSkillsUnderUnityCliLoop,
                data.IsInstallingSkills);
            _skillsSetupPanelView.UpdateSelectedTargetInstall(
                data.SelectedTarget,
                data.SelectedTargetInstallState,
                data.IsCliInstalled,
                data.IsInstallingSkills);
        }

        internal static string GetInstallCliButtonText(
            bool isCliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool canUninstallCli,
            bool needsCliPathSetup,
            ManagedCliKind managedCliKind,
            string cliVersion,
            string requiredCliVersion)
        {
            if (isChecking)
            {
                return "Checking...";
            }

            if (managedCliKind != ManagedCliKind.None)
            {
                if (isInstallingCli)
                {
                    return "Fixing PATH...";
                }

                return needsCliPathSetup
                    ? "Fix PATH"
                    : CliSetupLabelFormatter.GetManagedButtonText(managedCliKind);
            }

            bool isUninstallAction = IsUninstallCliAction(isCliInstalled, needsUpdate, canUninstallCli);
            if (isInstallingCli)
            {
                if (CliSetupPrimaryActionPolicy.ShouldRepairCliPath(needsCliPathSetup, needsUpdate))
                {
                    return "Fixing PATH...";
                }

                return isUninstallAction ? "Uninstalling..." : "Installing...";
            }

            if (needsUpdate)
            {
                return CliSetupLabelFormatter.GetCliReplacementButtonText("Update", cliVersion, requiredCliVersion);
            }

            if (needsCliPathSetup)
            {
                return "Fix PATH";
            }

            if (!isCliInstalled)
            {
                return "Install CLI";
            }

            return canUninstallCli ? "Uninstall CLI" : "Install CLI";
        }

        internal static bool IsInstallCliButtonEnabled(
            bool isInstallingCli,
            bool isChecking,
            ManagedCliKind managedCliKind,
            bool needsCliPathSetup)
        {
            if (isInstallingCli || isChecking)
            {
                return false;
            }

            return managedCliKind == ManagedCliKind.None || needsCliPathSetup;
        }

        internal static string GetCliStatusText(
            bool isChecking,
            bool isCliInstalled,
            string cliVersion)
        {
            if (isChecking)
            {
                return "CLI: Checking...";
            }

            if (!isCliInstalled || cliVersion == null)
            {
                return "CLI: Not installed";
            }

            return $"CLI: v{cliVersion}";
        }

        internal static bool IsUninstallCliAction(
            bool isCliInstalled,
            bool needsUpdate,
            bool canUninstallCli)
        {
            return CliSetupPrimaryActionPolicy.ShouldUninstallCli(
                isCliInstalled,
                needsUpdate,
                canUninstallCli);
        }
    }
}
