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
        [TestCase(true, false, false, false, false, true, "3.0.0", "3.0.0", "Uninstall CLI")]
        [TestCase(true, false, false, false, false, false, "3.0.0", "3.0.0", "Install CLI")]
        [TestCase(true, false, false, true, false, true, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, false, true, true, "3.1.0", "3.0.0", "Downgrade CLI (v3.1.0 \u2192 v3.0.0)")]
        [TestCase(true, true, false, false, false, true, "3.0.0", "3.0.0", "Uninstalling...")]
        [TestCase(false, true, false, false, false, false, null, "3.0.0", "Installing...")]
        [TestCase(false, false, true, false, false, false, null, "3.0.0", "Checking...")]
        public void GetInstallCliButtonText_ReturnsExpectedText(
            bool isCliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool needsDowngrade,
            bool canUninstallCli,
            string cliVersion,
            string requiredDispatcherVersion,
            string expectedText)
        {
            string text = CliSetupSection.GetInstallCliButtonText(
                isCliInstalled,
                isInstallingCli,
                isChecking,
                needsUpdate,
                needsDowngrade,
                canUninstallCli,
                cliVersion,
                requiredDispatcherVersion);

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

        [TestCase(true, false, false, true, true)]
        [TestCase(true, false, false, false, false)]
        [TestCase(false, false, false, true, false)]
        [TestCase(true, true, false, true, false)]
        [TestCase(true, false, true, true, false)]
        public void IsUninstallCliAction_ReturnsExpectedValue(
            bool isCliInstalled,
            bool needsUpdate,
            bool needsDowngrade,
            bool canUninstallCli,
            bool expected)
        {
            bool result = CliSetupSection.IsUninstallCliAction(
                isCliInstalled,
                needsUpdate,
                needsDowngrade,
                canUninstallCli);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(false, false, SkillInstallState.Missing, "Install Skills")]
        [TestCase(true, true, SkillInstallState.Missing, "Installing...")]
        [TestCase(true, false, SkillInstallState.Checking, "Checking...")]
        [TestCase(true, false, SkillInstallState.Outdated, "Update Skills")]
        [TestCase(true, false, SkillInstallState.Missing, "Install Skills")]
        [TestCase(true, false, SkillInstallState.Installed, "Installed")]
        public void GetInstallSkillsButtonText_ReturnsExpectedText(
            bool isCliInstalled,
            bool isInstallingSkills,
            SkillInstallState installState,
            string expectedText)
        {
            string text = CliSetupSection.GetInstallSkillsButtonText(
                isCliInstalled,
                isInstallingSkills,
                installState);

            Assert.That(text, Is.EqualTo(expectedText));
        }

        [TestCase(false, false, SkillInstallState.Missing, false)]
        [TestCase(true, true, SkillInstallState.Missing, false)]
        [TestCase(true, false, SkillInstallState.Checking, false)]
        [TestCase(true, false, SkillInstallState.Installed, false)]
        [TestCase(true, false, SkillInstallState.Outdated, true)]
        [TestCase(true, false, SkillInstallState.Missing, true)]
        public void IsInstallSkillsButtonEnabled_ReturnsExpectedValue(
            bool isCliInstalled,
            bool isInstallingSkills,
            SkillInstallState installState,
            bool expectedEnabled)
        {
            // Verifies that the Skills install button ignores CLI-only refresh work.
            bool enabled = CliSetupSection.IsInstallSkillsButtonEnabled(
                isCliInstalled,
                isInstallingSkills,
                installState);

            Assert.That(enabled, Is.EqualTo(expectedEnabled));
        }

        [Test]
        public void Update_WhenCliRefreshIsChecking_KeepsSkillsReloadButtonEnabled()
        {
            // Verifies that a CLI-only refresh does not block manual Skills reload.
            VisualElement root = CreateRootElement();
            CliSetupSection section = new(root);
            CliSetupData data = CreateData(
                isCliInstalled: true,
                isChecking: true,
                selectedTargetInstallState: SkillInstallState.Checking);

            section.Update(data);

            Button refreshSkillsButton = root.Q<Button>("refresh-skills-state-button");
            VisualElement skillsSubsection = root.Q<VisualElement>("skills-subsection");
            Assert.That(refreshSkillsButton.enabledSelf, Is.True);
            Assert.That(skillsSubsection.enabledSelf, Is.True);
        }

        private static VisualElement CreateRootElement()
        {
            VisualElement root = new();
            root.Add(new VisualElement { name = "cli-status-icon" });
            root.Add(new Label { name = "cli-status-label" });
            root.Add(new Button { name = "refresh-cli-version-button" });
            root.Add(new Button { name = "install-cli-button" });
            root.Add(new EnumField { name = "skills-target-field" });
            root.Add(new Button { name = "refresh-skills-state-button" });
            root.Add(new VisualElement { name = "group-skills-row" });
            root.Add(new Toggle { name = "group-skills-toggle" });
            root.Add(new Label { name = "group-skills-label" });
            root.Add(new Button { name = "install-skills-button" });
            root.Add(new VisualElement { name = "skills-subsection" });
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
                requiredDispatcherVersion: "3.0.0",
                needsUpdate: false,
                needsDowngrade: false,
                canUninstallCli: true,
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
                isInstallingSkills: false);
        }
    }
}
