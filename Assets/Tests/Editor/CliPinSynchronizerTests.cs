using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class CliPinSynchronizerTests
    {
        [Test]
        public void SyncProjectPinFile_WhenDestinationMissing_ShouldCopyPackagePin()
        {
            // Tests that the dispatcher pin contract is published into the project .uloop directory.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectRoot);
                File.WriteAllText(Path.Combine(packageRoot, "cli-pin.json"), "{\"schemaVersion\":1}");

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.True);
                Assert.That(
                    File.ReadAllText(Path.Combine(projectRoot, ".uloop", "cli-pin.json")),
                    Is.EqualTo("{\"schemaVersion\":1}"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SyncProjectPinFile_WhenDestinationMatches_ShouldSkipWrite()
        {
            // Tests that startup does not rewrite the project pin when package and project copies already match.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");
            string projectUloopRoot = Path.Combine(projectRoot, ".uloop");

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectUloopRoot);
                string sourcePath = Path.Combine(packageRoot, "cli-pin.json");
                string destinationPath = Path.Combine(projectUloopRoot, "cli-pin.json");
                File.WriteAllText(sourcePath, "{\"schemaVersion\":1}");
                File.WriteAllText(destinationPath, "{\"schemaVersion\":1}");
                DateTime previousWriteTime = File.GetLastWriteTimeUtc(destinationPath);

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.False);
                Assert.That(File.GetLastWriteTimeUtc(destinationPath), Is.EqualTo(previousWriteTime));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SyncProjectPinFile_WhenPackagePinChanges_ShouldUpdateProjectPin()
        {
            // Tests that package upgrades update the project dispatcher pin contract.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");
            string projectUloopRoot = Path.Combine(projectRoot, ".uloop");

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectUloopRoot);
                File.WriteAllText(Path.Combine(packageRoot, "cli-pin.json"), "{\"schemaVersion\":2}");
                string destinationPath = Path.Combine(projectUloopRoot, "cli-pin.json");
                File.WriteAllText(destinationPath, "{\"schemaVersion\":1}");

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.True);
                Assert.That(File.ReadAllText(destinationPath), Is.EqualTo("{\"schemaVersion\":2}"));
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
