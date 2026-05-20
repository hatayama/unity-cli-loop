using NUnit.Framework;
using UnityEngine;

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

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void ShouldRepairCliPathFromPrimaryButton_ReturnsExpectedAction(
            bool needsCliPathSetup,
            bool expected)
        {
            // Verifies that stale terminal PATH state routes the primary button to repair before uninstall.
            bool result = UnityCliLoopSettingsWindow.ShouldRepairCliPathFromPrimaryButton(needsCliPathSetup);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(RuntimePlatform.OSXEditor, true, true)]
        [TestCase(RuntimePlatform.OSXEditor, false, false)]
        [TestCase(RuntimePlatform.LinuxEditor, true, true)]
        [TestCase(RuntimePlatform.WindowsEditor, true, false)]
        public void ShouldCheckCliPathSetupForPlatform_RequiresPackageOwnedCli(
            RuntimePlatform platform,
            bool hasPackageOwnedCurrentUserInstall,
            bool expected)
        {
            // Verifies that PATH repair only runs for POSIX package-owned current-user installs.
            bool result = UnityCliLoopSettingsWindow.ShouldCheckCliPathSetupForPlatform(
                platform,
                hasPackageOwnedCurrentUserInstall);

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
