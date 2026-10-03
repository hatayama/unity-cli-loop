using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the platform-independent file paths of <see cref="HotReloadSourceSnapshotter"/>:
    /// stamp reading and the atomic source capture, each run against a per-test temporary directory.
    /// </summary>
    public sealed class HotReloadSourceSnapshotterTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Verifies that a stamp file that does not exist never matches, instead of failing to read it.
        /// </summary>
        [Test]
        public void HasMatchingStamp_WhenStampFileIsMissing_ReturnsFalse()
        {
            string stampPath = Path.Combine(_tempRoot, "Missing.stamp");

            bool matches = HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, 1L, 2L);

            Assert.That(matches, Is.False);
        }

        /// <summary>
        /// Verifies that a stamp whose mtime field is not an integer is rejected.
        /// </summary>
        [Test]
        public void HasMatchingStamp_WhenMtimeFieldIsNotAnInteger_ReturnsFalse()
        {
            string stampPath = Path.Combine(_tempRoot, "Foo.stamp");
            File.WriteAllText(stampPath, Guid.NewGuid().ToString("N") + ",not-a-number,4096");

            bool matches = HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, 0L, 4096L);

            Assert.That(matches, Is.False);
        }

        /// <summary>
        /// Verifies that a stamp whose byte-length field is not an integer is rejected.
        /// </summary>
        [Test]
        public void HasMatchingStamp_WhenByteLengthFieldIsNotAnInteger_ReturnsFalse()
        {
            string stampPath = Path.Combine(_tempRoot, "Foo.stamp");
            File.WriteAllText(stampPath, Guid.NewGuid().ToString("N") + ",123,not-a-number");

            bool matches = HotReloadSourceSnapshotter.HasMatchingStamp(stampPath, 123L, 0L);

            Assert.That(matches, Is.False);
        }

        /// <summary>
        /// Verifies that the capture copies each existing source byte for byte under the hash of its slash-normalized path, skips a listed source that does not exist, and publishes the snapshot only under its final name.
        /// </summary>
        [Test]
        public void CaptureAssemblySourcesAtomically_ExistingAndMissingSources_CopiesExistingByteExactUnderFinalDirectory()
        {
            string sourceDirectory = Path.Combine(_tempRoot, "Sources");
            Directory.CreateDirectory(sourceDirectory);
            byte[] firstBytes = { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n', (byte)'b' };
            byte[] secondBytes = { (byte)'c', (byte)'\n' };
            File.WriteAllBytes(Path.Combine(sourceDirectory, "First.cs"), firstBytes);
            File.WriteAllBytes(Path.Combine(sourceDirectory, "Second.cs"), secondBytes);
            string snapshotDirectory = Path.Combine(_tempRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            string[] sourceFiles = { "Sources/First.cs", "Sources\\Second.cs", "Sources/Missing.cs" };

            HotReloadSourceSnapshotter.CaptureAssemblySourcesAtomically(
                _tempRoot,
                snapshotDirectory,
                sourceFiles,
                "Fixture");

            string firstSnapshot = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/First.cs") + ".cs");
            string secondSnapshot = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Second.cs") + ".cs");
            Assert.That(File.Exists(firstSnapshot), Is.True);
            Assert.That(File.Exists(secondSnapshot), Is.True);
            Assert.That(Directory.Exists(snapshotDirectory + ".tmp"), Is.False);
            Assert.That(Directory.GetFiles(snapshotDirectory).Length, Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(firstSnapshot), Is.EqualTo(firstBytes));
            Assert.That(File.ReadAllBytes(secondSnapshot), Is.EqualTo(secondBytes));
        }

        /// <summary>
        /// Verifies that a leftover temporary directory from an interrupted capture is discarded, so its stale files never reach the published snapshot.
        /// </summary>
        [Test]
        public void CaptureAssemblySourcesAtomically_WhenTemporaryDirectoryIsLeftOver_PublishesOnlyFreshSources()
        {
            string sourceDirectory = Path.Combine(_tempRoot, "Sources");
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(Path.Combine(sourceDirectory, "Fresh.cs"), "fresh\n");
            string snapshotDirectory = Path.Combine(_tempRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            string leftoverDirectory = snapshotDirectory + ".tmp";
            Directory.CreateDirectory(leftoverDirectory);
            File.WriteAllText(Path.Combine(leftoverDirectory, "stale.cs"), "stale\n");

            HotReloadSourceSnapshotter.CaptureAssemblySourcesAtomically(
                _tempRoot,
                snapshotDirectory,
                new[] { "Sources/Fresh.cs" },
                "Fixture");

            string[] publishedNames = Directory.GetFiles(snapshotDirectory).Select(Path.GetFileName).ToArray();
            Assert.That(
                publishedNames,
                Is.EqualTo(new[] { HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Fresh.cs") + ".cs" }));
            Assert.That(Directory.Exists(leftoverDirectory), Is.False);
        }
    }
}
