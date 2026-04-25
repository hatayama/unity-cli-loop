using System;
using System.IO;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class ServerBundleCopierTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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

        [Test]
        public void CopyServerBundleWhenChanged_WhenDestinationExists_OverwritesDestination()
        {
            string sourcePath = Path.Combine(_tempDirectory, "source.bundle.js");
            string destinationPath = Path.Combine(_tempDirectory, "Library", "server.bundle.js");

            File.WriteAllText(sourcePath, "new bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            File.WriteAllText(destinationPath, "old bundle");

            bool copied = ServerBundleCopier.CopyServerBundleWhenChanged(sourcePath, destinationPath);

            Assert.That(copied, Is.True);
            Assert.That(File.ReadAllText(destinationPath), Is.EqualTo("new bundle"));
        }

        [Test]
        public void CopyServerBundleWhenChanged_WhenDestinationMissing_CopiesSource()
        {
            string sourcePath = Path.Combine(_tempDirectory, "source.bundle.js");
            string destinationPath = Path.Combine(_tempDirectory, "Library", "server.bundle.js");

            File.WriteAllText(sourcePath, "new bundle");

            bool copied = ServerBundleCopier.CopyServerBundleWhenChanged(sourcePath, destinationPath);

            Assert.That(copied, Is.True);
            Assert.That(File.ReadAllText(destinationPath), Is.EqualTo("new bundle"));
        }
    }
}
