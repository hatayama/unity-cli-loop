using System;
using System.Globalization;
using System.IO;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for whole-assembly source change detection from compile snapshots.
    /// </summary>
    public class HotReloadChangedSourceDetectorTests
    {
        // Why fixed write times: the detector reuses a verdict while a file's length and write time
        // stay the same, so each test sets the stamp it means instead of relying on the clock.
        private static readonly DateTime FirstWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime SecondWriteTimeUtc = FirstWriteTimeUtc.AddDays(1);

        /// <summary>
        /// What: a missing snapshot directory reports no baseline instead of conflating it with no changes.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_WhenSnapshotDirectoryMissing_ReportsNoBaseline()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sourcePath = "Assets/Source.cs";
                WriteProjectFile(projectRoot, sourcePath, "disk");

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Assembly-mvid",
                        new[] { sourcePath });

                Assert.That(result.HasBaseline, Is.False);
                Assert.That(result.ChangedProjectRelativePaths, Is.Empty);
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: an existing snapshot directory with byte-identical sources reports a baseline and no changes.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_WhenSourcesMatchSnapshot_ReportsNoChanges()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sourcePath = "Assets/Source.cs";
                WriteProjectFile(projectRoot, sourcePath, "same");
                WriteSnapshot(projectRoot, "Assembly-mvid", sourcePath, "same");

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Assembly-mvid",
                        new[] { sourcePath });

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(result.ChangedProjectRelativePaths, Is.Empty);
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: an assembly snapshot directory without the source's own snapshot keeps the baseline
        /// and fails closed instead of treating the source as changed.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_WhenSourceSnapshotIsMissing_ReportsBaselineWithoutChanges()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sourcePath = "Assets/Source.cs";
                string snapshotDirectory = Path.Combine(
                    HotReloadSourceSnapshotLayout.Root(projectRoot),
                    "Assembly-mvid");
                WriteProjectFile(projectRoot, sourcePath, "disk-changed");
                Directory.CreateDirectory(snapshotDirectory);

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Assembly-mvid",
                        new[] { sourcePath });

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(result.ChangedProjectRelativePaths, Is.Empty);
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: every snapshot-mismatched source is returned without requiring an edited-file exclusion.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_WhenSourcesDiffer_ReturnsProjectRelativePaths()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string changedPath = "Assets/Changed.cs";
                string unchangedPath = "Assets/Unchanged.cs";
                WriteProjectFile(projectRoot, changedPath, "disk-changed");
                WriteProjectFile(projectRoot, unchangedPath, "same");
                WriteSnapshot(projectRoot, "Assembly-mvid", changedPath, "snapshot-changed");
                WriteSnapshot(projectRoot, "Assembly-mvid", unchangedPath, "same");

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Assembly-mvid",
                        new[] { changedPath, unchangedPath });

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(result.ChangedProjectRelativePaths, Is.EqualTo(new[] { changedPath }));
                Assert.That(result.ScanLimitWarning, Is.EqualTo(string.Empty));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: more changed sources than the cap keeps the first 50 and reports the fixed cap warning.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_WhenMoreThanLimitChanged_TruncatesAndWarns()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                const int changedSourceCount = 51;
                string[] sourcePaths = new string[changedSourceCount];
                for (int index = 0; index < changedSourceCount; index++)
                {
                    string sourcePath = "Assets/Source" + index.ToString(CultureInfo.InvariantCulture) + ".cs";
                    sourcePaths[index] = sourcePath;
                    WriteProjectFile(projectRoot, sourcePath, "disk-" + index.ToString(CultureInfo.InvariantCulture));
                    WriteSnapshot(
                        projectRoot,
                        "Assembly-mvid",
                        sourcePath,
                        "snapshot-" + index.ToString(CultureInfo.InvariantCulture));
                }

                HotReloadChangedSourceScanResult result =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        "Assembly-mvid",
                        sourcePaths);

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(result.ChangedProjectRelativePaths, Has.Count.EqualTo(50));
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
        /// What: the default file selection does not pick a source rewritten while it keeps its
        /// length and write time, because the earlier "matches" verdict is reused. This is a known
        /// limit until the next compile.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_SecondScanWithSameStamp_DoesNotSelectTheRewrite()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sourcePath = "Assets/Source.cs";
                WriteSnapshot(projectRoot, "Assembly-mvid", sourcePath, "source-AAAA");
                WriteProjectFileAt(projectRoot, sourcePath, "source-AAAA", FirstWriteTimeUtc);
                Assert.That(ScanAll(projectRoot, sourcePath), Is.Empty);

                WriteProjectFileAt(projectRoot, sourcePath, "source-BBBB", FirstWriteTimeUtc);

                Assert.That(ScanAll(projectRoot, sourcePath), Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: the default file selection picks a source rewritten with a later write time after
        /// an earlier scan found it unchanged.
        /// </summary>
        [Test]
        public void DetectAllChangedFromSnapshotDirectory_SecondScanAfterRewriteWithNewWriteTime_SelectsTheRewrite()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sourcePath = "Assets/Source.cs";
                WriteSnapshot(projectRoot, "Assembly-mvid", sourcePath, "source-AAAA");
                WriteProjectFileAt(projectRoot, sourcePath, "source-AAAA", FirstWriteTimeUtc);
                Assert.That(ScanAll(projectRoot, sourcePath), Is.Empty);

                WriteProjectFileAt(projectRoot, sourcePath, "source-BBBB", SecondWriteTimeUtc);

                Assert.That(ScanAll(projectRoot, sourcePath), Is.EqualTo(new[] { sourcePath }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        private static System.Collections.Generic.List<string> ScanAll(string projectRoot, string sourcePath)
        {
            return HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                projectRoot,
                "Assembly-mvid",
                new[] { sourcePath }).ChangedProjectRelativePaths;
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
                "hot-reload-source-scan-" + Guid.NewGuid().ToString("N"));
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
                HotReloadSourceSnapshotLayout.Root(projectRoot),
                assemblySnapshotDirectoryName);
            Directory.CreateDirectory(snapshotDirectory);
            string snapshotPath = HotReloadSourceSnapshotLayout.SourcePath(snapshotDirectory, projectRelativePath);
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
