using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that the skill setup adapter reports detected targets as domain target info.
    /// </summary>
    [TestFixture]
    public sealed class ToolSkillSetupServiceTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUpTemporaryRoot()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDownTemporaryRoot()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, true);
            }
        }

        /// <summary>
        /// Verifies that the freshness-checking detection reports a target without a skills directory as a not opted-in, missing target.
        /// </summary>
        [Test]
        public void DetectSkillTargetsForLayoutAtProjectRoot_WhenTargetHasNoSkillsDirectory_ReportsMissingTarget()
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, ".codex"));
            ToolSkillSetupService service = new(new EmptyToolSettingsPort());

            List<SkillSetupTargetInfo> targets = service.DetectSkillTargetsForLayoutAtProjectRoot(
                _temporaryRoot,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DisplayName, Is.EqualTo("Codex CLI"));
            Assert.That(targets[0].DirName, Is.EqualTo(".codex"));
            Assert.That(targets[0].InstallFlag, Is.EqualTo("--codex"));
            Assert.That(targets[0].HasSkillsDirectory, Is.False);
            Assert.That(targets[0].HasExistingSkills, Is.False);
            Assert.That(targets[0].HasDifferentLayoutSkills, Is.False);
            Assert.That(targets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        /// <summary>
        /// Verifies that the fast detection reports a target without a skills directory as a not opted-in, missing target.
        /// </summary>
        [Test]
        public void DetectSkillTargetsForLayoutFastAtProjectRoot_WhenTargetHasNoSkillsDirectory_ReportsMissingTarget()
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, ".agent"));
            ToolSkillSetupService service = new(new EmptyToolSettingsPort());

            List<SkillSetupTargetInfo> targets = service.DetectSkillTargetsForLayoutFastAtProjectRoot(
                _temporaryRoot,
                groupSkillsUnderUnityCliLoop: true);

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DisplayName, Is.EqualTo("Antigravity"));
            Assert.That(targets[0].DirName, Is.EqualTo(".agent"));
            Assert.That(targets[0].InstallFlag, Is.EqualTo("--antigravity"));
            Assert.That(targets[0].HasSkillsDirectory, Is.False);
            Assert.That(targets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        /// <summary>
        /// Tool settings fake with no disabled tools.
        /// </summary>
        private sealed class EmptyToolSettingsPort : IToolSettingsPort
        {
            public bool IsToolEnabled(string toolName)
            {
                return true;
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
            }

            public string[] GetDisabledTools()
            {
                return Array.Empty<string>();
            }

            public void InvalidateCache()
            {
            }
        }
    }
}
