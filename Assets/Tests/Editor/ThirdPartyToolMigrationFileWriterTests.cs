using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationFileServiceConstants;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies batch migration writes commit atomically or roll back.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFileWriterTests
    {
        [Test]
        public void WriteBatch_WhenAllTargetsAreWritable_CommitsEveryFileAndLeavesNoSidecars()
        {
            // Verifies a mixed existing/new batch commits all targets and leaves no sidecar files.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string existingFile1 = Path.Combine(tempDirectory, "ExistingOne.cs");
                string existingFile2 = Path.Combine(tempDirectory, "ExistingTwo.cs");
                string newFile = Path.Combine(tempDirectory, "NewFile.cs");
                File.WriteAllText(existingFile1, "original-one");
                File.WriteAllText(existingFile2, "original-two");

                List<MigrationFileChange> changes = new()
                {
                    new MigrationFileChange(existingFile1, "migrated-one"),
                    new MigrationFileChange(existingFile2, "migrated-two"),
                    new MigrationFileChange(newFile, "migrated-new")
                };

                ThirdPartyToolMigrationFileWriter.WriteBatch(changes);

                Assert.That(File.ReadAllText(existingFile1), Is.EqualTo("migrated-one"));
                Assert.That(File.ReadAllText(existingFile2), Is.EqualTo("migrated-two"));
                Assert.That(File.ReadAllText(newFile), Is.EqualTo("migrated-new"));
                Assert.That(CountSidecarFiles(tempDirectory), Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void WriteBatch_WhenPrepareFails_LeavesAllTargetFilesUntouched()
        {
            // Verifies a prepare failure leaves every target untouched and removes temp sidecars.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string existingFile = Path.Combine(tempDirectory, "Existing.cs");
                File.WriteAllText(existingFile, "original");
                string missingDirectoryFile = Path.Combine(
                    tempDirectory,
                    "missing-directory",
                    "Missing.cs");

                List<MigrationFileChange> changes = new()
                {
                    new MigrationFileChange(existingFile, "migrated"),
                    new MigrationFileChange(missingDirectoryFile, "never-written")
                };

                Assert.Throws<DirectoryNotFoundException>(
                    () => ThirdPartyToolMigrationFileWriter.WriteBatch(changes));

                Assert.That(File.ReadAllText(existingFile), Is.EqualTo("original"));
                Assert.That(CountSidecarFiles(tempDirectory), Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void WriteBatch_WhenCommitFails_RestoresCommittedFilesFromBackups()
        {
            // Verifies a mid-batch commit failure restores already committed files from backups.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string file1 = Path.Combine(tempDirectory, "FileOne.cs");
                string file2 = Path.Combine(tempDirectory, "FileTwo.cs");
                string file3 = Path.Combine(tempDirectory, "FileThree.cs");
                File.WriteAllText(file1, "original-one");
                File.WriteAllText(file3, "original-three");
                Directory.CreateDirectory(file2);

                List<MigrationFileChange> changes = new()
                {
                    new MigrationFileChange(file1, "migrated-one"),
                    new MigrationFileChange(file2, "migrated-two"),
                    new MigrationFileChange(file3, "migrated-three")
                };

                Assert.Throws<IOException>(
                    () => ThirdPartyToolMigrationFileWriter.WriteBatch(changes));

                Assert.That(File.ReadAllText(file1), Is.EqualTo("original-one"));
                Assert.That(File.ReadAllText(file3), Is.EqualTo("original-three"));
                Assert.That(CountSidecarFiles(tempDirectory), Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void WriteBatch_WhenCommitFails_DeletesNewlyCreatedFiles()
        {
            // Verifies rollback removes newly created targets that had no backup sidecar.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string newFile = Path.Combine(tempDirectory, "NewFile.cs");
                string failingTarget = Path.Combine(tempDirectory, "FailingTarget.cs");
                Directory.CreateDirectory(failingTarget);

                List<MigrationFileChange> changes = new()
                {
                    new MigrationFileChange(newFile, "migrated-new"),
                    new MigrationFileChange(failingTarget, "never-committed")
                };

                Assert.Throws<IOException>(
                    () => ThirdPartyToolMigrationFileWriter.WriteBatch(changes));

                Assert.That(File.Exists(newFile), Is.False);
                Assert.That(CountSidecarFiles(tempDirectory), Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public async Task WriteBatchAsync_WhenBatchExceedsYieldSize_CommitsEveryFile()
        {
            // Verifies the async prepare yield path still commits every file in a large batch.
            string tempDirectory = CreateTempDirectory();
            try
            {
                List<MigrationFileChange> changes = new();
                for (int index = 0; index < PreviewYieldBatchSize + 8; index++)
                {
                    string filePath = Path.Combine(tempDirectory, $"File{index:D2}.cs");
                    File.WriteAllText(filePath, $"original-{index}");
                    changes.Add(new MigrationFileChange(filePath, $"migrated-{index}"));
                }

                await ThirdPartyToolMigrationFileWriter.WriteBatchAsync(changes);

                for (int index = 0; index < changes.Count; index++)
                {
                    Assert.That(
                        File.ReadAllText(changes[index].FilePath),
                        Is.EqualTo($"migrated-{index}"));
                }

                Assert.That(CountSidecarFiles(tempDirectory), Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private static string CreateTempDirectory()
        {
            string tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopMigrationWriterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private static int CountSidecarFiles(string directory)
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Count(filePath =>
                    filePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
        }
    }
}
