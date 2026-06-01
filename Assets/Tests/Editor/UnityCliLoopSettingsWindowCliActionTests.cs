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

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void ShouldRepairCliPathFromPrimaryButton_ReturnsExpectedAction(
            bool needsCliPathSetup,
            bool needsUpdate,
            bool expected)
        {
            // Verifies that stale terminal PATH state routes to repair only when install/update is not needed.
            bool result = UnityCliLoopSettingsWindow.ShouldRepairCliPathFromPrimaryButton(
                needsCliPathSetup,
                needsUpdate);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(true, "3.0.0", "3.0.0", true, "RepairPath")]
        [TestCase(true, "2.9.0", "3.0.0", true, "InstallOrUpdate")]
        [TestCase(false, "3.0.0", "3.0.0", true, "Uninstall")]
        [TestCase(false, "2.9.0", "3.0.0", true, "InstallOrUpdate")]
        [TestCase(false, null, "3.0.0", true, "InstallOrUpdate")]
        public void ResolveCliPrimaryButtonAction_ReturnsClickedPrimaryAction(
            bool needsCliPathSetup,
            string cliVersion,
            string requiredCliVersion,
            bool canUninstallCli,
            string expected)
        {
            // Verifies that the Settings window preserves the primary action chosen before refresh.
            UnityCliLoopSettingsWindow.CliPrimaryButtonAction result =
                UnityCliLoopSettingsWindow.ResolveCliPrimaryButtonAction(
                    needsCliPathSetup,
                    cliVersion,
                    requiredCliVersion,
                    canUninstallCli);

            Assert.That(result.ToString(), Is.EqualTo(expected));
        }

        [TestCase("InstallOrUpdate", "InstallOrUpdate", "InstallOrUpdate")]
        [TestCase("RepairPath", "RepairPath", "RepairPath")]
        [TestCase("Uninstall", "Uninstall", "Uninstall")]
        [TestCase("InstallOrUpdate", "RepairPath", "RepairPath")]
        [TestCase("InstallOrUpdate", "Uninstall", "None")]
        [TestCase("Uninstall", "InstallOrUpdate", "None")]
        [TestCase("RepairPath", "Uninstall", "None")]
        public void ResolveExecutableCliPrimaryButtonAction_IgnoresUnsafeStaleActions(
            string clickedAction,
            string refreshedAction,
            string expected)
        {
            // Verifies that a refreshed Settings state cannot turn a stale click into a destructive action.
            UnityCliLoopSettingsWindow.CliPrimaryButtonAction result =
                UnityCliLoopSettingsWindow.ResolveExecutableCliPrimaryButtonAction(
                    ParseAction(clickedAction),
                    ParseAction(refreshedAction));

            Assert.That(result.ToString(), Is.EqualTo(expected));
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

        private static UnityCliLoopSettingsWindow.CliPrimaryButtonAction ParseAction(string action)
        {
            return (UnityCliLoopSettingsWindow.CliPrimaryButtonAction)
                System.Enum.Parse(typeof(UnityCliLoopSettingsWindow.CliPrimaryButtonAction), action);
        }
    }
}
