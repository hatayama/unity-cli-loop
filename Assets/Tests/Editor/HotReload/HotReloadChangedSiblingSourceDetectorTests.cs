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

        private static string AbsoluteProjectPath(string projectRoot, string projectRelativePath)
        {
            return Path.GetFullPath(
                Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
