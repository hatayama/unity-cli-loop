using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        [Test]
        public void WriteBatch_WhenTargetHasUtf8Bom_PreservesBomBytes()
        {
            // Verifies a UTF-8 BOM target keeps EF BB BF after a batch write.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(tempDirectory, "BomFile.cs");
                File.WriteAllText(filePath, "original", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                ThirdPartyToolMigrationFileWriter.WriteBatch(
                    new List<MigrationFileChange>
                    {
                        new MigrationFileChange(filePath, "migrated")
                    });

                byte[] bytes = File.ReadAllBytes(filePath);
                Assert.That(bytes[0], Is.EqualTo(0xEF));
                Assert.That(bytes[1], Is.EqualTo(0xBB));
                Assert.That(bytes[2], Is.EqualTo(0xBF));
                Assert.That(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Is.EqualTo("migrated"));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void WriteBatch_WhenTargetIsUtf16LittleEndian_PreservesEncoding()
        {
            // Verifies a UTF-16 LE BOM target is rewritten as UTF-16 LE with the new content.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(tempDirectory, "Utf16File.cs");
                Encoding utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
                File.WriteAllText(filePath, "original", utf16);

                ThirdPartyToolMigrationFileWriter.WriteBatch(
                    new List<MigrationFileChange>
                    {
                        new MigrationFileChange(filePath, "migrated")
                    });

                byte[] bytes = File.ReadAllBytes(filePath);
                Assert.That(bytes[0], Is.EqualTo(0xFF));
                Assert.That(bytes[1], Is.EqualTo(0xFE));
                Assert.That(utf16.GetString(bytes, 2, bytes.Length - 2), Is.EqualTo("migrated"));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void WriteBatch_WhenTargetHasNoBom_DoesNotAddBom()
        {
            // Verifies a BOM-less UTF-8 target does not gain EF BB BF after a batch write.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(tempDirectory, "NoBomFile.cs");
                File.WriteAllText(filePath, "original", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                ThirdPartyToolMigrationFileWriter.WriteBatch(
                    new List<MigrationFileChange>
                    {
                        new MigrationFileChange(filePath, "migrated")
                    });

                byte[] bytes = File.ReadAllBytes(filePath);
                Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(1));
                Assert.That(bytes[0], Is.Not.EqualTo(0xEF));
                Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo("migrated"));
            }
            finally
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void WriteBatch_WhenContentUsesCrlf_PreservesCrlfBytes()
        {
            // Verifies CRLF content bytes survive the batch write without lone LF rewriting.
            string tempDirectory = CreateTempDirectory();
            try
            {
                string filePath = Path.Combine(tempDirectory, "CrlfFile.cs");
                File.WriteAllText(filePath, "original\r\nline", new UTF8Encoding(false));
                string migratedContent = "migrated\r\nline\r\n";

                ThirdPartyToolMigrationFileWriter.WriteBatch(
                    new List<MigrationFileChange>
                    {
                        new MigrationFileChange(filePath, migratedContent)
                    });

                byte[] bytes = File.ReadAllBytes(filePath);
                string decoded = Encoding.UTF8.GetString(bytes);
                Assert.That(decoded, Is.EqualTo(migratedContent));
                Assert.That(decoded.Contains("\r\n"), Is.True);
                Assert.That(decoded.Replace("\r\n", string.Empty).Contains("\n"), Is.False);
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

        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        /// <summary>
        /// Verifies that a UTF-32 little-endian target is rewritten as UTF-32 little-endian with its BOM.
        /// </summary>
        [Test]
        public void WriteBatch_WhenTargetIsUtf32LittleEndian_PreservesEncoding()
        {
            Encoding encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);

            byte[] bytes = WriteMigratedContentOverOriginal(encoding.GetPreamble().Concat(encoding.GetBytes("original")).ToArray());

            Assert.That(bytes, Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes("migrated")).ToArray()));
        }

        /// <summary>
        /// Verifies that a UTF-32 big-endian target is rewritten as UTF-32 big-endian with its BOM.
        /// </summary>
        [Test]
        public void WriteBatch_WhenTargetIsUtf32BigEndian_PreservesEncoding()
        {
            Encoding encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);

            byte[] bytes = WriteMigratedContentOverOriginal(encoding.GetPreamble().Concat(encoding.GetBytes("original")).ToArray());

            Assert.That(bytes, Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes("migrated")).ToArray()));
        }

        /// <summary>
        /// Verifies that a UTF-16 big-endian target is rewritten as UTF-16 big-endian with its BOM.
        /// </summary>
        [Test]
        public void WriteBatch_WhenTargetIsUtf16BigEndian_PreservesEncoding()
        {
            Encoding encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);

            byte[] bytes = WriteMigratedContentOverOriginal(encoding.GetPreamble().Concat(encoding.GetBytes("original")).ToArray());

            Assert.That(bytes, Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes("migrated")).ToArray()));
        }

        /// <summary>
        /// Verifies that a target shorter than four bytes that is only a UTF-8 BOM still keeps the BOM.
        /// </summary>
        [Test]
        public void WriteBatch_WhenTargetIsOnlyUtf8Bom_PreservesBom()
        {
            byte[] utf8Bom = { 0xEF, 0xBB, 0xBF };

            byte[] bytes = WriteMigratedContentOverOriginal(utf8Bom);

            Assert.That(bytes, Is.EqualTo(utf8Bom.Concat(Encoding.UTF8.GetBytes("migrated")).ToArray()));
        }

        /// <summary>
        /// Verifies that an async batch whose prepare step fails throws and leaves targets and sidecars untouched.
        /// </summary>
        [Test]
        public void WriteBatchAsync_WhenPrepareFails_LeavesTargetUntouchedAndRemovesSidecars()
        {
            string existingFile = Path.Combine(_tempDirectory, "Existing.cs");
            File.WriteAllText(existingFile, "original");
            string missingDirectoryFile = Path.Combine(_tempDirectory, "missing-directory", "Missing.cs");
            List<MigrationFileChange> changes = new List<MigrationFileChange>
            {
                new MigrationFileChange(existingFile, "migrated"),
                new MigrationFileChange(missingDirectoryFile, "never-written")
            };

            Task task = ThirdPartyToolMigrationFileWriter.WriteBatchAsync(changes);

            Assert.That(task.IsCompleted, Is.True);
            Assert.Throws<DirectoryNotFoundException>(() => task.GetAwaiter().GetResult());
            Assert.That(File.ReadAllText(existingFile), Is.EqualTo("original"));
            Assert.That(Directory.GetFiles(_tempDirectory), Is.EqualTo(new[] { existingFile }));
        }

        private byte[] WriteMigratedContentOverOriginal(byte[] originalBytes)
        {
            string filePath = Path.Combine(_tempDirectory, "EncodedFile.cs");
            File.WriteAllBytes(filePath, originalBytes);

            ThirdPartyToolMigrationFileWriter.WriteBatch(
                new List<MigrationFileChange>
                {
                    new MigrationFileChange(filePath, "migrated")
                });

            return File.ReadAllBytes(filePath);
        }
    }
}
