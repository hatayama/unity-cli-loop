using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies UnityEditor-independent Settings decision policies extracted for Presentation Move Method.
    /// </summary>
    public class SettingsPresentationDecisionPolicyTests
    {
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, false)]
        public void CliPathSetupCheckPolicy_ShouldCheck_UsesWindowsAndOwnership(
            bool isWindowsEditor,
            bool hasPackageOwnedCurrentUserInstall,
            bool expected)
        {
            // Verifies PATH checks stay off for Windows and non package-owned installs.
            bool result = CliPathSetupCheckPolicy.ShouldCheck(
                isWindowsEditor,
                hasPackageOwnedCurrentUserInstall);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void SkillInstallDialogPolicy_ShouldShowForInstallableTargets_RequiresAllEligible()
        {
            // Verifies wizard dialog policy requires every target to be first-install eligible.
            SkillSetupTargetInfo[] targets =
            {
                CreateSkillTarget(SkillInstallState.Missing, false),
                CreateSkillTarget(SkillInstallState.Outdated, false)
            };

            bool result = SkillInstallDialogPolicy.ShouldShowForInstallableTargets(targets);

            Assert.That(result, Is.False);
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
    }
}
