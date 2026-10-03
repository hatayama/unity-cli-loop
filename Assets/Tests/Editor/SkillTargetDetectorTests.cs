using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which agent target directories are reported as install targets.
    /// </summary>
    [TestFixture]
    public sealed class SkillTargetDetectorTests
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
        /// Verifies that a target directory without a skills directory is not returned as an opted-in target.
        /// </summary>
        [Test]
        public void DetectTargetsWithSkillsDirectory_WhenTargetLacksSkillsDirectory_SkipsIt()
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, ".claude", SkillInstallLayout.SkillsDirName));
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, ".codex"));

            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
                SkillTargetDetector.DetectTargetsWithSkillsDirectory(_temporaryRoot);

            Assert.That(targets.Select(target => target.DirName).ToArray(), Is.EqualTo(new[] { ".claude" }));
        }
    }
}
