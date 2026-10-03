using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that one installed skill directory is synchronized with its source files and rolled back on failure.
    /// </summary>
    [TestFixture]
    public sealed class SkillDirectoryContentSynchronizerTests
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
        /// Verifies that syncing an existing skill directory replaces changed files, deletes stale files, and removes directories left empty.
        /// </summary>
        [Test]
        public void SyncInstalledSkillDirectory_WhenDirectoryHasStaleFiles_ReplacesContentAndRemovesStaleEntries()
        {
            string skillDirectory = Path.Combine(_temporaryRoot, "uloop-compile");
            WriteTextFile(Path.Combine(skillDirectory, "SKILL.md"), "old skill");
            WriteTextFile(Path.Combine(skillDirectory, "stale.md"), "stale");
            WriteTextFile(Path.Combine(skillDirectory, "old", "nested", "reference.md"), "old reference");
            Dictionary<string, byte[]> skillFiles = new(StringComparer.Ordinal)
            {
                ["SKILL.md"] = Encoding.UTF8.GetBytes("new skill")
            };

            SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(skillDirectory, skillFiles, CancellationToken.None);

            Assert.That(File.ReadAllText(Path.Combine(skillDirectory, "SKILL.md")), Is.EqualTo("new skill"));
            Assert.That(ListRelativeEntries(skillDirectory), Is.EqualTo(new[] { "SKILL.md" }));
        }

        /// <summary>
        /// Verifies that syncing keeps generated sidecar files such as .meta even though the source does not list them.
        /// </summary>
        [Test]
        public void SyncInstalledSkillDirectory_WhenDirectoryHasExcludedSidecar_KeepsIt()
        {
            string skillDirectory = Path.Combine(_temporaryRoot, "uloop-compile");
            WriteTextFile(Path.Combine(skillDirectory, "SKILL.md"), "skill");
            WriteTextFile(Path.Combine(skillDirectory, "SKILL.md.meta"), "meta");
            Dictionary<string, byte[]> skillFiles = new(StringComparer.Ordinal)
            {
                ["SKILL.md"] = Encoding.UTF8.GetBytes("skill")
            };

            SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(skillDirectory, skillFiles, CancellationToken.None);

            Assert.That(ListRelativeEntries(skillDirectory), Is.EqualTo(new[] { "SKILL.md", "SKILL.md.meta" }));
        }

        /// <summary>
        /// Verifies that the rollback snapshot skips excluded sidecar files and keeps the other files keyed by relative path.
        /// </summary>
        [Test]
        public void ReadSkillFilesForRollback_WhenDirectoryHasExcludedSidecar_SkipsIt()
        {
            string skillDirectory = Path.Combine(_temporaryRoot, "uloop-compile");
            WriteTextFile(Path.Combine(skillDirectory, "SKILL.md"), "skill");
            WriteTextFile(Path.Combine(skillDirectory, "SKILL.md.meta"), "meta");

            Dictionary<string, byte[]> files = SkillDirectoryContentSynchronizer.ReadSkillFilesForRollback(skillDirectory);

            Assert.That(files.Keys.ToArray(), Is.EqualTo(new[] { "SKILL.md" }));
            Assert.That(Encoding.UTF8.GetString(files["SKILL.md"]), Is.EqualTo("skill"));
        }

        /// <summary>
        /// Verifies that a failed sync of an existing skill directory restores the original files.
        /// </summary>
        [Test]
        public void SyncInstalledSkillDirectory_WhenWriteFailsInExistingDirectory_RestoresOriginalFiles()
        {
            string skillDirectory = Path.Combine(_temporaryRoot, "uloop-compile");
            WriteTextFile(Path.Combine(skillDirectory, "SKILL.md"), "original skill");
            WriteTextFile(Path.Combine(skillDirectory, "notes.md"), "original notes");
            SortedDictionary<string, byte[]> skillFiles = CreateSkillFilesThatFailAfterWritingSkillFile();

            Assert.That(
                () => SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(skillDirectory, skillFiles, CancellationToken.None),
                Throws.InstanceOf<IOException>());

            Assert.That(ListRelativeEntries(skillDirectory), Is.EqualTo(new[] { "SKILL.md", "notes.md" }));
            Assert.That(File.ReadAllText(Path.Combine(skillDirectory, "SKILL.md")), Is.EqualTo("original skill"));
            Assert.That(File.ReadAllText(Path.Combine(skillDirectory, "notes.md")), Is.EqualTo("original notes"));
        }

        /// <summary>
        /// Verifies that a failed sync of a new skill directory removes the partially written directory.
        /// </summary>
        [Test]
        public void SyncInstalledSkillDirectory_WhenWriteFailsInNewDirectory_RemovesPartialDirectory()
        {
            string skillDirectory = Path.Combine(_temporaryRoot, "uloop-compile");
            SortedDictionary<string, byte[]> skillFiles = CreateSkillFilesThatFailAfterWritingSkillFile();

            Assert.That(
                () => SkillDirectoryContentSynchronizer.SyncInstalledSkillDirectory(skillDirectory, skillFiles, CancellationToken.None),
                Throws.InstanceOf<IOException>());

            Assert.That(Directory.Exists(skillDirectory), Is.False);
            Assert.That(Directory.Exists(_temporaryRoot), Is.True);
        }

        // SKILL.md is written first, then the nested path needs SKILL.md to be a directory,
        // so directory creation fails after the directory has already been modified.
        // A sorted ordinal dictionary pins that write order: "SKILL.md" is a prefix of
        // "SKILL.md/child.md", whereas Dictionary enumeration order is not guaranteed.
        private static SortedDictionary<string, byte[]> CreateSkillFilesThatFailAfterWritingSkillFile()
        {
            return new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["SKILL.md"] = Encoding.UTF8.GetBytes("replacement skill"),
                ["SKILL.md/child.md"] = Encoding.UTF8.GetBytes("child")
            };
        }

        private static void WriteTextFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        private static string[] ListRelativeEntries(string directory)
        {
            return Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
