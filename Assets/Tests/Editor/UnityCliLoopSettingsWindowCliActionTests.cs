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
        private const string TestMinimumDispatcherVersion = "3.0.0";

        [TestCase(null, false, true, false)]
        [TestCase("", false, true, false)]
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
            bool result = UnityCliLoopSettingsCliSetupPresenter.ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                cliIsDispatcher,
                canUninstallCli,
                TestMinimumDispatcherVersion);

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
            bool result = UnityCliLoopSettingsCliSetupPresenter.ShouldRepairCliPathFromPrimaryButton(
                needsCliPathSetup,
                needsUpdate);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(true, "3.0.0", true, true, false, "RepairPath")]
        [TestCase(true, "3.0.1", true, true, false, "RepairPath")]
        [TestCase(true, "3.0.0", false, true, false, "InstallOrUpdate")]
        [TestCase(true, "2.9.0", true, true, false, "InstallOrUpdate")]
        [TestCase(false, "3.0.0", true, true, false, "Uninstall")]
        [TestCase(false, "3.0.1", true, true, false, "Uninstall")]
        [TestCase(false, "3.0.0", false, true, false, "InstallOrUpdate")]
        [TestCase(false, "3.0.0", true, false, false, "InstallOrUpdate")]
        [TestCase(false, null, false, true, false, "InstallOrUpdate")]
        [TestCase(false, "", false, true, false, "InstallOrUpdate")]
        [TestCase(false, "3.0.0", true, false, true, "None")]
        [TestCase(false, "2.9.0", true, false, true, "None")]
        [TestCase(false, null, false, false, true, "None")]
        [TestCase(true, "3.0.0", true, false, true, "RepairPath")]
        [TestCase(true, "2.9.0", true, false, true, "RepairPath")]
        public void ResolveCliPrimaryButtonAction_ReturnsClickedPrimaryAction(
            bool needsCliPathSetup,
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli,
            bool isHomebrewManagedCli,
            string expected)
        {
            // Verifies that the Settings window chooses repair only when dispatcher replacement is unnecessary,
            // and that a Homebrew-managed path leaves PATH repair as its only executable action.
            CliSetupPrimaryAction result =
                UnityCliLoopSettingsCliSetupPresenter.ResolveCliPrimaryButtonAction(
                    needsCliPathSetup,
                    cliVersion,
                    cliIsDispatcher,
                    canUninstallCli,
                    isHomebrewManagedCli,
                    TestMinimumDispatcherVersion);

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
            CliSetupPrimaryAction result =
                UnityCliLoopSettingsCliSetupPresenter.ResolveExecutableCliPrimaryButtonAction(
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
            bool result = UnityCliLoopSettingsCliSetupPresenter.ShouldCheckCliPathSetupForPlatform(
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
            bool result = UnityCliLoopSettingsCliSetupPresenter.IsCliUpdateNeeded(
                cliVersion,
                cliIsDispatcher,
                TestMinimumDispatcherVersion);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(SkillInstallState.Missing, false, true)]
        [TestCase(SkillInstallState.Outdated, false, false)]
        [TestCase(SkillInstallState.Missing, true, false)]
        public void ShouldShowSkillsInstalledDialog_ReturnsExpectedValue(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool expected)
        {
            // Verifies that Settings keeps the success dialog for first install only.
            SkillSetupTargetInfo targetInfo = CreateSkillTarget(installState, hasDifferentLayoutSkills);

            bool result = SkillInstallDialogPolicy.ShouldShowForSelectedTarget(targetInfo);

            Assert.That(result, Is.EqualTo(expected));
        }

        private static SkillSetupTargetInfo CreateSkillTarget(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills)
        {
            return new SkillSetupTargetInfo(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true,
                hasDifferentLayoutSkills,
                installState);
        }

        private static CliSetupPrimaryAction ParseAction(string action)
        {
            return (CliSetupPrimaryAction)
                System.Enum.Parse(typeof(CliSetupPrimaryAction), action);
        }
    }
}
