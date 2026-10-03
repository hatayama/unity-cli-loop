using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies install-state detection for one skill under an explicit project root.
    /// </summary>
    [TestFixture]
    public sealed class V3MigrationSkillInstallerTests
    {
        private string _temporaryProjectRoot;

        [SetUp]
        public void SetUpTemporaryProjectRoot()
        {
            _temporaryProjectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryProjectRoot);
        }

        [TearDown]
        public void TearDownTemporaryProjectRoot()
        {
            if (Directory.Exists(_temporaryProjectRoot))
            {
                Directory.Delete(_temporaryProjectRoot, true);
            }
        }

        /// <summary>
        /// Verifies that a skill is reported as missing when the agent target directory does not exist.
        /// </summary>
        [Test]
        public void GetSkillInstallStateAtProjectRoot_WhenTargetDirectoryIsMissing_ReturnsMissing()
        {
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: false,
                hasExistingSkills: false);
            SkillInstallLayout.SkillSourceInfo skill = new(
                "uloop-compile",
                "compile",
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["SKILL.md"] = new byte[] { 0x41 }
                });

            SkillInstallState state = V3MigrationSkillInstaller.GetSkillInstallStateAtProjectRoot(
                _temporaryProjectRoot,
                target,
                skill,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(state, Is.EqualTo(SkillInstallState.Missing));
            Assert.That(Directory.Exists(Path.Combine(_temporaryProjectRoot, ".claude")), Is.False);
        }
    }
}
