using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    /// <summary>
    /// Pure unit coverage for directory file-count retention used by uloop output exporters.
    /// </summary>
    [TestFixture]
    public class OutputFileRetentionTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "OutputFileRetentionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_tempDirectory) && Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        /// <summary>
        /// Verifies retention is a no-op when matching files are below the configured limit.
        /// </summary>
        [Test]
        public void DeleteOldestBeyondLimit_WhenFileCountIsBelowLimit_ShouldNotDeleteAnything()
        {
            CreateFileWithWriteTime("file_01.png", DateTime.UtcNow.AddMinutes(-20));
            CreateFileWithWriteTime("file_02.png", DateTime.UtcNow.AddMinutes(-10));
            CreateFileWithWriteTime("file_03.png", DateTime.UtcNow.AddMinutes(-1));

            OutputFileRetention.DeleteOldestBeyondLimit(_tempDirectory, "*.png");

            string[] remaining = Directory.GetFiles(_tempDirectory, "*.png");
            Assert.That(remaining.Length, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies retention is a no-op when matching files are exactly at MAX_FILES_PER_DIRECTORY.
        /// </summary>
        [Test]
        public void DeleteOldestBeyondLimit_WhenFileCountIsAtLimit_ShouldNotDeleteAnything()
        {
            DateTime baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < OutputFileRetention.MAX_FILES_PER_DIRECTORY; i++)
            {
                CreateFileWithWriteTime($"file_{i:D2}.png", baseTime.AddMinutes(i));
            }

            OutputFileRetention.DeleteOldestBeyondLimit(_tempDirectory, "*.png");

            string[] remaining = Directory.GetFiles(_tempDirectory, "*.png");
            Assert.That(remaining.Length, Is.EqualTo(OutputFileRetention.MAX_FILES_PER_DIRECTORY));
        }

        /// <summary>
        /// Verifies oldest matching files are deleted until only MAX_FILES_PER_DIRECTORY remain.
        /// </summary>
        [Test]
        public void DeleteOldestBeyondLimit_WhenFileCountExceedsLimit_ShouldKeepNewestFiles()
        {
            DateTime baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int totalFiles = OutputFileRetention.MAX_FILES_PER_DIRECTORY + 5;
            for (int i = 0; i < totalFiles; i++)
            {
                string fileName = $"file_{i:D2}.png";
                CreateFileWithWriteTime(fileName, baseTime.AddMinutes(i));
            }

            OutputFileRetention.DeleteOldestBeyondLimit(_tempDirectory, "*.png");

            string[] remaining = Directory.GetFiles(_tempDirectory, "*.png")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.That(remaining.Length, Is.EqualTo(OutputFileRetention.MAX_FILES_PER_DIRECTORY));
            Assert.That(remaining[0], Is.EqualTo("file_05.png"));
            Assert.That(remaining[remaining.Length - 1], Is.EqualTo("file_24.png"));
        }

        /// <summary>
        /// Verifies files outside searchPattern are neither counted toward the limit nor deleted.
        /// </summary>
        [Test]
        public void DeleteOldestBeyondLimit_WhenNonMatchingFilesExist_ShouldIgnoreThem()
        {
            DateTime baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            int matchingFiles = OutputFileRetention.MAX_FILES_PER_DIRECTORY + 2;
            for (int i = 0; i < matchingFiles; i++)
            {
                CreateFileWithWriteTime($"match_{i:D2}.json", baseTime.AddMinutes(i));
            }

            CreateFileWithWriteTime(".DS_Store", baseTime.AddMinutes(-100));
            CreateFileWithWriteTime("notes.txt", baseTime.AddMinutes(-50));

            OutputFileRetention.DeleteOldestBeyondLimit(_tempDirectory, "*.json");

            string[] remainingJson = Directory.GetFiles(_tempDirectory, "*.json");
            Assert.That(remainingJson.Length, Is.EqualTo(OutputFileRetention.MAX_FILES_PER_DIRECTORY));
            Assert.That(File.Exists(Path.Combine(_tempDirectory, ".DS_Store")), Is.True);
            Assert.That(File.Exists(Path.Combine(_tempDirectory, "notes.txt")), Is.True);
        }

        /// <summary>
        /// Verifies an empty directory is accepted without throwing or creating files.
        /// </summary>
        [Test]
        public void DeleteOldestBeyondLimit_WhenDirectoryIsEmpty_ShouldNotThrow()
        {
            Assert.DoesNotThrow(() =>
                OutputFileRetention.DeleteOldestBeyondLimit(_tempDirectory, "*.png"));

            Assert.That(Directory.GetFiles(_tempDirectory).Length, Is.EqualTo(0));
        }

        private void CreateFileWithWriteTime(string fileName, DateTime lastWriteTimeUtc)
        {
            string path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(path, fileName);
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }
    }
}
