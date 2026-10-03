using System;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class AtomicFileWriterTests
    {
        [Test]
        public void RecoverSidecarFiles_WhenOnlyInProgressTempExists_ShouldLeaveTargetMissing()
        {
            // Tests that recovery does not promote a file that may still be mid-write.
            string root = CreateTestRoot();
            string filePath = Path.Combine(root, "state.json");
            Directory.CreateDirectory(root);

            try
            {
                File.WriteAllText(filePath + ".tmp.write", "{\"phase\":\"starting\"}");

                AtomicFileWriter.RecoverSidecarFiles(filePath);

                Assert.That(File.Exists(filePath), Is.False);
                Assert.That(File.Exists(filePath + ".tmp.write"), Is.True);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Write_WhenTargetMissing_ShouldPublishTargetAndRemoveInProgressTemp()
        {
            // Tests that the externally visible temp sidecar is only used after content is fully written.
            string root = CreateTestRoot();
            string filePath = Path.Combine(root, "state.json");
            Directory.CreateDirectory(root);

            try
            {
                AtomicFileWriter.Write(filePath, "{\"phase\":\"ready\"}");

                Assert.That(File.ReadAllText(filePath), Is.EqualTo("{\"phase\":\"ready\"}"));
                Assert.That(File.Exists(filePath + ".tmp.write"), Is.False);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static string CreateTestRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "unity-cli-loop-tests",
                Guid.NewGuid().ToString("N"));
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
        /// Verifies that writing over an existing target replaces a stale backup with the previous target content.
        /// </summary>
        [Test]
        public void Write_WhenTargetAndStaleBackupExist_RotatesPreviousTargetIntoBackup()
        {
            string filePath = Path.Combine(_tempDirectory, "settings.json");
            string backupFilePath = filePath + AtomicFileWriter.BackupFileSuffix;
            File.WriteAllText(filePath, "previous");
            File.WriteAllText(backupFilePath, "stale");

            AtomicFileWriter.Write(filePath, "current");

            Assert.That(File.ReadAllText(filePath), Is.EqualTo("current"));
            Assert.That(File.ReadAllText(backupFilePath), Is.EqualTo("previous"));
        }

        /// <summary>
        /// Verifies that an in-progress temp file left by an interrupted write is deleted.
        /// </summary>
        [Test]
        public void CleanupInProgressTemp_WhenFileExists_DeletesIt()
        {
            string inProgressTempFilePath =
                Path.Combine(_tempDirectory, "settings.json" + AtomicFileWriter.InProgressTempFileSuffix);
            File.WriteAllText(inProgressTempFilePath, "partial");

            AtomicFileWriter.CleanupInProgressTemp(inProgressTempFilePath);

            Assert.That(File.Exists(inProgressTempFilePath), Is.False);
        }
    }
}
