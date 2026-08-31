using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor.Compilation;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Snapshot-directory metadata needed to scan one compilation assembly's source files.
    /// </summary>
    internal sealed class HotReloadSnapshotAssembly
    {
        internal string SnapshotDirectoryName { get; }

        internal string[] SourceFiles { get; }

        internal HotReloadSnapshotAssembly(string snapshotDirectoryName, string[] sourceFiles)
        {
            Debug.Assert(
                !string.IsNullOrEmpty(snapshotDirectoryName),
                "snapshotDirectoryName must not be null or empty.");
            SnapshotDirectoryName = snapshotDirectoryName;
            SourceFiles = sourceFiles ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// All changed sources selected from the available compilation snapshot directories.
    /// </summary>
    internal sealed class HotReloadChangedFileAggregationResult
    {
        internal bool HasBaseline { get; }

        internal List<string> ChangedProjectRelativePaths { get; }

        internal List<string> ScanLimitWarnings { get; }

        internal HotReloadChangedFileAggregationResult(
            bool hasBaseline,
            List<string> changedProjectRelativePaths,
            List<string> scanLimitWarnings)
        {
            HasBaseline = hasBaseline;
            ChangedProjectRelativePaths = changedProjectRelativePaths ?? new List<string>();
            ScanLimitWarnings = scanLimitWarnings ?? new List<string>();
        }
    }

    /// <summary>
    /// Aggregates changed sources from every mutable compilation assembly that has a snapshot candidate.
    /// </summary>
    internal static class HotReloadChangedFileAggregator
    {
        /// <summary>
        /// Detects changed sources using the same compilation-assembly eligibility rules as snapshot capture.
        /// </summary>
        internal static HotReloadChangedFileAggregationResult Detect()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            List<HotReloadSnapshotAssembly> snapshotAssemblies = CollectSnapshotAssemblies(projectRoot);
            return DetectFromSnapshotDirectories(projectRoot, snapshotAssemblies);
        }

        // Why an internal adapter: CompilePipeline assemblies cannot be planted in EditMode fixtures,
        // while snapshot directory names and source files fully determine the pure aggregation behavior.
        internal static HotReloadChangedFileAggregationResult DetectFromSnapshotDirectories(
            string projectRoot,
            IReadOnlyList<HotReloadSnapshotAssembly> snapshotAssemblies)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(snapshotAssemblies != null, "snapshotAssemblies must not be null.");

            bool hasBaseline = false;
            HashSet<string> changedPathSet = new HashSet<string>(GetProjectRelativePathComparer());
            List<string> scanLimitWarnings = new List<string>();
            HashSet<string> warningSet = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < snapshotAssemblies.Count; index++)
            {
                HotReloadSnapshotAssembly assembly = snapshotAssemblies[index];
                HotReloadChangedSourceScanResult scan =
                    HotReloadChangedSiblingSourceDetector.DetectAllChangedFromSnapshotDirectory(
                        projectRoot,
                        assembly.SnapshotDirectoryName,
                        assembly.SourceFiles);
                hasBaseline |= scan.HasBaseline;

                for (int pathIndex = 0; pathIndex < scan.ChangedProjectRelativePaths.Count; pathIndex++)
                {
                    changedPathSet.Add(scan.ChangedProjectRelativePaths[pathIndex]);
                }

                if (!string.IsNullOrEmpty(scan.ScanLimitWarning)
                    && warningSet.Add(scan.ScanLimitWarning))
                {
                    scanLimitWarnings.Add(scan.ScanLimitWarning);
                }
            }

            List<string> changedProjectRelativePaths = new List<string>(changedPathSet);
            changedProjectRelativePaths.Sort(GetProjectRelativePathComparer());
            scanLimitWarnings.Sort(StringComparer.Ordinal);
            return new HotReloadChangedFileAggregationResult(
                hasBaseline,
                changedProjectRelativePaths,
                scanLimitWarnings);
        }

        private static List<HotReloadSnapshotAssembly> CollectSnapshotAssemblies(string projectRoot)
        {
            List<HotReloadSnapshotAssembly> snapshotAssemblies = new List<HotReloadSnapshotAssembly>();
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                string[] sourceFiles = assembly.sourceFiles;
                if (sourceFiles == null || sourceFiles.Length == 0)
                {
                    continue;
                }

                if (HotReloadSourceSnapshotter.ShouldSkipImmutablePackageSources(sourceFiles))
                {
                    continue;
                }

                string dllPath = Path.Combine(
                    projectRoot,
                    HotReloadConstants.ScriptAssembliesRelativeDirectory,
                    assembly.name + HotReloadConstants.CompiledAssemblyExtension);
                string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
                if (!File.Exists(dllPath) || !File.Exists(pdbPath))
                {
                    continue;
                }

                string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath);
                snapshotAssemblies.Add(
                    new HotReloadSnapshotAssembly(assembly.name + "-" + mvid, sourceFiles));
            }

            return snapshotAssemblies;
        }

        private static StringComparer GetProjectRelativePathComparer()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }
    }
}
