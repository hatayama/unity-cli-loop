using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Settings Window CLI Action behavior.
    /// </summary>
    public class UnityCliLoopSettingsWindowCliActionTests
    {
        [TestCase(null, false, true, false)]
        [TestCase("2.9.0", false, true, false)]
        [TestCase("3.0.0", false, true, false)]
        [TestCase("2.9.0", true, true, false)]
        [TestCase("3.0.0", true, true, true)]
        [TestCase("3.0.1", true, true, true)]
        [TestCase("3.0.0", true, false, false)]
        public void ShouldUninstallCliFromPrimaryButton_ReturnsExpectedAction(
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli,
            bool expected)
        {
            // Verifies that package-owned installs route to uninstall only when the dispatcher minimum is satisfied.
            bool result = UnityCliLoopSettingsWindow.ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                cliIsDispatcher,
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
            // Verifies that stale terminal PATH state routes to repair only when CLI replacement is not needed.
            bool result = UnityCliLoopSettingsWindow.ShouldRepairCliPathFromPrimaryButton(
                needsCliPathSetup,
                needsUpdate);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(true, "3.0.0", true, true, "RepairPath")]
        [TestCase(true, "3.0.1", true, true, "RepairPath")]
        [TestCase(true, "3.0.0", false, true, "InstallOrUpdate")]
        [TestCase(true, "2.9.0", true, true, "InstallOrUpdate")]
        [TestCase(false, "3.0.0", true, true, "Uninstall")]
        [TestCase(false, "3.0.1", true, true, "Uninstall")]
        [TestCase(false, "3.0.0", false, true, "InstallOrUpdate")]
        [TestCase(false, "3.0.0", true, false, "InstallOrUpdate")]
        [TestCase(false, null, false, true, "InstallOrUpdate")]
        public void ResolveCliPrimaryButtonAction_ReturnsClickedPrimaryAction(
            bool needsCliPathSetup,
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli,
            string expected)
        {
            // Verifies that the Settings window chooses repair only when dispatcher replacement is unnecessary.
            UnityCliLoopSettingsWindow.CliPrimaryButtonAction result =
                UnityCliLoopSettingsWindow.ResolveCliPrimaryButtonAction(
                    needsCliPathSetup,
                    cliVersion,
                    cliIsDispatcher,
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

        [TestCase(null, false, false)]
        [TestCase("2.1.10", false, true)]
        [TestCase("3.0.0", false, true)]
        [TestCase("2.1.10", true, true)]
        [TestCase("3.0.0", true, false)]
        [TestCase("3.0.1", true, false)]
        [TestCase("not-a-version", true, true)]
        public void IsCliUpdateNeeded_UsesDispatcherMinimumVersion(
            string cliVersion,
            bool cliIsDispatcher,
            bool expected)
        {
            // Verifies that the settings UI updates non-dispatcher or older dispatcher installs.
            bool result = UnityCliLoopSettingsWindow.IsCliUpdateNeeded(cliVersion, cliIsDispatcher);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(SkillInstallState.Missing, true)]
        [TestCase(SkillInstallState.Outdated, false)]
        public void ShouldShowSkillsInstalledDialog_ReturnsExpectedValue(
            SkillInstallState installState,
            bool expected)
        {
            // Verifies that Settings keeps the success dialog for first install only.
            bool result = UnityCliLoopSettingsWindow.ShouldShowSkillsInstalledDialog(installState);

            Assert.That(result, Is.EqualTo(expected));
        }

        private static UnityCliLoopSettingsWindow.CliPrimaryButtonAction ParseAction(string action)
        {
            return (UnityCliLoopSettingsWindow.CliPrimaryButtonAction)
                System.Enum.Parse(typeof(UnityCliLoopSettingsWindow.CliPrimaryButtonAction), action);
        }
    }
}
