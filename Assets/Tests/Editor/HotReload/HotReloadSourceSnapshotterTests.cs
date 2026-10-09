using System;
using System.Globalization;
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
                "Fixture",
                SuspectsNothing(_tempRoot));

            string firstSnapshot = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/First.cs") + ".cs");
            string secondSnapshot = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Second.cs") + ".cs");
            Assert.That(File.Exists(firstSnapshot), Is.True);
            Assert.That(File.Exists(secondSnapshot), Is.True);
            Assert.That(Directory.Exists(snapshotDirectory + ".tmp"), Is.False);
            Assert.That(Directory.GetFiles(snapshotDirectory).Length, Is.EqualTo(3));
            Assert.That(File.ReadAllBytes(firstSnapshot), Is.EqualTo(firstBytes));
            Assert.That(File.ReadAllBytes(secondSnapshot), Is.EqualTo(secondBytes));
        }

        /// <summary>
        /// Verifies that the capture writes one manifest line with the length and last write time of each copied source, in the order of the source list, and no line for a listed source that does not exist.
        /// </summary>
        [Test]
        public void CaptureAssemblySourcesAtomically_RecordsTheLengthAndWriteTimeOfEachCopiedSource()
        {
            string sourceDirectory = Path.Combine(_tempRoot, "Sources");
            Directory.CreateDirectory(sourceDirectory);
            DateTime firstWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime secondWriteTimeUtc = firstWriteTimeUtc.AddDays(1);
            WriteSourceAt(Path.Combine(sourceDirectory, "First.cs"), new byte[] { 1, 2, 3, 4, 5, 6, 7 }, firstWriteTimeUtc);
            WriteSourceAt(Path.Combine(sourceDirectory, "Second.cs"), new byte[] { 1, 2 }, secondWriteTimeUtc);
            string snapshotDirectory = Path.Combine(_tempRoot, "Fixture-" + Guid.NewGuid().ToString("N"));
            string[] sourceFiles = { "Sources/First.cs", "Sources\\Second.cs", "Sources/Missing.cs" };

            HotReloadSourceSnapshotter.CaptureAssemblySourcesAtomically(
                _tempRoot,
                snapshotDirectory,
                sourceFiles,
                "Fixture",
                SuspectsNothing(_tempRoot));

            string expected = HotReloadConstants.SourceStampManifestHeader + "\n"
                + HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/First.cs") + ".cs\t7\t"
                + firstWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t0\n"
                + HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Second.cs") + ".cs\t2\t"
                + secondWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t0\n";
            Assert.That(
                File.ReadAllText(Path.Combine(snapshotDirectory, HotReloadConstants.SourceStampManifestFileName)),
                Is.EqualTo(expected));
            Assert.That(Directory.Exists(snapshotDirectory + ".tmp"), Is.False);
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
                "Fixture",
                SuspectsNothing(_tempRoot));

            string[] publishedNames = Directory.GetFiles(snapshotDirectory).Select(Path.GetFileName).ToArray();
            // Why EquivalentTo: the order GetFiles lists files in depends on the file system.
            Assert.That(
                publishedNames,
                Is.EquivalentTo(new[]
                {
                    HotReloadSourceSnapshotter.HashProjectRelativePath("Sources/Fresh.cs") + ".cs",
                    HotReloadConstants.SourceStampManifestFileName,
                }));
            Assert.That(Directory.Exists(leftoverDirectory), Is.False);
        }

        /// <summary>
        /// Verifies that a source written before the compile started and unchanged while it was read
        /// keeps its stamp without being checked against the PDB.
        /// </summary>
        [Test]
        public void JudgeCopy_ASourceWrittenBeforeTheStartAndReadWhole_TrustsTheStampWithoutChecking()
        {
            int checks = 0;

            HotReloadSnapshotCopyVerdict verdict = HotReloadSourceSnapshotter.JudgeCopy(
                true,
                99,
                100,
                () =>
                {
                    checks++;
                    return false;
                });

            Assert.That(verdict, Is.EqualTo(HotReloadSnapshotCopyVerdict.StampTrusted));
            Assert.That(checks, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a source written exactly when the compile started is checked, and marked when
        /// it differs from the PDB.
        /// </summary>
        [Test]
        public void JudgeCopy_ASourceWrittenAtTheStartThatDiffersFromThePdb_MarksIt()
        {
            HotReloadSnapshotCopyVerdict verdict = HotReloadSourceSnapshotter.JudgeCopy(true, 100, 100, () => false);

            Assert.That(verdict, Is.EqualTo(HotReloadSnapshotCopyVerdict.EditedAfterCompile));
        }

        /// <summary>
        /// Verifies that a source written after the compile started keeps an unmarked stamp when it
        /// matches the PDB.
        /// </summary>
        [Test]
        public void JudgeCopy_ASourceWrittenAfterTheStartThatMatchesThePdb_TrustsTheStamp()
        {
            HotReloadSnapshotCopyVerdict verdict = HotReloadSourceSnapshotter.JudgeCopy(true, 101, 100, () => true);

            Assert.That(verdict, Is.EqualTo(HotReloadSnapshotCopyVerdict.StampTrusted));
        }

        /// <summary>
        /// Verifies that a source written after the compile started is marked when it differs from the PDB.
        /// </summary>
        [Test]
        public void JudgeCopy_ASourceWrittenAfterTheStartThatDiffersFromThePdb_MarksIt()
        {
            HotReloadSnapshotCopyVerdict verdict = HotReloadSourceSnapshotter.JudgeCopy(true, 101, 100, () => false);

            Assert.That(verdict, Is.EqualTo(HotReloadSnapshotCopyVerdict.EditedAfterCompile));
        }

        /// <summary>
        /// Verifies that a source that changed while it was read is checked even though its write time
        /// is before the start, and gets no stamp when it matches the PDB.
        /// </summary>
        [Test]
        public void JudgeCopy_ASourceThatChangedWhileReadAndMatchesThePdb_LeavesItUnstamped()
        {
            int checks = 0;

            HotReloadSnapshotCopyVerdict verdict = HotReloadSourceSnapshotter.JudgeCopy(
                false,
                99,
                100,
                () =>
                {
                    checks++;
                    return true;
                });

            Assert.That(verdict, Is.EqualTo(HotReloadSnapshotCopyVerdict.NoStamp));
            Assert.That(checks, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a source that changed while it was read is marked when it differs from the PDB,
        /// even though its write time is before the start.
        /// </summary>
        [Test]
        public void JudgeCopy_ASourceThatChangedWhileReadAndDiffersFromThePdb_MarksIt()
        {
            HotReloadSnapshotCopyVerdict verdict = HotReloadSourceSnapshotter.JudgeCopy(false, 99, 100, () => false);

            Assert.That(verdict, Is.EqualTo(HotReloadSnapshotCopyVerdict.EditedAfterCompile));
        }

        // No source is written at or after the end of time, so the capture never reads a compiled assembly.
        private static HotReloadSnapshotSourceCheck SuspectsNothing(string root)
        {
            return new HotReloadSnapshotSourceCheck(
                DateTime.MaxValue.Ticks,
                "unused.dll",
                "unused.pdb",
                "unused",
                new HotReloadPdbDocumentIndex(Path.Combine(root, "PdbDocuments")));
        }

        // Why a write time the test chooses: SetLastWriteTimeUtc stores microseconds while
        // LastWriteTimeUtc reports 100 ns ticks, so only a chosen value can be put back exactly.
        private static void WriteSourceAt(string path, byte[] bytes, DateTime lastWriteTimeUtc)
        {
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
            Assert.That(
                new FileInfo(path).LastWriteTimeUtc,
                Is.EqualTo(lastWriteTimeUtc),
                "The write time must be settable exactly, or the stamp the capture records is not the one the test chose.");
        }
    }
}
