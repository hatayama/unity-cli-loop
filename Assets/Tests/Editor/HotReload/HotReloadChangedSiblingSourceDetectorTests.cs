using System;
using System.Globalization;
using System.IO;
using System.Text;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for snapshot-vs-disk sibling change detection used by const-drift scanning,
    /// and for the check that one source still holds its snapshot bytes.
    /// </summary>
    public class HotReloadChangedSiblingSourceDetectorTests
    {
        // Why fixed write times: the detector reuses a verdict while a file's length and write time
        // stay the same, so each test sets the stamp it means instead of relying on the clock.
        private static readonly DateTime FirstWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime SecondWriteTimeUtc = FirstWriteTimeUtc.AddDays(1);

        /// <summary>
        /// What: a sibling whose on-disk bytes differ from its snapshot is returned, and the
        /// edited file itself is excluded even when it also differs.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenSiblingBytesDiffer_ReturnsSiblingAndExcludesEditedFile()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string editedRelative = "Assets/Edited.cs";
                string siblingRelative = "Assets/Sibling.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteProjectFile(projectRoot, siblingRelative, "sibling-disk");
                WriteSnapshot(projectRoot, "Asm-mvid", editedRelative, "edited-snapshot");
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-snapshot");

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { editedRelative, siblingRelative },
                        new[] { editedRelative });

                Assert.That(result.ChangedSiblingAbsolutePaths, Has.Length.EqualTo(1));
                Assert.That(
                    result.ChangedSiblingAbsolutePaths[0],
                    Is.EqualTo(AbsoluteProjectPath(projectRoot, siblingRelative)));
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: every file edited in the same run is excluded, so a file transformed together
        /// with its group is not also reported as a drifted sibling.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenTwoFilesAreEdited_ExcludesBothOfThem()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string firstEditedRelative = "Assets/FirstEdited.cs";
                string secondEditedRelative = "Assets/SecondEdited.cs";
                string siblingRelative = "Assets/Sibling.cs";
                WriteProjectFile(projectRoot, firstEditedRelative, "first-disk");
                WriteProjectFile(projectRoot, secondEditedRelative, "second-disk");
                WriteProjectFile(projectRoot, siblingRelative, "sibling-disk");
                WriteSnapshot(projectRoot, "Asm-mvid", firstEditedRelative, "first-snapshot");
                WriteSnapshot(projectRoot, "Asm-mvid", secondEditedRelative, "second-snapshot");
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-snapshot");

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { firstEditedRelative, secondEditedRelative, siblingRelative },
                        new[] { firstEditedRelative, secondEditedRelative });

                Assert.That(result.ChangedSiblingAbsolutePaths, Has.Length.EqualTo(1));
                Assert.That(
                    result.ChangedSiblingAbsolutePaths[0],
                    Is.EqualTo(AbsoluteProjectPath(projectRoot, siblingRelative)));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a missing snapshot directory returns no siblings and no cap warning.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenSnapshotDirectoryMissing_ReturnsEmpty()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string editedRelative = "Assets/Edited.cs";
                string siblingRelative = "Assets/Sibling.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteProjectFile(projectRoot, siblingRelative, "sibling-disk");

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { editedRelative, siblingRelative },
                        new[] { editedRelative });

                Assert.That(result.ChangedSiblingAbsolutePaths, Is.Empty);
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a missing DLL skips the snapshot walk instead of throwing.
        /// </summary>
        [Test]
        public void Detect_WhenDllMissing_ReturnsEmpty()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                HotReloadChangedSiblingScanResult result = HotReloadChangedSiblingSourceDetector.Detect(
                    projectRoot,
                    "Asm",
                    Path.Combine(projectRoot, "missing.dll"),
                    new[] { "Assets/Edited.cs", "Assets/Sibling.cs" },
                    new[] { "Assets/Edited.cs" });

                Assert.That(result.ChangedSiblingAbsolutePaths, Is.Empty);
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a snapshot-identical sibling is omitted while a byte-changed sibling is returned.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenOneSiblingMatchesSnapshot_OmitsTheIdenticalSibling()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string editedRelative = "Assets/Edited.cs";
                string changedRelative = "Assets/ChangedSibling.cs";
                string identicalRelative = "Assets/IdenticalSibling.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteProjectFile(projectRoot, changedRelative, "changed-disk");
                WriteProjectFile(projectRoot, identicalRelative, "identical-bytes");
                WriteSnapshot(projectRoot, "Asm-mvid", editedRelative, "edited-snapshot");
                WriteSnapshot(projectRoot, "Asm-mvid", changedRelative, "changed-snapshot");
                WriteSnapshot(projectRoot, "Asm-mvid", identicalRelative, "identical-bytes");

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { editedRelative, changedRelative, identicalRelative },
                        new[] { editedRelative });

                Assert.That(result.ChangedSiblingAbsolutePaths, Has.Length.EqualTo(1));
                Assert.That(
                    result.ChangedSiblingAbsolutePaths[0],
                    Is.EqualTo(AbsoluteProjectPath(projectRoot, changedRelative)));
                Assert.That(
                    result.ChangedSiblingAbsolutePaths,
                    Does.Not.Contain(AbsoluteProjectPath(projectRoot, identicalRelative)));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: 51 changed siblings truncates the path list to 50 and emits the cap warning
        /// with the fixed 50/51 wording.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenMoreThanLimitChanged_TruncatesAndWarns()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                const int changedSiblingCount = 51;
                string editedRelative = "Assets/Edited.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteSnapshot(projectRoot, "Asm-mvid", editedRelative, "edited-snapshot");

                string[] sourceFiles = new string[changedSiblingCount + 1];
                sourceFiles[0] = editedRelative;
                for (int index = 0; index < changedSiblingCount; index++)
                {
                    string relative = "Assets/Sibling" + index.ToString(CultureInfo.InvariantCulture) + ".cs";
                    sourceFiles[index + 1] = relative;
                    WriteProjectFile(projectRoot, relative, "disk-" + index.ToString(CultureInfo.InvariantCulture));
                    WriteSnapshot(projectRoot, "Asm-mvid", relative, "snap-" + index.ToString(CultureInfo.InvariantCulture));
                }

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        sourceFiles,
                        new[] { editedRelative });

                Assert.That(result.ChangedSiblingAbsolutePaths, Has.Length.EqualTo(50));
                Assert.That(
                    result.ScanLimitWarning,
                    Is.EqualTo("sibling const-drift scan limited to first 50 changed files (51 total)"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: without a snapshot directory nothing was compared, so the empty list is reported as
        /// incomplete rather than as "no sibling changed".
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenSnapshotDirectoryMissing_IsNotComplete()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string editedRelative = "Assets/Edited.cs";
                string siblingRelative = "Assets/Sibling.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteProjectFile(projectRoot, siblingRelative, "sibling-disk");

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { editedRelative, siblingRelative },
                        new[] { editedRelative });

                Assert.That(result.IsComplete, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a list cut down to the scan limit is reported as incomplete, because a changed
        /// sibling past the limit is missing from it.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenMoreThanLimitChanged_IsNotComplete()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                const int changedSiblingCount = 51;
                string editedRelative = "Assets/Edited.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteSnapshot(projectRoot, "Asm-mvid", editedRelative, "edited-snapshot");

                string[] sourceFiles = new string[changedSiblingCount + 1];
                sourceFiles[0] = editedRelative;
                for (int index = 0; index < changedSiblingCount; index++)
                {
                    string relative = "Assets/Sibling" + index.ToString(CultureInfo.InvariantCulture) + ".cs";
                    sourceFiles[index + 1] = relative;
                    WriteProjectFile(projectRoot, relative, "disk-" + index.ToString(CultureInfo.InvariantCulture));
                    WriteSnapshot(projectRoot, "Asm-mvid", relative, "snap-" + index.ToString(CultureInfo.InvariantCulture));
                }

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        sourceFiles,
                        new[] { editedRelative });

                Assert.That(result.IsComplete, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: when every sibling was compared with its snapshot and the list stayed under the
        /// limit, the list is reported as complete.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenEverySiblingWasCompared_IsComplete()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string editedRelative = "Assets/Edited.cs";
                string siblingRelative = "Assets/Sibling.cs";
                WriteProjectFile(projectRoot, editedRelative, "edited-disk");
                WriteProjectFile(projectRoot, siblingRelative, "sibling-disk");
                WriteSnapshot(projectRoot, "Asm-mvid", editedRelative, "edited-snapshot");
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-snapshot");

                HotReloadChangedSiblingScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { editedRelative, siblingRelative },
                        new[] { editedRelative });

                Assert.That(result.IsComplete, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a source holding the bytes of its snapshot matches it, read from the path given
        /// rather than from the project file.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshotDirectory_SameBytes_IsTrue()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string revertedRelative = "Assets/Reverted.cs";
                string workerCopyRelative = "Temp/WorkerCopy/Reverted.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", revertedRelative, "compiled-bytes");
                WriteProjectFile(projectRoot, workerCopyRelative, "compiled-bytes");

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshotDirectory(
                    projectRoot,
                    "Asm-mvid",
                    revertedRelative,
                    AbsoluteProjectPath(projectRoot, workerCopyRelative));

                Assert.That(matches, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a source whose bytes differ from its snapshot does not match, even while the project
        /// file at the same path still holds the snapshot bytes, because the given source is what the
        /// worker reads.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshotDirectory_DifferentBytes_IsFalse()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string revertedRelative = "Assets/Reverted.cs";
                string workerCopyRelative = "Temp/WorkerCopy/Reverted.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", revertedRelative, "compiled-bytes");
                WriteProjectFile(projectRoot, revertedRelative, "compiled-bytes");
                WriteProjectFile(projectRoot, workerCopyRelative, "edited-bytes");

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshotDirectory(
                    projectRoot,
                    "Asm-mvid",
                    revertedRelative,
                    AbsoluteProjectPath(projectRoot, workerCopyRelative));

                Assert.That(matches, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a file the snapshot directory holds no copy of does not match, so a missing
        /// snapshot is never read as "back at its compiled source".
        /// </summary>
        [Test]
        public void SourceMatchesSnapshotDirectory_NoSnapshotFile_IsFalse()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string revertedRelative = "Assets/Reverted.cs";
                string workerCopyRelative = "Temp/WorkerCopy/Reverted.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", "Assets/Other.cs", "compiled-bytes");
                WriteProjectFile(projectRoot, workerCopyRelative, "compiled-bytes");

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshotDirectory(
                    projectRoot,
                    "Asm-mvid",
                    revertedRelative,
                    AbsoluteProjectPath(projectRoot, workerCopyRelative));

                Assert.That(matches, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a source path with no file behind it does not match, instead of throwing.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshotDirectory_NoSourceFile_IsFalse()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string revertedRelative = "Assets/Reverted.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", revertedRelative, "compiled-bytes");

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshotDirectory(
                    projectRoot,
                    "Asm-mvid",
                    revertedRelative,
                    AbsoluteProjectPath(projectRoot, "Temp/WorkerCopy/Reverted.cs"));

                Assert.That(matches, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: without the compiled DLL the snapshot directory cannot be named, so the source does
        /// not match.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshot_NoDll_IsFalse()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string workerCopyRelative = "Temp/WorkerCopy/Reverted.cs";
                WriteProjectFile(projectRoot, workerCopyRelative, "compiled-bytes");

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshot(
                    projectRoot,
                    "Asm",
                    Path.Combine(projectRoot, "missing.dll"),
                    "Assets/Reverted.cs",
                    AbsoluteProjectPath(projectRoot, workerCopyRelative));

                Assert.That(matches, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a DLL without its PDB has no snapshot to compare with, so the source does not match
        /// and the DLL is not read.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshot_NoPdb_IsFalse()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string workerCopyRelative = "Temp/WorkerCopy/Reverted.cs";
                string dllPath = Path.Combine(projectRoot, "Fixture.dll");
                File.WriteAllBytes(dllPath, Array.Empty<byte>());
                WriteProjectFile(projectRoot, workerCopyRelative, "compiled-bytes");

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshot(
                    projectRoot,
                    "Asm",
                    dllPath,
                    "Assets/Reverted.cs",
                    AbsoluteProjectPath(projectRoot, workerCopyRelative));

                Assert.That(matches, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling that matched its snapshot is reported once it is rewritten with another
        /// length while its write time stays the same, so the length alone triggers a new comparison.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_SecondScanAfterRewriteWithNewLength_ReturnsSibling()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                Assert.That(ScanSibling(projectRoot, "Asm-mvid", siblingRelative), Is.Empty);

                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA-longer", FirstWriteTimeUtc);

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling that matched its snapshot is reported once it is rewritten with the same
        /// length and a later write time, so the write time alone triggers a new comparison.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_SecondScanAfterRewriteWithNewWriteTime_ReturnsSibling()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                Assert.That(ScanSibling(projectRoot, "Asm-mvid", siblingRelative), Is.Empty);

                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-BBBB", SecondWriteTimeUtc);

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling rewritten with the same length while its write time is put back keeps
        /// the earlier "matches" verdict. This is a known limit: a change that keeps both length and
        /// write time (a copy that preserves timestamps) is not noticed until the next compile.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_SecondScanWithSameStamp_KeepsTheMatchVerdict()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                Assert.That(ScanSibling(projectRoot, "Asm-mvid", siblingRelative), Is.Empty);

                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-BBBB", FirstWriteTimeUtc);

                Assert.That(ScanSibling(projectRoot, "Asm-mvid", siblingRelative), Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a verdict against one assembly generation's snapshot is not reused against
        /// another generation's snapshot of the same file.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_SameSiblingAgainstAnotherSnapshotDirectory_ComparesAgain()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid1", siblingRelative, "sibling-AAAA");
                WriteSnapshot(projectRoot, "Asm-mvid2", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                Assert.That(ScanSibling(projectRoot, "Asm-mvid1", siblingRelative), Is.Empty);

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid2", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling put back to its snapshot bytes while its length and write time are
        /// kept keeps the earlier "differs" verdict. This is the same known limit in the other
        /// direction: it stays reported until the next compile.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_SecondScanWithSameStamp_KeepsTheDiffersVerdict()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-BBBB", FirstWriteTimeUtc);
                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));

                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling whose length and write time equal the stamp the capture recorded is
        /// taken to match its snapshot without comparing bytes, even though the bytes differ.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenTheManifestStampEqualsTheSource_TrustsItWithoutComparingBytes()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", StampLine(siblingRelative, 12, FirstWriteTimeUtc));

                Assert.That(ScanSibling(projectRoot, "Asm-mvid", siblingRelative), Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a recorded stamp with another length is not trusted, so the bytes are compared
        /// and a differing sibling is reported.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenTheManifestLengthDiffers_ComparesBytes()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", StampLine(siblingRelative, 13, FirstWriteTimeUtc));

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a recorded stamp with another write time is not trusted, so the bytes are compared
        /// and a differing sibling is reported.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenTheManifestWriteTimeDiffers_ComparesBytes()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", StampLine(siblingRelative, 12, SecondWriteTimeUtc));

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling whose stamp differs from the recorded one but whose bytes still equal the
        /// snapshot is left out, because the byte comparison decides.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenTheManifestStampDiffersButTheBytesAreEqual_OmitsTheSibling()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", StampLine(siblingRelative, 12, SecondWriteTimeUtc));

                Assert.That(ScanSibling(projectRoot, "Asm-mvid", siblingRelative), Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a manifest with one line that cannot be parsed is ignored as a whole, so even a
        /// matching stamp on another line is not trusted and the differing sibling is reported.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_WhenAManifestLineIsMalformed_IgnoresTheWholeManifest()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(
                    projectRoot,
                    "Asm-mvid",
                    StampLine(siblingRelative, 12, FirstWriteTimeUtc),
                    "broken\t12");

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: the re-apply check after a skip also takes a source whose stamp equals the recorded
        /// one to match its snapshot without comparing bytes.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshotDirectory_WhenTheManifestStampEqualsTheSource_IsTrueWithoutComparingBytes()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", StampLine(siblingRelative, 12, FirstWriteTimeUtc));

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshotDirectory(
                    projectRoot,
                    "Asm-mvid",
                    siblingRelative,
                    AbsoluteProjectPath(projectRoot, siblingRelative));

                Assert.That(matches, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: the default file selection leaves out a source whose stamp equals the recorded one,
        /// without comparing bytes.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_WhenTheManifestStampEqualsTheSource_LeavesTheSourceOut()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-BBBB");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", StampLine(siblingRelative, 12, FirstWriteTimeUtc));

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { siblingRelative });

                Assert.That(result.ChangedProjectRelativePaths, Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a sibling the capture marked as edited after the compile is reported as changed,
        /// even though its bytes and stamp equal the snapshot.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectory_ASiblingMarkedEditedAfterCompile_ReportsItThoughItsBytesMatch()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", MarkedStampLine(siblingRelative, 12, FirstWriteTimeUtc));

                Assert.That(
                    ScanSibling(projectRoot, "Asm-mvid", siblingRelative),
                    Is.EqualTo(new[] { AbsoluteProjectPath(projectRoot, siblingRelative) }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: the re-apply check after a skip does not take a file marked as edited after the
        /// compile to be back at its compiled source, even though its bytes and stamp equal the snapshot.
        /// </summary>
        [Test]
        public void SourceMatchesSnapshotDirectory_AFileMarkedEditedAfterCompile_IsFalseThoughItsBytesMatch()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", MarkedStampLine(siblingRelative, 12, FirstWriteTimeUtc));

                bool matches = HotReloadChangedSiblingSourceDetector.SourceMatchesSnapshotDirectory(
                    projectRoot,
                    "Asm-mvid",
                    siblingRelative,
                    AbsoluteProjectPath(projectRoot, siblingRelative));

                Assert.That(matches, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: the default file selection picks a file marked as edited after the compile, even
        /// though its bytes and stamp equal the snapshot.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_AFileMarkedEditedAfterCompile_SelectsItThoughItsBytesMatch()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string siblingRelative = "Assets/Sibling.cs";
                WriteSnapshot(projectRoot, "Asm-mvid", siblingRelative, "sibling-AAAA");
                WriteProjectFileAt(projectRoot, siblingRelative, "sibling-AAAA", FirstWriteTimeUtc);
                WriteStampManifest(projectRoot, "Asm-mvid", MarkedStampLine(siblingRelative, 12, FirstWriteTimeUtc));

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Asm-mvid",
                        new[] { siblingRelative });

                Assert.That(result.ChangedProjectRelativePaths, Is.EqualTo(new[] { siblingRelative }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: sibling-derived warnings are ordinal-deduped among themselves and skipped
        /// when the own-file list already contains the exact string, without collapsing
        /// duplicates that were already in the own-file list.
        /// </summary>
        [Test]
        public void AppendSiblingDerivedWarnings_DedupesAmongSiblingsAndSkipsOwnFileMatches()
        {
            System.Collections.Generic.List<string> ownFileWarnings =
                new System.Collections.Generic.List<string> { "own-a", "own-a", "shared" };

            HotReloadOutcomeAggregation.AppendSiblingDerivedWarnings(
                ownFileWarnings,
                new[] { "shared", "sibling-b", "sibling-b", "own-a" });

            Assert.That(
                ownFileWarnings,
                Is.EqualTo(new[] { "own-a", "own-a", "shared", "sibling-b" }));
        }

        // Why an edited file: a run always has one, and the detector requires it.
        private const string EditedRelative = "Assets/Edited.cs";

        private static string[] ScanSibling(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string siblingRelative)
        {
            return HotReloadChangedSiblingSourceDetector.DetectFromSnapshotDirectory(
                projectRoot,
                assemblySnapshotDirectoryName,
                new[] { EditedRelative, siblingRelative },
                new[] { EditedRelative }).ChangedSiblingAbsolutePaths;
        }

        // Why a write time the test chooses: SetLastWriteTimeUtc stores microseconds while
        // LastWriteTimeUtc reports 100 ns ticks, so only a chosen value can be put back exactly.
        private static void WriteProjectFileAt(
            string projectRoot,
            string projectRelativePath,
            string contents,
            DateTime lastWriteTimeUtc)
        {
            WriteProjectFile(projectRoot, projectRelativePath, contents);
            string absolutePath = AbsoluteProjectPath(projectRoot, projectRelativePath);
            File.SetLastWriteTimeUtc(absolutePath, lastWriteTimeUtc);
            Assert.That(
                new FileInfo(absolutePath).LastWriteTimeUtc,
                Is.EqualTo(lastWriteTimeUtc),
                "The write time must be settable exactly, or the stamp the detector reads is not the one the test chose.");
        }

        private static string CreateTempProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-sibling-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            return projectRoot;
        }

        private static void WriteProjectFile(string projectRoot, string projectRelativePath, string contents)
        {
            string absolutePath = AbsoluteProjectPath(projectRoot, projectRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void WriteSnapshot(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string projectRelativePath,
            string contents)
        {
            string snapshotDirectory = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblySnapshotDirectoryName);
            Directory.CreateDirectory(snapshotDirectory);
            string snapshotPath = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath(projectRelativePath.Replace('\\', '/')) + ".cs");
            File.WriteAllBytes(
                snapshotPath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents));
        }

        // Plants a stamp manifest as the capture would write it: the header line, then one
        // "<snapshot file name>\t<length>\t<ticks>\t<edited after compile>" line per entry, "\n" after every line.
        private static void WriteStampManifest(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            params string[] lines)
        {
            string snapshotDirectory = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblySnapshotDirectoryName);
            Directory.CreateDirectory(snapshotDirectory);
            StringBuilder text = new StringBuilder();
            text.Append(HotReloadConstants.SourceStampManifestHeader).Append('\n');
            foreach (string line in lines)
            {
                text.Append(line).Append('\n');
            }

            File.WriteAllText(
                Path.Combine(snapshotDirectory, HotReloadConstants.SourceStampManifestFileName),
                text.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string StampLine(string projectRelativePath, long length, DateTime lastWriteTimeUtc)
        {
            return HotReloadSourceSnapshotter.HashProjectRelativePath(projectRelativePath.Replace('\\', '/'))
                + ".cs\t" + length.ToString(CultureInfo.InvariantCulture)
                + "\t" + lastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                + "\t0";
        }

        // A stamp line the capture marked as edited after the compile.
        private static string MarkedStampLine(string projectRelativePath, long length, DateTime lastWriteTimeUtc)
        {
            string unmarked = StampLine(projectRelativePath, length, lastWriteTimeUtc);
            return unmarked.Substring(0, unmarked.Length - 1) + "1";
        }

        private static string AbsoluteProjectPath(string projectRoot, string projectRelativePath)
        {
            return Path.GetFullPath(
                Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
