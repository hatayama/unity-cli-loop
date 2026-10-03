using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the full startup target scan for projects the preflight scan cannot decide.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTargetScannerTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        /// <summary>
        /// Verifies that a project whose only migration marker is a comment has no migration target.
        /// </summary>
        [Test]
        public void HasMigrationTargetAsync_WhenOnlyCommentMentionsLegacyNamespace_ReturnsFalse()
        {
            WriteAssetFile("Notes.cs", "// using io.github.hatayama.uLoopMCP;\npublic sealed class Notes {}");

            Task<bool> task = ThirdPartyToolMigrationTargetScanner.HasMigrationTargetAsync(_projectRoot, CancellationToken.None);

            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.False);
        }

        private void WriteAssetFile(string fileName, string content)
        {
            string directory = Path.Combine(_projectRoot, "Assets", "VendorTools");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }
    }
}
