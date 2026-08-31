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
    }
}
