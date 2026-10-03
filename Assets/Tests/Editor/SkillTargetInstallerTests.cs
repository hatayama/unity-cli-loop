using System;
using System.IO;
using System.Threading;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies cleanup of the grouped managed skills directory when an installed skill is deleted.
    /// </summary>
    [TestFixture]
    public sealed class SkillTargetInstallerTests
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
        /// Verifies that deleting a grouped skill keeps a user file at the managed root, and therefore keeps the managed root itself.
        /// </summary>
        [Test]
        public void DeleteSkillDirectoryIfExists_WhenManagedRootHasUserFile_KeepsFileAndManagedRoot()
        {
            string targetRoot = Path.Combine(_temporaryRoot, ".claude");
            string managedSkillsRoot = SkillInstallLayout.GetManagedSkillsRoot(targetRoot);
            WriteTextFile(Path.Combine(managedSkillsRoot, "uloop-compile", "SKILL.md"), "skill");
            WriteTextFile(Path.Combine(managedSkillsRoot, "notes.txt"), "user notes");

            SkillTargetInstaller.DeleteSkillDirectoryIfExists(
                targetRoot,
                "uloop-compile",
                groupSkillsUnderUnityCliLoop: true,
                CancellationToken.None);

            Assert.That(Directory.Exists(Path.Combine(managedSkillsRoot, "uloop-compile")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(managedSkillsRoot, "notes.txt")), Is.EqualTo("user notes"));
        }

        /// <summary>
        /// Verifies that deleting a grouped skill also removes empty sibling directories under the managed root while keeping non-empty ones.
        /// </summary>
        [Test]
        public void DeleteSkillDirectoryIfExists_WhenManagedRootHasEmptySibling_RemovesOnlyEmptySibling()
        {
            string targetRoot = Path.Combine(_temporaryRoot, ".claude");
            string managedSkillsRoot = SkillInstallLayout.GetManagedSkillsRoot(targetRoot);
            WriteTextFile(Path.Combine(managedSkillsRoot, "uloop-compile", "SKILL.md"), "skill");
            Directory.CreateDirectory(Path.Combine(managedSkillsRoot, "uloop-empty", "nested"));
            WriteTextFile(Path.Combine(managedSkillsRoot, "uloop-other", "SKILL.md"), "other skill");

            SkillTargetInstaller.DeleteSkillDirectoryIfExists(
                targetRoot,
                "uloop-compile",
                groupSkillsUnderUnityCliLoop: true,
                CancellationToken.None);

            Assert.That(Directory.Exists(Path.Combine(managedSkillsRoot, "uloop-compile")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(managedSkillsRoot, "uloop-empty")), Is.False);
            Assert.That(File.Exists(Path.Combine(managedSkillsRoot, "uloop-other", "SKILL.md")), Is.True);
        }

        private static void WriteTextFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }
    }
}
