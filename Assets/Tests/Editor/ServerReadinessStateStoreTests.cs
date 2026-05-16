using System;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class ServerReadinessStateStoreTests
    {
        [Test]
        public void Delete_WhenSidecarFilesExist_ShouldRemoveAllRecoverableStateFiles()
        {
            // Verifies that clearing readiness state cannot be undone by atomic-write recovery sidecars.
            string projectRoot = CreateTestRoot();
            ServerReadinessStateStore store = new(projectRoot);
            string stateFilePath = store.StateFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(stateFilePath));
            File.WriteAllText(stateFilePath, "{\"phase\":\"ready\"}");
            File.WriteAllText(stateFilePath + AtomicFileWriter.CompletedTempFileSuffix, "{\"phase\":\"recovering\"}");
            File.WriteAllText(stateFilePath + AtomicFileWriter.InProgressTempFileSuffix, "{\"phase\":\"starting\"}");
            File.WriteAllText(stateFilePath + AtomicFileWriter.BackupFileSuffix, "{\"phase\":\"failed\"}");

            store.Delete();

            Assert.That(File.Exists(stateFilePath), Is.False);
            Assert.That(File.Exists(stateFilePath + AtomicFileWriter.CompletedTempFileSuffix), Is.False);
            Assert.That(File.Exists(stateFilePath + AtomicFileWriter.InProgressTempFileSuffix), Is.False);
            Assert.That(File.Exists(stateFilePath + AtomicFileWriter.BackupFileSuffix), Is.False);
            Assert.That(store.Read(), Is.Null);
        }

        private static string CreateTestRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "unity-cli-loop-tests",
                Guid.NewGuid().ToString("N"));
        }
    }
}
