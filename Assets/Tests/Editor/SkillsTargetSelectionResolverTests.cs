using System;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class SkillsTargetSelectionResolverTests
    {
        [TestCase(SkillsTarget.Claude, "Claude Code", ".claude", "skills install --claude")]
        [TestCase(SkillsTarget.Cursor, "Cursor", ".cursor", "skills install --cursor")]
        [TestCase(SkillsTarget.Gemini, "Gemini CLI", ".gemini", "skills install --gemini")]
        [TestCase(SkillsTarget.Codex, "Codex CLI", ".codex", "skills install --codex")]
        [TestCase(SkillsTarget.Agents, "Other (.agents)", ".agents", "skills install --agents")]
        public void Resolve_ReturnsMappedSelection(
            SkillsTarget target,
            string expectedDisplayName,
            string expectedDirectoryName,
            string expectedInstallArguments)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(target);

            Assert.That(selection.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(selection.DirectoryName, Is.EqualTo(expectedDirectoryName));
            Assert.That(selection.InstallArguments, Is.EqualTo(expectedInstallArguments));
        }

        [TestCase(SkillsTarget.Claude, true)]
        [TestCase(SkillsTarget.Cursor, false)]
        [TestCase(SkillsTarget.Gemini, true)]
        [TestCase(SkillsTarget.Codex, false)]
        [TestCase(SkillsTarget.Agents, true)]
        public void IsInstalled_ReturnsExpectedStateForTarget(
            SkillsTarget target,
            bool expectedInstalled)
        {
            CliSetupData data = new(
                isCliInstalled: true,
                cliVersion: "1.7.3",
                packageVersion: "1.7.3",
                needsUpdate: false,
                needsDowngrade: false,
                isInstallingCli: false,
                isChecking: false,
                isClaudeSkillsInstalled: true,
                isAgentsSkillsInstalled: true,
                isCursorSkillsInstalled: false,
                isGeminiSkillsInstalled: true,
                isCodexSkillsInstalled: false,
                isAntigravitySkillsInstalled: false,
                selectedTarget: target,
                isInstallingSkills: false);

            bool isInstalled = SkillsTargetSelectionResolver.IsInstalled(data, target);

            Assert.That(isInstalled, Is.EqualTo(expectedInstalled));
        }

        [Test]
        public void Resolve_ThrowsForUnknownTarget()
        {
            SkillsTarget invalidTarget = (SkillsTarget)999;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => SkillsTargetSelectionResolver.Resolve(invalidTarget));
        }
    }
}
