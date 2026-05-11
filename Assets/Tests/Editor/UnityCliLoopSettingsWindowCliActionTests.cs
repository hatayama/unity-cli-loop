using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Settings Window CLI Action behavior.
    /// </summary>
    public class UnityCliLoopSettingsWindowCliActionTests
    {
        [TestCase(null, "3.0.0", true, false)]
        [TestCase("2.9.0", "3.0.0", true, false)]
        [TestCase("3.1.0", "3.0.0", true, true)]
        [TestCase("3.0.0", "3.0.0", true, true)]
        [TestCase("3.0.0", "3.0.0", false, false)]
        public void ShouldUninstallCliFromPrimaryButton_ReturnsExpectedAction(
            string cliVersion,
            string requiredCliVersion,
            bool canUninstallCli,
            bool expected)
        {
            // Verifies that package-owned installs route to uninstall when the CLI satisfies the package minimum.
            bool result = UnityCliLoopSettingsWindow.ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                requiredCliVersion,
                canUninstallCli);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("3.0.0-beta.0", "3.0.0-beta.1", true)]
        [TestCase("3.0.0-beta.1", "3.0.0-beta.1", false)]
        [TestCase("3.0.0", "3.0.0-beta.1", false)]
        public void IsCliUpdateNeeded_UsesMinimumRequiredCliVersion(
            string cliVersion,
            string requiredCliVersion,
            bool expected)
        {
            // Verifies that the settings UI ignores package version drift and only updates old CLIs.
            bool result = UnityCliLoopSettingsWindow.IsCliUpdateNeeded(cliVersion, requiredCliVersion);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
