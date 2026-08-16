using System.Collections.Generic;

using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI Setup Section behavior.
    /// </summary>
    public class CliSetupSectionTests
    {
        [TestCase(false, false, false, false, false, false, false, null, "3.0.0", "Install CLI")]
        [TestCase(false, false, false, false, false, true, false, null, "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, false, true, false, false, "3.0.0", "3.0.0", "Uninstall CLI")]
        [TestCase(true, false, false, false, false, false, false, "3.0.0", "3.0.0", "Install CLI")]
        [TestCase(true, false, false, false, true, true, false, "3.0.0", "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, true, true, false, false, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, true, true, false, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, true, false, false, "3.0.0", "3.0.0", "Update CLI (v3.0.0 required)")]
        [TestCase(true, true, false, false, true, false, false, "3.0.0", "3.0.0", "Uninstalling...")]
        [TestCase(true, true, false, false, true, true, false, "3.0.0", "3.0.0", "Fixing PATH...")]
        [TestCase(false, true, false, false, false, false, false, null, "3.0.0", "Installing...")]
        [TestCase(false, false, true, false, false, false, false, null, "3.0.0", "Checking...")]
        [TestCase(true, false, false, false, false, false, true, "3.0.0", "3.0.0", "Managed by Homebrew")]
        [TestCase(true, false, false, true, false, false, true, "2.9.0", "3.0.0", "Managed by Homebrew")]
        [TestCase(true, false, true, false, false, false, true, "3.0.0", "3.0.0", "Checking...")]
        [TestCase(false, false, false, false, false, false, true, null, "3.0.0", "Install CLI")]
        public void GetInstallCliButtonText_ReturnsExpectedText(
            bool isCliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool canUninstallCli,
            bool needsCliPathSetup,
            bool isHomebrewManagedCli,
            string cliVersion,
            string requiredCliVersion,
            string expectedText)
        {
            string text = CliSetupSection.GetInstallCliButtonText(
                isCliInstalled,
                isInstallingCli,
                isChecking,
                needsUpdate,
                canUninstallCli,
                needsCliPathSetup,
                isHomebrewManagedCli,
                cliVersion,
                requiredCliVersion);

            Assert.That(text, Is.EqualTo(expectedText));
        }

        [TestCase(false, false, false, "3.0.0", true)]
        [TestCase(true, false, false, "3.0.0", false)]
        [TestCase(false, true, false, "3.0.0", false)]
        [TestCase(false, false, true, "3.0.0", false)]
        [TestCase(false, false, true, null, true)]
        [TestCase(false, false, true, "", true)]
        public void IsInstallCliButtonEnabled_ReturnsExpectedValue(
            bool isInstallingCli,
            bool isChecking,
            bool isHomebrewManagedCli,
            string cliVersion,
            bool expectedEnabled)
        {
            // Verifies a Homebrew path whose binary reports no version keeps the install action reachable.
            bool enabled = CliSetupSection.IsInstallCliButtonEnabled(
                isInstallingCli,
                isChecking,
                isHomebrewManagedCli,
                cliVersion);

            Assert.That(enabled, Is.EqualTo(expectedEnabled));
        }

        [TestCase(true, false, null, "CLI: Checking...")]
        [TestCase(false, false, null, "CLI: Not installed")]
        [TestCase(false, true, "3.0.0", "CLI: v3.0.0")]
        public void GetCliStatusText_ReturnsExpectedText(
            bool isChecking,
            bool isCliInstalled,
            string cliVersion,
            string expectedText)
        {
            // Verifies the status line stays a short single-line summary of the detected CLI.
            string text = CliSetupSection.GetCliStatusText(
                isChecking,
                isCliInstalled,
                cliVersion);

            Assert.That(text, Is.EqualTo(expectedText));
        }

        [Test]
        public void Update_WhenCliIsHomebrewManaged_DisablesPrimaryButton()
        {
            // Verifies the Settings primary button cannot trigger an install for a Homebrew-managed CLI.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                isHomebrewManagedCli: true);

            section.Update(data);

            Button installCliButton = root.Q<Button>("install-cli-button");
            Assert.That(installCliButton.text, Is.EqualTo("Managed by Homebrew"));
            Assert.That(installCliButton.enabledSelf, Is.False);
        }

        [Test]
        public void Update_WhenHomebrewPathReportsNoVersion_KeepsPathRepairReachable()
        {
            // Verifies a Homebrew path whose binary answers no version probe still offers PATH repair.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                isHomebrewManagedCli: true,
                cliVersion: null,
                needsCliPathSetup: true);

            section.Update(data);

            Button installCliButton = root.Q<Button>("install-cli-button");
            Assert.That(installCliButton.text, Is.EqualTo("Fix PATH"));
            Assert.That(installCliButton.enabledSelf, Is.True);
        }

        [Test]
        public void Update_WhenHomebrewManagedCliNeedsUpdate_ShowsUpgradeWarning()
        {
            // Verifies the brew upgrade command is shown in a dedicated warning block, not in the status line.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                isHomebrewManagedCli: true,
                needsUpdate: true,
                cliVersion: "2.9.0");

            section.Update(data);

            Label statusLabel = root.Q<Label>("cli-status-label");
            Label warningLabel = root.Q<Label>("cli-homebrew-upgrade-message");
            Assert.That(statusLabel.text, Is.EqualTo("CLI: v2.9.0"));
            Assert.That(
                warningLabel.text,
                Is.EqualTo("Homebrew-managed CLI v2.9.0 does not meet the required v3.0.0.\n"
                    + "Run this command in your terminal:\nbrew upgrade uloop"));
            Assert.That(warningLabel.ClassListContains("unity-cli-loop-warning-message--visible"), Is.True);
        }

        [Test]
        public void Update_WhenHomebrewManagedCliIsUpToDate_HidesUpgradeWarning()
        {
            // Verifies the upgrade warning stays hidden while the Homebrew CLI satisfies the required version.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                isHomebrewManagedCli: true);

            section.Update(data);

            Label warningLabel = root.Q<Label>("cli-homebrew-upgrade-message");
            Assert.That(warningLabel.text, Is.Empty);
            Assert.That(warningLabel.ClassListContains("unity-cli-loop-warning-message--visible"), Is.False);
        }

        [Test]
        public void Update_WhenCliIsNotHomebrewManaged_HidesUpgradeWarning()
        {
            // Verifies a package-installed CLI that needs an update keeps using the primary button, not the warning.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                needsUpdate: true,
                cliVersion: "2.9.0");

            section.Update(data);

            Label warningLabel = root.Q<Label>("cli-homebrew-upgrade-message");
            Assert.That(warningLabel.ClassListContains("unity-cli-loop-warning-message--visible"), Is.False);
        }

        [TestCase(true, false, true, true)]
        [TestCase(true, false, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(true, true, true, false)]
        public void IsUninstallCliAction_ReturnsExpectedValue(
            bool isCliInstalled,
            bool needsUpdate,
            bool canUninstallCli,
            bool expected)
        {
            bool result = CliSetupSection.IsUninstallCliAction(
                isCliInstalled,
                needsUpdate,
                canUninstallCli);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void Update_WhenSkillStateIsChecking_ShowsSkillsCheckingState()
        {
            // Verifies that IsSkillStateChecking routes the shared skills panel into ShowChecking.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                isSkillStateChecking: true,
                selectedTargetInstallState: SkillInstallState.Missing);

            section.Update(data);

            Button refreshSkillsButton = root.Q<Button>("refresh-skills-state-button");
            VisualElement skillsSubsection = root.Q<VisualElement>("skills-subsection");
            Button installAllSkillsButton = root.Q<Button>("install-all-skills-button");
            Button installSelectedSkillsButton = root.Q<Button>("install-selected-skills-button");
            EnumField skillsTargetField = root.Q<EnumField>("skills-target-field");
            Toggle groupSkillsToggle = root.Q<Toggle>("group-skills-toggle");
            Assert.That(refreshSkillsButton.enabledSelf, Is.False);
            Assert.That(skillsSubsection.enabledSelf, Is.True);
            Assert.That(installAllSkillsButton.enabledSelf, Is.False);
            Assert.That(installAllSkillsButton.text, Is.EqualTo("Checking..."));
            Assert.That(installSelectedSkillsButton.enabledSelf, Is.False);
            Assert.That(installSelectedSkillsButton.text, Is.EqualTo("Checking..."));
            Assert.That(skillsTargetField.enabledSelf, Is.False);
            Assert.That(groupSkillsToggle.enabledSelf, Is.False);
        }

        [Test]
        public void Update_WhenOnlyCliIsChecking_DoesNotShowSkillsCheckingState()
        {
            // Verifies CLI refresh checking does not force the skills panel into Checking... state.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: true,
                isSkillStateChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing);

            section.Update(data);

            Button installAllSkillsButton = root.Q<Button>("install-all-skills-button");
            EnumField skillsTargetField = root.Q<EnumField>("skills-target-field");
            Button refreshCliVersionButton = root.Q<Button>("refresh-cli-version-button");
            Assert.That(installAllSkillsButton.text, Is.Not.EqualTo("Checking..."));
            Assert.That(skillsTargetField.enabledSelf, Is.True);
            Assert.That(refreshCliVersionButton.enabledSelf, Is.False);
        }

        [Test]
        public void Update_WhenSkillsStateIsChecking_DisablesSkillsTargetField()
        {
            // Verifies that the Skills target cannot change while the selected target state is being checked.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Checking);

            section.Update(data);

            EnumField skillsTargetField = root.Q<EnumField>("skills-target-field");
            Assert.That(skillsTargetField.enabledSelf, Is.False);
        }

        [Test]
        public void Update_WhenNoInstallableTargets_HidesBulkInstallAndShowsGuidance()
        {
            // Verifies empty detection hides the bulk Install Skills button and shows guidance instead.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing,
                installableSkillTargets: new List<SkillSetupTargetInfo>());

            section.Update(data);

            Button installAllSkillsButton = root.Q<Button>("install-all-skills-button");
            Label noTargetsMessage = root.Q<Label>("skills-no-targets-message");
            Assert.That(installAllSkillsButton.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(noTargetsMessage.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Update_WhenInstallableTargetsExist_ShowsBulkInstallAndHidesGuidance()
        {
            // Verifies detected targets keep the bulk Install Skills button and hide empty-state guidance.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget("Claude", ".claude", SkillInstallState.Missing)
            };
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing,
                installableSkillTargets: targets);

            section.Update(data);

            Button installAllSkillsButton = root.Q<Button>("install-all-skills-button");
            Label noTargetsMessage = root.Q<Label>("skills-no-targets-message");
            Assert.That(installAllSkillsButton.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(noTargetsMessage.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void Update_WhenCheckingTargetsArrive_DoesNotChangeSpecificTargetFoldout()
        {
            // Verifies Checking updates leave the foldout value untouched until a resolved state arrives.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            Foldout foldout = root.Q<Foldout>("install-specific-target-foldout");
            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Missing)
                }));
            Assert.That(foldout.value, Is.True);
            foldout.SetValueWithoutNotify(false);

            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Checking,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Checking),
                    CreateSkillTarget("Common", ".agents", SkillInstallState.Missing)
                }));

            Assert.That(foldout.value, Is.False);
        }

        [Test]
        public void Update_WhenMissingTargetsBecomeInstalled_ClosesSpecificTargetFoldout()
        {
            // Verifies the foldout closes when reload resolves missing targets to installed.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            Foldout foldout = root.Q<Foldout>("install-specific-target-foldout");
            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Missing)
                }));
            Assert.That(foldout.value, Is.True);

            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Installed)
                }));

            Assert.That(foldout.value, Is.False);
        }

        [Test]
        public void Update_WhenInstalledDefaultUnchanged_PreservesUserOpenedFoldout()
        {
            // Verifies a user-opened foldout stays open across later installed-only updates.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            Foldout foldout = root.Q<Foldout>("install-specific-target-foldout");
            List<SkillSetupTargetInfo> installedTargets = new()
            {
                CreateSkillTarget("Claude", ".claude", SkillInstallState.Installed)
            };
            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                installableSkillTargets: installedTargets));
            Assert.That(foldout.value, Is.False);
            foldout.SetValueWithoutNotify(true);

            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Installed)
                }));

            Assert.That(foldout.value, Is.True);
        }

        [Test]
        public void Update_WhenMissingDefaultUnchanged_PreservesUserClosedFoldout()
        {
            // Verifies a user-closed foldout is not forced open while targets remain missing.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            Foldout foldout = root.Q<Foldout>("install-specific-target-foldout");
            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Missing)
                }));
            Assert.That(foldout.value, Is.True);
            foldout.SetValueWithoutNotify(false);

            section.Update(CreateData(
                isCliInstalled: true,
                isChecking: false,
                selectedTargetInstallState: SkillInstallState.Missing,
                installableSkillTargets: new List<SkillSetupTargetInfo>
                {
                    CreateSkillTarget("Claude", ".claude", SkillInstallState.Missing)
                }));

            Assert.That(foldout.value, Is.False);
        }

        private static SkillSetupTargetInfo CreateSkillTarget(
            string displayName,
            string dirName,
            SkillInstallState installState)
        {
            return new(
                displayName,
                dirName,
                "--flag",
                hasSkillsDirectory: true,
                hasExistingSkills: true,
                hasDifferentLayoutSkills: false,
                installState);
        }

        private static VisualElement CreateRootElement()
        {
            VisualElement root = new();
            root.Add(new VisualElement { name = "cli-status-icon" });
            root.Add(new Label { name = "cli-status-label" });
            root.Add(new Button { name = "refresh-cli-version-button" });
            root.Add(new Label { name = "cli-homebrew-upgrade-message" });
            root.Add(new Button { name = "install-cli-button" });
            VisualElement installProgress = new() { name = "cli-install-progress" };
            installProgress.Add(new Label { name = "cli-install-progress-label" });
            root.Add(installProgress);

            root.Add(new Button { name = "refresh-skills-state-button" });
            VisualElement skillsSubsection = new() { name = "skills-subsection" };
            VisualElement skillsSetupPanel = new() { name = "skills-setup-panel" };
            skillsSetupPanel.Add(new VisualElement { name = "skill-target-status-list" });
            skillsSetupPanel.Add(new VisualElement { name = "skill-target-status-divider" });
            skillsSetupPanel.Add(new Label { name = "skill-target-status-summary" });
            skillsSetupPanel.Add(new Label { name = "skills-no-targets-message" });
            skillsSetupPanel.Add(new Button { name = "install-all-skills-button" });
            Foldout specificTargetFoldout = new() { name = "install-specific-target-foldout" };
            VisualElement groupSkillsRow = new() { name = "group-skills-row" };
            groupSkillsRow.Add(new Toggle { name = "group-skills-toggle" });
            groupSkillsRow.Add(new Label { name = "group-skills-label" });
            specificTargetFoldout.Add(groupSkillsRow);
            VisualElement skillsTargetRow = new() { name = "skills-target-row" };
            skillsTargetRow.Add(new EnumField { name = "skills-target-field" });
            specificTargetFoldout.Add(skillsTargetRow);
            specificTargetFoldout.Add(new Button { name = "install-selected-skills-button" });
            skillsSetupPanel.Add(specificTargetFoldout);
            skillsSubsection.Add(skillsSetupPanel);
            root.Add(skillsSubsection);
            return root;
        }

        private static CliSetupData CreateData(
            bool isCliInstalled,
            bool isChecking,
            SkillInstallState selectedTargetInstallState,
            IReadOnlyList<SkillSetupTargetInfo> installableSkillTargets = null,
            bool? isSkillStateChecking = null,
            bool isHomebrewManagedCli = false,
            bool needsUpdate = false,
            string cliVersion = "3.0.0",
            bool needsCliPathSetup = false)
        {
            return new CliSetupData(
                isCliInstalled,
                cliVersion,
                requiredCliVersion: "3.0.0",
                needsUpdate,
                canUninstallCli: true,
                needsCliPathSetup,
                isHomebrewManagedCli,
                isInstallingCli: false,
                isChecking,
                isSkillStateChecking: isSkillStateChecking ?? isChecking,
                isClaudeSkillsInstalled: false,
                isAgentsSkillsInstalled: false,
                isCodexSkillsInstalled: false,
                isAntigravitySkillsInstalled: false,
                selectedTargetInstallState,
                SkillsTarget.Claude,
                groupSkillsUnderUnityCliLoop: false,
                isInstallingSkills: false,
                installableSkillTargets: installableSkillTargets ?? new List<SkillSetupTargetInfo>());
        }
    }
}
