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
        [TestCase(false, false, false, false, false, false, null, "3.0.0", "Install CLI")]
        [TestCase(false, false, false, false, false, true, null, "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, false, true, false, "3.0.0", "3.0.0", "Uninstall CLI")]
        [TestCase(true, false, false, false, false, false, "3.0.0", "3.0.0", "Install CLI")]
        [TestCase(true, false, false, false, true, true, "3.0.0", "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, true, true, false, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, true, true, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, true, false, "3.0.0", "3.0.0", "Update CLI (v3.0.0 required)")]
        [TestCase(true, true, false, false, true, false, "3.0.0", "3.0.0", "Uninstalling...")]
        [TestCase(true, true, false, false, true, true, "3.0.0", "3.0.0", "Fixing PATH...")]
        [TestCase(false, true, false, false, false, false, null, "3.0.0", "Installing...")]
        [TestCase(false, false, true, false, false, false, null, "3.0.0", "Checking...")]
        public void GetInstallCliButtonText_ReturnsExpectedText(
            bool isCliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool canUninstallCli,
            bool needsCliPathSetup,
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
                cliVersion,
                requiredCliVersion);

            Assert.That(text, Is.EqualTo(expectedText));
        }

        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void IsInstallCliButtonEnabled_ReturnsExpectedValue(
            bool isInstallingCli,
            bool isChecking,
            bool expectedEnabled)
        {
            bool enabled = CliSetupSection.IsInstallCliButtonEnabled(
                isInstallingCli,
                isChecking);

            Assert.That(enabled, Is.EqualTo(expectedEnabled));
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
        public void Update_WhenCliRefreshIsChecking_ShowsSkillsCheckingState()
        {
            // Verifies that IsChecking routes the shared skills panel into ShowChecking.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: true,
                selectedTargetInstallState: SkillInstallState.Missing);

            section.Update(data);

            Button refreshSkillsButton = root.Q<Button>("refresh-skills-state-button");
            VisualElement skillsSubsection = root.Q<VisualElement>("skills-subsection");
            Button installAllSkillsButton = root.Q<Button>("install-all-skills-button");
            Button installSelectedSkillsButton = root.Q<Button>("install-selected-skills-button");
            Assert.That(refreshSkillsButton.enabledSelf, Is.False);
            Assert.That(skillsSubsection.enabledSelf, Is.True);
            Assert.That(installAllSkillsButton.enabledSelf, Is.False);
            Assert.That(installAllSkillsButton.text, Is.EqualTo("Checking..."));
            Assert.That(installSelectedSkillsButton.enabledSelf, Is.False);
            Assert.That(installSelectedSkillsButton.text, Is.EqualTo("Checking..."));
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

        private static VisualElement CreateRootElement()
        {
            VisualElement root = new();
            root.Add(new VisualElement { name = "cli-status-icon" });
            root.Add(new Label { name = "cli-status-label" });
            root.Add(new Button { name = "refresh-cli-version-button" });
            root.Add(new Button { name = "install-cli-button" });
            VisualElement installProgress = new() { name = "cli-install-progress" };
            installProgress.Add(new Label { name = "cli-install-progress-label" });
            root.Add(installProgress);

            VisualElement skillsSubsection = new() { name = "skills-subsection" };
            VisualElement skillsSetupPanel = new() { name = "skills-setup-panel" };
            skillsSetupPanel.Add(new VisualElement { name = "skill-target-status-list" });
            skillsSetupPanel.Add(new VisualElement { name = "skill-target-status-divider" });
            skillsSetupPanel.Add(new Label { name = "skill-target-status-summary" });
            skillsSetupPanel.Add(new Button { name = "install-all-skills-button" });
            Foldout specificTargetFoldout = new() { name = "install-specific-target-foldout" };
            VisualElement groupSkillsRow = new() { name = "group-skills-row" };
            groupSkillsRow.Add(new Toggle { name = "group-skills-toggle" });
            groupSkillsRow.Add(new Label { name = "group-skills-label" });
            specificTargetFoldout.Add(groupSkillsRow);
            VisualElement skillsTargetRow = new() { name = "skills-target-row" };
            skillsTargetRow.Add(new EnumField { name = "skills-target-field" });
            skillsTargetRow.Add(new Button { name = "refresh-skills-state-button" });
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
            SkillInstallState selectedTargetInstallState)
        {
            return new CliSetupData(
                isCliInstalled,
                cliVersion: "3.0.0",
                requiredCliVersion: "3.0.0",
                needsUpdate: false,
                canUninstallCli: true,
                needsCliPathSetup: false,
                isInstallingCli: false,
                isChecking,
                isClaudeSkillsInstalled: false,
                isAgentsSkillsInstalled: false,
                isCursorSkillsInstalled: false,
                isGeminiSkillsInstalled: false,
                isCodexSkillsInstalled: false,
                isAntigravitySkillsInstalled: false,
                selectedTargetInstallState,
                SkillsTarget.Claude,
                groupSkillsUnderUnityCliLoop: false,
                isInstallingSkills: false,
                installableSkillTargets: new List<SkillSetupTargetInfo>());
        }
    }
}
