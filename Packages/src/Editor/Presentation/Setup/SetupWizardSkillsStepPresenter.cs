using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the setup wizard skill installation step.
    /// </summary>
    internal sealed class SetupWizardSkillsStepPresenter
    {
        private readonly VisualElement _skillsTargetRow;
        private readonly VisualElement _skillsTargetList;
        private readonly VisualElement _skillsStatusDivider;
        private readonly Label _skillsStatusLabel;
        private readonly Button _installSkillsButton;

        internal SetupWizardSkillsStepPresenter(
            VisualElement skillsTargetRow,
            VisualElement skillsTargetList,
            VisualElement skillsStatusDivider,
            Label skillsStatusLabel,
            Button installSkillsButton,
            System.Action onInstallSkillsClicked)
        {
            Debug.Assert(skillsTargetRow != null, "skillsTargetRow must not be null");
            Debug.Assert(skillsTargetList != null, "skillsTargetList must not be null");
            Debug.Assert(skillsStatusDivider != null, "skillsStatusDivider must not be null");
            Debug.Assert(skillsStatusLabel != null, "skillsStatusLabel must not be null");
            Debug.Assert(installSkillsButton != null, "installSkillsButton must not be null");
            Debug.Assert(onInstallSkillsClicked != null, "onInstallSkillsClicked must not be null");

            _skillsTargetRow = skillsTargetRow
                ?? throw new System.ArgumentNullException(nameof(skillsTargetRow));
            _skillsTargetList = skillsTargetList
                ?? throw new System.ArgumentNullException(nameof(skillsTargetList));
            _skillsStatusDivider = skillsStatusDivider
                ?? throw new System.ArgumentNullException(nameof(skillsStatusDivider));
            _skillsStatusLabel = skillsStatusLabel
                ?? throw new System.ArgumentNullException(nameof(skillsStatusLabel));
            _installSkillsButton = installSkillsButton
                ?? throw new System.ArgumentNullException(nameof(installSkillsButton));
            _installSkillsButton.clicked += onInstallSkillsClicked
                ?? throw new System.ArgumentNullException(nameof(onInstallSkillsClicked));
        }

        internal void ShowChecking(bool shouldUseFirstInstallSkillsUi)
        {
            UpdateSkillsStatusLabel("Checking installed skills...");
            _installSkillsButton.SetEnabled(false);
            _installSkillsButton.text = "Checking...";
            ViewDataBinder.SetVisible(_skillsTargetRow, shouldUseFirstInstallSkillsUi);
            ViewDataBinder.SetVisible(_skillsTargetList, !shouldUseFirstInstallSkillsUi);
            _skillsTargetList.Clear();
        }

        internal void Update(
            bool canManageSkills,
            List<SkillSetupTargetInfo> targets,
            bool shouldUseFirstInstallSkillsUi,
            SkillsTarget selectedTarget,
            bool groupSkillsUnderUnityCliLoop,
            bool isInstallingSkills)
        {
            _skillsTargetList.Clear();
            ViewDataBinder.SetVisible(
                _skillsTargetRow,
                ShouldShowSkillsTargetRowForSetupWizard(shouldUseFirstInstallSkillsUi));
            ViewDataBinder.SetVisible(
                _skillsTargetList,
                ShouldShowSkillsTargetListForSetupWizard(canManageSkills, shouldUseFirstInstallSkillsUi));

            if (!canManageSkills)
            {
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = GetSkillsButtonTextForSetupWizard(
                    cliInstalled: false,
                    isInstallingSkills,
                    hasOutdatedSkills: false);
                return;
            }

            if (shouldUseFirstInstallSkillsUi)
            {
                SkillSetupTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
                    targets,
                    selectedTarget,
                    groupSkillsUnderUnityCliLoop);
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.text = CliSetupSection.GetInstallSkillsButtonText(
                    isCliInstalled: true,
                    isInstallingSkills,
                    selectedTargetInfo.InstallState);
                _installSkillsButton.SetEnabled(CliSetupSection.IsInstallSkillsButtonEnabled(
                    isCliInstalled: true,
                    isInstallingSkills,
                    selectedTargetInfo.InstallState));
                return;
            }

            List<SkillSetupTargetInfo> installableTargets = FilterInstallableSkillTargets(targets);

            foreach (SkillSetupTargetInfo target in installableTargets)
            {
                VisualElement item = new();
                item.AddToClassList("setup-target-item");

                Label nameLabel = new($"{target.DisplayName} ({target.DirName}/)");
                nameLabel.AddToClassList("setup-target-item__label");
                item.Add(nameLabel);

                Label statusLabel = new(GetSkillInstallStatusText(
                    target.InstallState,
                    target.HasDifferentLayoutSkills,
                    groupSkillsUnderUnityCliLoop));
                statusLabel.AddToClassList("setup-target-item__status");
                statusLabel.AddToClassList(GetSkillInstallStatusClass(
                    target.InstallState,
                    target.HasDifferentLayoutSkills));
                item.Add(statusLabel);

                _skillsTargetList.Add(item);
            }

            if (installableTargets.Count == 0)
            {
                UpdateSkillsStatusLabel(
                    "Create a tool folder to enable skill installation (.claude/, .agents/, etc.)");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Install Skills";
                return;
            }

            bool isCheckingSkills = installableTargets.Any(
                t => t.InstallState == SkillInstallState.Checking);
            if (isCheckingSkills)
            {
                UpdateSkillsStatusLabel("Checking installed skills...");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Checking...";
                return;
            }

            bool allSkillsInstalled = installableTargets.All(
                t => t.InstallState == SkillInstallState.Installed);
            if (allSkillsInstalled)
            {
                UpdateSkillsStatusLabel($"Installed for {installableTargets.Count} targets");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Installed";
            }
            else
            {
                bool hasOutdatedSkills = installableTargets.Any(
                    t => t.InstallState == SkillInstallState.Outdated);
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.SetEnabled(!isInstallingSkills);
                _installSkillsButton.text = GetSkillsButtonTextForSetupWizard(
                    cliInstalled: true,
                    isInstallingSkills,
                    hasOutdatedSkills);
            }
        }

        internal static List<SkillSetupTargetInfo> FilterInstallableSkillTargets(
            IEnumerable<SkillSetupTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets
                .Where(target => target.HasSkillsDirectory)
                .ToList();
        }

        internal static bool ShouldShowSkillsTargetRowForSetupWizard(bool shouldUseFirstInstallSkillsUi)
        {
            return shouldUseFirstInstallSkillsUi;
        }

        internal static bool ShouldShowSkillsTargetListForSetupWizard(
            bool canManageSkills,
            bool shouldUseFirstInstallSkillsUi)
        {
            return canManageSkills && !shouldUseFirstInstallSkillsUi;
        }

        internal static SkillSetupTargetInfo CreateFirstInstallSkillTarget(
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            return new(
                selection.DisplayName,
                selection.DirectoryName,
                selection.InstallFlag,
                hasSkillsDirectory: false,
                hasExistingSkills: false,
                hasDifferentLayoutSkills: false,
                SkillInstallState.Missing);
        }

        internal static SkillSetupTargetInfo GetSelectedSkillTargetInfo(
            IEnumerable<SkillSetupTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(targets != null, "targets must not be null");

            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            SkillSetupTargetInfo selectedTargetInfo = targets
                .FirstOrDefault(info => info.DirName == selection.DirectoryName);
            return string.IsNullOrEmpty(selectedTargetInfo.DirName)
                ? CreateFirstInstallSkillTarget(target, groupSkillsUnderUnityCliLoop)
                : selectedTargetInfo;
        }

        internal static List<SkillSetupTargetInfo> GetFirstInstallableSkillTargets(
            IEnumerable<SkillSetupTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillSetupTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
                targets,
                target,
                groupSkillsUnderUnityCliLoop);
            return selectedTargetInfo.InstallState == SkillInstallState.Installed
                   || selectedTargetInfo.InstallState == SkillInstallState.Checking
                ? new List<SkillSetupTargetInfo>()
                : new List<SkillSetupTargetInfo> { selectedTargetInfo };
        }

        internal static string GetSkillsButtonTextForSetupWizard(
            bool cliInstalled,
            bool isInstallingSkills,
            bool hasOutdatedSkills)
        {
            return !cliInstalled
                ? "Install Skills"
                : GetInstallSkillsButtonText(isInstallingSkills, hasOutdatedSkills);
        }

        internal static string GetInstallSkillsButtonText(
            bool isInstallingSkills,
            bool hasOutdatedSkills)
        {
            if (isInstallingSkills)
            {
                return "Installing...";
            }

            return hasOutdatedSkills ? "Update Skills" : "Install Skills";
        }

        internal static string GetSkillInstallStatusText(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop)
        {
            if (installState == SkillInstallState.Checking)
            {
                return "Checking...";
            }

            if (installState == SkillInstallState.Installed)
            {
                return "Installed";
            }

            if (installState == SkillInstallState.Outdated)
            {
                return "Outdated";
            }

            if (!hasDifferentLayoutSkills)
            {
                return "Missing";
            }

            return groupSkillsUnderUnityCliLoop ? "Not grouped" : "Grouped";
        }

        internal static string GetSkillInstallStatusClass(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills)
        {
            if (installState == SkillInstallState.Checking)
            {
                return "setup-target-item__status--checking";
            }

            if (installState == SkillInstallState.Installed)
            {
                return "setup-target-item__status--installed";
            }

            if (installState == SkillInstallState.Outdated)
            {
                return "setup-target-item__status--outdated";
            }

            if (!hasDifferentLayoutSkills)
            {
                return "setup-target-item__status--missing";
            }

            return "setup-target-item__status--different-layout";
        }

        private void UpdateSkillsStatusLabel(string text)
        {
            _skillsStatusLabel.text = text;
            bool isVisible = !string.IsNullOrEmpty(text);
            ViewDataBinder.SetVisible(_skillsStatusDivider, isVisible);
            ViewDataBinder.SetVisible(_skillsStatusLabel, isVisible);
        }
    }
}
