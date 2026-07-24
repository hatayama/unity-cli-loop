using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies shared SkillsSetupPanelView pure helpers.
    /// </summary>
    public class SkillsSetupPanelViewTests
    {
        [Test]
        public void FilterInstallableSkillTargets_ExcludesTargetsWithoutSkillsDirectory()
        {
            // Verifies that only targets with a skills directory remain installable.
            List<SkillSetupTargetInfo> targets = new()
            {
                new("Claude Code", ".claude", "--claude", true, true),
                new("Cursor", ".cursor", "--cursor", false, false),
                new("Codex CLI", ".codex", "--codex", true, false, hasDifferentLayoutSkills: true)
            };

            List<SkillSetupTargetInfo> installableTargets =
                SkillsSetupPanelView.FilterInstallableSkillTargets(targets);

            Assert.That(installableTargets.Count, Is.EqualTo(2));
            Assert.That(installableTargets[0].DirName, Is.EqualTo(".claude"));
            Assert.That(installableTargets[1].DirName, Is.EqualTo(".codex"));
        }

        [Test]
        public void CreateFirstInstallSkillTarget_WhenClaudeSelected_ReturnsClaudeProjectTarget()
        {
            // Verifies Claude maps to the project-level first-install target metadata.
            SkillSetupTargetInfo target =
                SkillsSetupPanelView.CreateFirstInstallSkillTarget(SkillsTarget.Claude, true);

            Assert.That(target.DisplayName, Is.EqualTo("Claude Code"));
            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallFlag, Is.EqualTo("--claude"));
            Assert.That(target.HasSkillsDirectory, Is.False);
            Assert.That(target.HasExistingSkills, Is.False);
        }

        [TestCase(SkillsTarget.Cursor, "Cursor", ".cursor", "--cursor")]
        [TestCase(SkillsTarget.Gemini, "Gemini CLI", ".gemini", "--gemini")]
        [TestCase(SkillsTarget.Codex, "Codex CLI", ".codex", "--codex")]
        [TestCase(SkillsTarget.Agents, "Other (.agents)", ".agents", "--agents")]
        public void CreateFirstInstallSkillTarget_ReturnsMappedTarget(
            SkillsTarget targetType,
            string expectedDisplayName,
            string expectedDirName,
            string expectedInstallFlag)
        {
            // Verifies each SkillsTarget maps to the expected first-install metadata.
            SkillSetupTargetInfo target =
                SkillsSetupPanelView.CreateFirstInstallSkillTarget(targetType, true);

            Assert.That(target.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(target.DirName, Is.EqualTo(expectedDirName));
            Assert.That(target.InstallFlag, Is.EqualTo(expectedInstallFlag));
            Assert.That(target.HasSkillsDirectory, Is.False);
            Assert.That(target.HasExistingSkills, Is.False);
        }

        [Test]
        public void CreateFirstInstallSkillTarget_WhenGroupingDisabled_KeepsTargetMetadata()
        {
            // Verifies grouping-off does not change target display/dir/flag metadata.
            SkillSetupTargetInfo target =
                SkillsSetupPanelView.CreateFirstInstallSkillTarget(SkillsTarget.Claude, false);

            Assert.That(target.DisplayName, Is.EqualTo("Claude Code"));
            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallFlag, Is.EqualTo("--claude"));
        }

        [Test]
        public void GetSelectedSkillTargetInfo_WhenDetectedTargetExists_ReturnsDetectedState()
        {
            // Verifies a detected target wins over a synthetic first-install fallback.
            List<SkillSetupTargetInfo> targets = new()
            {
                new(
                    "Claude Code",
                    ".claude",
                    "--claude",
                    hasSkillsDirectory: true,
                    hasExistingSkills: true,
                    installState: SkillInstallState.Installed)
            };

            SkillSetupTargetInfo target = SkillsSetupPanelView.GetSelectedSkillTargetInfo(
                targets,
                SkillsTarget.Claude,
                groupSkillsUnderUnityCliLoop: true);

            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void BuildSingleTargetInstallList_WhenSelectedTargetIsInstalled_ReturnsEmpty()
        {
            // Verifies an already-installed selected target yields an empty install list.
            List<SkillSetupTargetInfo> targets = new()
            {
                new(
                    "Claude Code",
                    ".claude",
                    "--claude",
                    hasSkillsDirectory: true,
                    hasExistingSkills: true,
                    installState: SkillInstallState.Installed)
            };

            List<SkillSetupTargetInfo> installableTargets =
                SkillsSetupPanelView.BuildSingleTargetInstallList(
                    targets,
                    SkillsTarget.Claude,
                    groupSkillsUnderUnityCliLoop: true);

            Assert.That(installableTargets, Is.Empty);
        }

        [Test]
        public void BuildSingleTargetInstallList_WhenSelectedTargetIsMissing_ReturnsMappedTarget()
        {
            // Verifies a missing selected target is mapped into a one-item install list.
            List<SkillSetupTargetInfo> installableTargets =
                SkillsSetupPanelView.BuildSingleTargetInstallList(
                    new List<SkillSetupTargetInfo>(),
                    SkillsTarget.Claude,
                    groupSkillsUnderUnityCliLoop: true);

            Assert.That(installableTargets.Count, Is.EqualTo(1));
            Assert.That(installableTargets[0].DirName, Is.EqualTo(".claude"));
            Assert.That(installableTargets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        [TestCase(SkillInstallState.Installed, false, true, "Installed")]
        [TestCase(SkillInstallState.Checking, false, true, "Checking...")]
        [TestCase(SkillInstallState.Outdated, false, true, "Outdated")]
        [TestCase(SkillInstallState.Missing, false, true, "Missing")]
        [TestCase(SkillInstallState.Missing, true, true, "Not grouped")]
        [TestCase(SkillInstallState.Missing, true, false, "Grouped")]
        public void GetSkillInstallStatusText_ReturnsExpectedLabel(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop,
            string expectedLabel)
        {
            // Verifies each install state maps to the status label shown in the panel list.
            string label = SkillsSetupPanelView.GetSkillInstallStatusText(
                installState,
                hasDifferentLayoutSkills,
                groupSkillsUnderUnityCliLoop);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(SkillInstallState.Installed, false, "skill-target-item__status--installed")]
        [TestCase(SkillInstallState.Checking, false, "skill-target-item__status--checking")]
        [TestCase(SkillInstallState.Outdated, false, "skill-target-item__status--outdated")]
        [TestCase(SkillInstallState.Missing, false, "skill-target-item__status--missing")]
        [TestCase(SkillInstallState.Missing, true, "skill-target-item__status--different-layout")]
        public void GetSkillInstallStatusClass_ReturnsExpectedClass(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            string expectedClass)
        {
            // Verifies each skill install state maps to the shared panel status style class.
            string className = SkillsSetupPanelView.GetSkillInstallStatusClass(
                installState,
                hasDifferentLayoutSkills);

            Assert.That(className, Is.EqualTo(expectedClass));
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
            // Verifies the single-target Install button text follows CLI and install state.
            string text = SkillsSetupPanelView.GetInstallSkillsButtonText(
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
            // Verifies the single-target Install button enablement follows only Skills state.
            bool enabled = SkillsSetupPanelView.IsInstallSkillsButtonEnabled(
                isCliInstalled,
                isInstallingSkills,
                installState);

            Assert.That(enabled, Is.EqualTo(expectedEnabled));
        }

        [TestCase(false, false, false, "Install Skills")]
        [TestCase(true, true, false, "Installing...")]
        [TestCase(true, false, true, "Update Skills")]
        [TestCase(true, false, false, "Install Skills")]
        public void GetBulkInstallButtonText_ReturnsExpectedLabel(
            bool canManageSkills,
            bool isInstallingSkills,
            bool hasOutdatedSkills,
            string expectedLabel)
        {
            // Verifies the bulk Install button text for manageability, install, and outdated states.
            string label = SkillsSetupPanelView.GetBulkInstallButtonText(
                canManageSkills,
                isInstallingSkills,
                hasOutdatedSkills);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [Test]
        public void BuildInstalledSummaryText_ReturnsInstalledForNTargetsFormat()
        {
            // Verifies the status summary uses the shared "Installed for N targets" format.
            string summary = SkillsSetupPanelView.BuildInstalledSummaryText(2);

            Assert.That(summary, Is.EqualTo("Installed for 2 targets"));
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(3, false)]
        public void ShouldExpandSpecificTargetFoldout_ReturnsExpectedValue(
            int installableTargetCount,
            bool expected)
        {
            // Verifies the specific-target foldout auto-expands only when no installable targets exist.
            bool shouldExpand = SkillsSetupPanelView.ShouldExpandSpecificTargetFoldout(
                installableTargetCount);

            Assert.That(shouldExpand, Is.EqualTo(expected));
        }
    }
}
