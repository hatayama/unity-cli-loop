using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests count-based retention for VibeLogger day files.
    /// </summary>
    public class VibeLoggerRetentionTests
    {
        /// <summary>
        /// Verifies VibeLogger keeps only the newest files up to the shared output retention limit.
        /// </summary>
        [Test]
        public void DeleteOldestLogFilesBeyondLimit_WhenLimitIsExceeded_KeepsNewestFiles()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "vibe-logger-retention-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);

            try
            {
                int totalFileCount = VibeLoggerService.MAX_LOG_FILES + 2;
                DateTime baseWriteTimeUtc = DateTime.UtcNow.AddDays(-totalFileCount);
                for (int index = 0; index < totalFileCount; index++)
                {
                    string filePath = Path.Combine(
                        temporaryDirectory,
                        "unity_vibe_202601" + index.ToString("D2") + ".json");
                    File.WriteAllText(filePath, index.ToString());
                    File.SetLastWriteTimeUtc(filePath, baseWriteTimeUtc.AddDays(index));
                }

                VibeLoggerService.DeleteOldestLogFilesBeyondLimit(temporaryDirectory);

                string[] remainingFiles = Directory.GetFiles(temporaryDirectory, "unity_vibe_*.json");
                Assert.That(remainingFiles, Has.Length.EqualTo(VibeLoggerService.MAX_LOG_FILES));
                Assert.That(
                    File.Exists(Path.Combine(temporaryDirectory, "unity_vibe_20260100.json")),
                    Is.False);
                Assert.That(
                    File.Exists(Path.Combine(temporaryDirectory, "unity_vibe_20260101.json")),
                    Is.False);
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }

        /// <summary>
        /// Verifies the write path itself prunes old day files when appending creates a new day file,
        /// so retention cannot regress to running only after size rotation.
        /// </summary>
        [Test]
        public void AppendLogLineWithRetention_WhenNewDayFileIsCreated_PrunesOldestFiles()
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "vibe-logger-retention-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);

            try
            {
                int preexistingFileCount = VibeLoggerService.MAX_LOG_FILES + 2;
                DateTime baseWriteTimeUtc = DateTime.UtcNow.AddDays(-preexistingFileCount - 1);
                for (int index = 0; index < preexistingFileCount; index++)
                {
                    string filePath = Path.Combine(
                        temporaryDirectory,
                        "unity_vibe_202601" + index.ToString("D2") + ".json");
                    File.WriteAllText(filePath, index.ToString());
                    File.SetLastWriteTimeUtc(filePath, baseWriteTimeUtc.AddDays(index));
                }

                VibeLoggerService.AppendLogLineWithRetention(temporaryDirectory, "{}\n");

                string todayFilePath = Path.Combine(
                    temporaryDirectory,
                    $"unity_vibe_{DateTime.UtcNow:yyyyMMdd}.json");
                string[] remainingFiles = Directory.GetFiles(temporaryDirectory, "unity_vibe_*.json");
                Assert.That(File.Exists(todayFilePath), Is.True);
                Assert.That(remainingFiles, Has.Length.EqualTo(VibeLoggerService.MAX_LOG_FILES));
                Assert.That(
                    File.Exists(Path.Combine(temporaryDirectory, "unity_vibe_20260100.json")),
                    Is.False);
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
