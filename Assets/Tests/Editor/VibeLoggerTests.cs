using System;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests VibeLogger append diagnostics without relying on the Unity request pipeline.
    /// </summary>
    [TestFixture]
    public sealed class VibeLoggerTests
    {
        private const string LogFilePrefix = "unity_vibe";

        [Test]
        public void LogInfo_WhenExistingLogFileHasMalformedOldLine_DoesNotScanWholeFile()
        {
            // Verifies append integrity diagnostics inspect only the newly appended JSONL tail.
            string logFilePath = CurrentLogFilePath();
            string logDirectory = Path.GetDirectoryName(logFilePath);
            Assert.That(logDirectory, Is.Not.Null);
            Directory.CreateDirectory(logDirectory);

            bool hadOriginalFile = File.Exists(logFilePath);
            string originalContent = hadOriginalFile ? File.ReadAllText(logFilePath) : "";

            try
            {
                File.WriteAllText(logFilePath, "{\"operation\":\"old_valid\"}\n{malformed_json\n");

                VibeLoggerService logger = new VibeLoggerService();
                logger.LogInfo(
                    "vibe_logger_tail_validation_test",
                    "Tail validation should ignore malformed historical content.");

                string updatedContent = File.ReadAllText(logFilePath);
                Assert.That(updatedContent, Does.Contain("vibe_logger_tail_validation_test"));
                Assert.That(updatedContent, Does.Not.Contain("vibe_log_write_interleaving_detected"));
            }
            finally
            {
                if (hadOriginalFile)
                {
                    File.WriteAllText(logFilePath, originalContent);
                }
                else if (File.Exists(logFilePath))
                {
                    File.Delete(logFilePath);
                }
            }
        }

        private static string CurrentLogFilePath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.Combine(
                projectRoot,
                UnityCliLoopConstants.OUTPUT_ROOT_DIR,
                UnityCliLoopConstants.VIBE_LOGS_DIR,
                $"{LogFilePrefix}_{DateTime.UtcNow:yyyyMMdd}.json");
        }
    }
}
