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
    /// EditMode coverage for snapshot-vs-disk sibling change detection used by const-drift scanning.
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
                        editedRelative);

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
                        editedRelative);

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
                    "Assets/Edited.cs");

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
                        editedRelative);

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
                        editedRelative);

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
        /// What: DeduplicatePreserveOrder keeps the first copy of a repeated warning string.
        /// </summary>
        [Test]
        public void DeduplicatePreserveOrder_WhenWarningsRepeat_KeepsFirstOccurrence()
        {
            System.Collections.Generic.List<string> unique =
                HotReloadOutcomeAggregation.DeduplicatePreserveOrder(
                    new[] { "a", "b", "a", "c", "b" });

            Assert.That(unique, Is.EqualTo(new[] { "a", "b", "c" }));
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
