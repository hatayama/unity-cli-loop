using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class CliSetupSectionTests
    {
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

        [TestCase(false, false, false, SkillInstallState.Missing, false)]
        [TestCase(true, true, false, SkillInstallState.Missing, false)]
        [TestCase(true, false, true, SkillInstallState.Missing, false)]
        [TestCase(true, false, false, SkillInstallState.Checking, false)]
        [TestCase(true, false, false, SkillInstallState.Installed, false)]
        [TestCase(true, false, false, SkillInstallState.Outdated, true)]
        [TestCase(true, false, false, SkillInstallState.Missing, true)]
        public void IsInstallSkillsButtonEnabled_ReturnsExpectedValue(
            bool isCliInstalled,
            bool isInstallingSkills,
            bool isChecking,
            SkillInstallState installState,
            bool expectedEnabled)
        {
            bool enabled = CliSetupSection.IsInstallSkillsButtonEnabled(
                isCliInstalled,
                isInstallingSkills,
                isChecking,
                installState);

            Assert.That(enabled, Is.EqualTo(expectedEnabled));
        }
    }
}
