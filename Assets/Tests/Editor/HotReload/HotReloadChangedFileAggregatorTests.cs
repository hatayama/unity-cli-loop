using System;
using System.Globalization;
using System.IO;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for aggregating changed sources from compile-snapshot assemblies.
    /// </summary>
    public class HotReloadChangedFileAggregatorTests
    {
        /// <summary>
        /// What: changed paths from overlapping assemblies are unioned, deduplicated, and ordinally sorted.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectories_WhenAssembliesOverlap_UnionsDedupesAndSortsPaths()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sharedPath = "Assets/Shared.cs";
                string firstPath = "Assets/Zeta.cs";
                string secondPath = "Assets/Alpha.cs";
                WriteChangedSource(projectRoot, "Assembly-A-mvid", sharedPath);
                WriteChangedSource(projectRoot, "Assembly-A-mvid", firstPath);
                WriteChangedSource(projectRoot, "Assembly-B-mvid", sharedPath);
                WriteChangedSource(projectRoot, "Assembly-B-mvid", secondPath);

                HotReloadChangedFileAggregationResult result =
                    HotReloadChangedFileAggregator.DetectFromSnapshotDirectories(
                        projectRoot,
                        new[]
                        {
                            new HotReloadSnapshotAssembly(
                                "Assembly-A-mvid",
                                new[] { firstPath, sharedPath }),
                            new HotReloadSnapshotAssembly(
                                "Assembly-B-mvid",
                                new[] { sharedPath, secondPath })
                        });

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(
                    result.ChangedProjectRelativePaths,
                    Is.EqualTo(new[] { secondPath, sharedPath, firstPath }));
                Assert.That(result.ScanLimitWarnings, Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: an assembly scan cap warning is retained by the aggregate result.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectories_WhenAssemblyScanIsLimited_CollectsTheWarning()
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
                    WriteChangedSource(projectRoot, "Assembly-A-mvid", sourcePath);
                }

                HotReloadChangedFileAggregationResult result =
                    HotReloadChangedFileAggregator.DetectFromSnapshotDirectories(
                        projectRoot,
                        new[] { new HotReloadSnapshotAssembly("Assembly-A-mvid", sourcePaths) });

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(result.ChangedProjectRelativePaths, Has.Count.EqualTo(50));
                Assert.That(
                    result.ScanLimitWarnings,
                    Is.EqualTo(
                        new[]
                        {
                            "sibling const-drift scan limited to first 50 changed files (51 total)"
                        }));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: a baseline in any one assembly keeps the aggregate result distinguishable from no baseline.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectories_WhenOnlyOneAssemblyHasBaseline_ReportsBaselineAvailable()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string baselinePath = "Assets/Baseline.cs";
                string missingBaselinePath = "Assets/MissingBaseline.cs";
                WriteProjectFile(projectRoot, baselinePath, "same");
                WriteSnapshot(projectRoot, "Assembly-WithBaseline-mvid", baselinePath, "same");
                WriteProjectFile(projectRoot, missingBaselinePath, "changed");

                HotReloadChangedFileAggregationResult result =
                    HotReloadChangedFileAggregator.DetectFromSnapshotDirectories(
                        projectRoot,
                        new[]
                        {
                            new HotReloadSnapshotAssembly(
                                "Assembly-WithBaseline-mvid",
                                new[] { baselinePath }),
                            new HotReloadSnapshotAssembly(
                                "Assembly-WithoutBaseline-mvid",
                                new[] { missingBaselinePath })
                        });

                Assert.That(result.HasBaseline, Is.True);
                Assert.That(result.ChangedProjectRelativePaths, Is.Empty);
                Assert.That(result.ScanLimitWarnings, Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        /// <summary>
        /// What: no assembly snapshot directories reports that no compile baseline exists.
        /// </summary>
        [Test]
        public void DetectFromSnapshotDirectories_WhenNoAssemblyHasBaseline_ReportsNoBaseline()
        {
            string projectRoot = CreateTempProjectRoot();
            try
            {
                string sourcePath = "Assets/Source.cs";
                WriteProjectFile(projectRoot, sourcePath, "changed");

                HotReloadChangedFileAggregationResult result =
                    HotReloadChangedFileAggregator.DetectFromSnapshotDirectories(
                        projectRoot,
                        new[] { new HotReloadSnapshotAssembly("Assembly-mvid", new[] { sourcePath }) });

                Assert.That(result.HasBaseline, Is.False);
                Assert.That(result.ChangedProjectRelativePaths, Is.Empty);
                Assert.That(result.ScanLimitWarnings, Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        private static string CreateTempProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "hot-reload-file-aggregate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            return projectRoot;
        }

        private static void WriteChangedSource(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string projectRelativePath)
        {
            WriteProjectFile(projectRoot, projectRelativePath, "disk");
            WriteSnapshot(projectRoot, assemblySnapshotDirectoryName, projectRelativePath, "snapshot");
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
