using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Skills Target Selection Resolver behavior.
    /// </summary>
    public class SkillsTargetSelectionResolverTests
    {
        [TestCase(SkillsTarget.Claude, true, "Claude Code", ".claude", "skills install --claude")]
        [TestCase(SkillsTarget.Codex, true, "Codex CLI", ".codex", "skills install --codex")]
        [TestCase(SkillsTarget.Agents, true, "Common", ".agents", "skills install --agents")]
        [TestCase(SkillsTarget.Claude, false, "Claude Code", ".claude", "skills install --claude --flat")]
        public void Resolve_ReturnsMappedSelection(
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop,
            string expectedDisplayName,
            string expectedDirectoryName,
            string expectedInstallArguments)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);

            Assert.That(selection.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(selection.DirectoryName, Is.EqualTo(expectedDirectoryName));
            Assert.That(selection.InstallArguments, Is.EqualTo(expectedInstallArguments));
        }

        [Test]
        public void Resolve_DisplayNameDoesNotContainDirectoryName_ForAllTargets()
        {
            // Verifies list labels can append ({DirectoryName}/) without duplicating the directory name.
            foreach (SkillsTarget target in Enum.GetValues(typeof(SkillsTarget)))
            {
                SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(target, true);

                Assert.That(
                    selection.DisplayName,
                    Does.Not.Contain(selection.DirectoryName),
                    $"DisplayName '{selection.DisplayName}' must not include DirectoryName '{selection.DirectoryName}' for {target}");
            }
        }

        [TestCase(SkillsTarget.Claude, true)]
        [TestCase(SkillsTarget.Codex, false)]
        [TestCase(SkillsTarget.Agents, true)]
        public void IsInstalled_ReturnsExpectedStateForTarget(
            SkillsTarget target,
            bool expectedInstalled)
        {
            CliSetupData data = new(
                isCliInstalled: true,
                cliVersion: "1.7.3",
                requiredCliVersion: "1.7.3",
                needsUpdate: false,
                canUninstallCli: true,
                needsCliPathSetup: false,
                isInstallingCli: false,
                isChecking: false,
                isSkillStateChecking: false,
                isClaudeSkillsInstalled: true,
                isAgentsSkillsInstalled: true,
                isCodexSkillsInstalled: false,
                isAntigravitySkillsInstalled: false,
                selectedTargetInstallState: SkillInstallState.Installed,
                selectedTarget: target,
                groupSkillsUnderUnityCliLoop: true,
                isInstallingSkills: false,
                installableSkillTargets: System.Array.Empty<SkillSetupTargetInfo>());

            bool isInstalled = SkillsTargetSelectionResolver.IsInstalled(data, target);

            Assert.That(isInstalled, Is.EqualTo(expectedInstalled));
        }

        [Test]
        public void Resolve_ThrowsForUnknownTarget()
        {
            SkillsTarget invalidTarget = (SkillsTarget)999;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => SkillsTargetSelectionResolver.Resolve(invalidTarget, true));
        }
    }
}
