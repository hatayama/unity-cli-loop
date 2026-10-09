using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using UnityEditor.Compilation;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compares compilation-assembly sources with the last compile snapshot: finds the sources whose
    /// on-disk bytes differ from it, and tells whether one source still holds its bytes.
    /// </summary>
    internal static class HotReloadChangedSiblingSourceDetector
    {
        // Why per domain: a verdict compares a file with a snapshot that never changes for its MVID, so it can only
        // go stale when the file itself changes, and a save changes the file's length or write time.
        private static readonly HotReloadSiblingVerdictCache _verdicts = new HotReloadSiblingVerdictCache();

        static HotReloadChangedSiblingSourceDetector()
        {
            // Why: a compile that fails keeps the domain alive, so without this the memo would keep
            // verdicts for an assembly generation that is no longer current.
            CompilationPipeline.compilationStarted += _ => _verdicts.Clear();
        }

        /// <summary>
        /// Returns changed sibling absolute paths for <paramref name="sourceFiles"/>, excluding
        /// <paramref name="editedProjectRelativePaths"/>. Missing snapshots or DLLs yield an empty
        /// result with no extra warning — the existing missing-baseline path already covers that.
        /// </summary>
        /// <remarks>
        /// Why a collection of edited paths: one run transforms every edited file of an assembly
        /// together, and a file edited in the same run is not a drifted sibling of its own group.
        /// </remarks>
        internal static HotReloadChangedSiblingScanResult Detect(
            string projectRoot,
            string assemblyName,
            string targetDllPath,
            string[] sourceFiles,
            IReadOnlyCollection<string> editedProjectRelativePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(
                editedProjectRelativePaths != null && editedProjectRelativePaths.Count > 0,
                "editedProjectRelativePaths must not be empty.");

            if (string.IsNullOrEmpty(targetDllPath) || !File.Exists(targetDllPath))
            {
                return HotReloadChangedSiblingScanResult.Empty;
            }

            string pdbPath = Path.ChangeExtension(targetDllPath, ".pdb");
            if (!File.Exists(pdbPath))
            {
                return HotReloadChangedSiblingScanResult.Empty;
            }

            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath);
            return DetectFromSnapshotDirectory(
                projectRoot,
                assemblyName + "-" + mvid,
                sourceFiles,
                editedProjectRelativePaths);
        }

        // Why a directory-name entry: EditMode tests plant a snapshot tree without a real DLL.
        internal static HotReloadChangedSiblingScanResult DetectFromSnapshotDirectory(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string[] sourceFiles,
            IReadOnlyCollection<string> editedProjectRelativePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(assemblySnapshotDirectoryName),
                "assemblySnapshotDirectoryName must not be null or empty.");
            Debug.Assert(
                editedProjectRelativePaths != null && editedProjectRelativePaths.Count > 0,
                "editedProjectRelativePaths must not be empty.");

            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return HotReloadChangedSiblingScanResult.Empty;
            }

            HotReloadChangedSourceScanResult sourceScan = DetectChangedFromSnapshotDirectory(
                projectRoot,
                assemblySnapshotDirectoryName,
                sourceFiles,
                editedProjectRelativePaths);
            List<string> changedSiblingAbsolutePaths = new List<string>(
                sourceScan.ChangedProjectRelativePaths.Count);
            for (int index = 0; index < sourceScan.ChangedProjectRelativePaths.Count; index++)
            {
                changedSiblingAbsolutePaths.Add(
                    ToAbsoluteProjectPath(projectRoot, sourceScan.ChangedProjectRelativePaths[index]));
            }

            return new HotReloadChangedSiblingScanResult(
                changedSiblingAbsolutePaths.ToArray(),
                sourceScan.ScanLimitWarning,
                sourceScan.HasBaseline && string.IsNullOrEmpty(sourceScan.ScanLimitWarning));
        }

        /// <summary>
        /// Returns every changed project-relative source path for one snapshot directory.
        /// </summary>
        internal static HotReloadChangedSourceScanResult DetectAllChangedFromSnapshotDirectory(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string[] sourceFiles)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(assemblySnapshotDirectoryName),
                "assemblySnapshotDirectoryName must not be null or empty.");

            return DetectChangedFromSnapshotDirectory(
                projectRoot,
                assemblySnapshotDirectoryName,
                sourceFiles,
                excludedProjectRelativePaths: null);
        }

        /// <summary>
        /// Whether <paramref name="sourcePath"/> holds the bytes the last compile snapshot keeps for
        /// <paramref name="projectRelativePath"/>. A missing DLL, PDB, snapshot or source answers
        /// false, so a file is never taken to be back at its compiled source without its snapshot.
        /// </summary>
        internal static bool SourceMatchesSnapshot(
            string projectRoot,
            string assemblyName,
            string targetDllPath,
            string projectRelativePath,
            string sourcePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");

            // Why the same guards as Detect: without the DLL and its PDB there is no snapshot
            // directory to name.
            if (string.IsNullOrEmpty(targetDllPath) || !File.Exists(targetDllPath))
            {
                return false;
            }

            if (!File.Exists(Path.ChangeExtension(targetDllPath, ".pdb")))
            {
                return false;
            }

            return SourceMatchesSnapshotDirectory(
                projectRoot,
                assemblyName + "-" + HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath),
                projectRelativePath,
                sourcePath);
        }

        // Why a directory-name entry: EditMode tests plant a snapshot tree without a real DLL.
        internal static bool SourceMatchesSnapshotDirectory(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string projectRelativePath,
            string sourcePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(assemblySnapshotDirectoryName),
                "assemblySnapshotDirectoryName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be null or empty.");

            if (string.IsNullOrEmpty(sourcePath))
            {
                return false;
            }

            FileInfo source = new FileInfo(sourcePath);
            if (!source.Exists)
            {
                return false;
            }

            string snapshotDirectory = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblySnapshotDirectoryName);
            string snapshotPath = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath(projectRelativePath.Replace('\\', '/')) + ".cs");
            if (!File.Exists(snapshotPath))
            {
                return false;
            }

            return MatchesSnapshot(
                source,
                snapshotPath,
                new Lazy<HotReloadSourceStampManifest>(() => HotReloadSourceStampManifest.Load(snapshotDirectory)));
        }

        private static HotReloadChangedSourceScanResult DetectChangedFromSnapshotDirectory(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string[] sourceFiles,
            IReadOnlyCollection<string> excludedProjectRelativePaths)
        {
            string snapshotDirectory = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblySnapshotDirectoryName);
            if (!Directory.Exists(snapshotDirectory))
            {
                return new HotReloadChangedSourceScanResult(false, new List<string>(), string.Empty);
            }

            // Why lazily: a run whose verdicts all come from the per-domain memo never opens the
            // manifest, and a run that needs it reads it once for every source.
            Lazy<HotReloadSourceStampManifest> manifest = new Lazy<HotReloadSourceStampManifest>(
                () => HotReloadSourceStampManifest.Load(snapshotDirectory));
            List<string> changedProjectRelativePaths = new List<string>();
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return new HotReloadChangedSourceScanResult(true, changedProjectRelativePaths, string.Empty);
            }

            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string changedPath = TryResolveChangedProjectRelativePath(
                    projectRoot,
                    snapshotDirectory,
                    sourceFiles[index],
                    excludedProjectRelativePaths,
                    manifest);
                if (changedPath != null)
                {
                    changedProjectRelativePaths.Add(changedPath);
                }
            }

            return LimitChangedSources(changedProjectRelativePaths);
        }

        private static string TryResolveChangedProjectRelativePath(
            string projectRoot,
            string snapshotDirectory,
            string projectRelativeSourcePath,
            IReadOnlyCollection<string> excludedProjectRelativePaths,
            Lazy<HotReloadSourceStampManifest> manifest)
        {
            if (string.IsNullOrEmpty(projectRelativeSourcePath))
            {
                return null;
            }

            string normalizedRelativePath = projectRelativeSourcePath.Replace('\\', '/');
            if (IsExcludedProjectRelativePath(normalizedRelativePath, excludedProjectRelativePaths))
            {
                return null;
            }

            string absoluteSourcePath = ToAbsoluteProjectPath(projectRoot, normalizedRelativePath);
            FileInfo source = new FileInfo(absoluteSourcePath);
            if (!source.Exists)
            {
                return null;
            }

            string snapshotPath = Path.Combine(
                snapshotDirectory,
                HotReloadSourceSnapshotter.HashProjectRelativePath(normalizedRelativePath) + ".cs");
            if (!File.Exists(snapshotPath))
            {
                return null;
            }

            if (MatchesSnapshot(source, snapshotPath, manifest))
            {
                return null;
            }

            // Why project-relative: the default --files path must work across machines and is the CLI contract.
            return normalizedRelativePath;
        }

        // Compares an existing source with an existing snapshot file: reuses the last verdict while
        // the source keeps its length and write time, then trusts the stamp the capture recorded
        // for the same length and write time, and only then reads both files. A copy the capture
        // marked as edited after the compile never matches.
        private static bool MatchesSnapshot(
            FileInfo source,
            string snapshotPath,
            Lazy<HotReloadSourceStampManifest> manifest)
        {
            long lastWriteTimeUtcTicks = source.LastWriteTimeUtc.Ticks;
            if (_verdicts.TryGetVerdict(
                    snapshotPath,
                    source.FullName,
                    source.Length,
                    lastWriteTimeUtcTicks,
                    out bool matches))
            {
                return matches;
            }

            // Why the mark before the stamp and the bytes: a marked copy usually holds the save made after
            // the compile, so it equals the current file by both and would answer "unchanged". The
            // verdict cache may keep this false: the mark does not change within one MVID's snapshot.
            bool equal = !manifest.Value.IsEditedAfterCompile(Path.GetFileName(snapshotPath))
                && (HasRecordedStamp(manifest.Value, snapshotPath, source.Length, lastWriteTimeUtcTicks)
                    || BytesEqual(File.ReadAllBytes(source.FullName), File.ReadAllBytes(snapshotPath)));
            // Why the stamp taken before reading: a write during the read moves the stamp on, so the
            // next scan reads the file again instead of trusting this verdict.
            _verdicts.Record(snapshotPath, source.FullName, source.Length, lastWriteTimeUtcTicks, equal);
            return equal;
        }

        // Why a stamp stands for the bytes: the capture recorded it from the same stat the copy was
        // taken under, so a source that still shows it holds the copied bytes (the same proof the
        // per-domain memo relies on), and the two reads can be skipped.
        private static bool HasRecordedStamp(
            HotReloadSourceStampManifest manifest,
            string snapshotPath,
            long length,
            long lastWriteTimeUtcTicks)
        {
            return manifest.TryGetStamp(
                    Path.GetFileName(snapshotPath),
                    out long recordedLength,
                    out long recordedTicks)
                && recordedLength == length
                && recordedTicks == lastWriteTimeUtcTicks;
        }

        private static HotReloadChangedSourceScanResult LimitChangedSources(
            List<string> changedProjectRelativePaths)
        {
            int totalChanged = changedProjectRelativePaths.Count;
            if (totalChanged <= HotReloadConstants.SiblingConstDriftScanFileLimit)
            {
                return new HotReloadChangedSourceScanResult(
                    true,
                    changedProjectRelativePaths,
                    string.Empty);
            }

            List<string> limited = changedProjectRelativePaths.GetRange(
                0,
                HotReloadConstants.SiblingConstDriftScanFileLimit);
            string warning = string.Format(
                CultureInfo.InvariantCulture,
                HotReloadConstants.SiblingConstDriftScanLimitedWarningFormat,
                HotReloadConstants.SiblingConstDriftScanFileLimit,
                totalChanged);
            return new HotReloadChangedSourceScanResult(true, limited, warning);
        }

        private static string ToAbsoluteProjectPath(string projectRoot, string projectRelativePath)
        {
            return Path.GetFullPath(
                Path.Combine(projectRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool IsExcludedProjectRelativePath(
            string normalizedRelativePath,
            IReadOnlyCollection<string> excludedProjectRelativePaths)
        {
            if (excludedProjectRelativePaths == null)
            {
                return false;
            }

            foreach (string excludedProjectRelativePath in excludedProjectRelativePaths)
            {
                if (!string.IsNullOrEmpty(excludedProjectRelativePath)
                    && IsSameProjectRelativePath(normalizedRelativePath, excludedProjectRelativePath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameProjectRelativePath(string left, string right)
        {
            string normalizedLeft = left.Replace('\\', '/');
            string normalizedRight = right.Replace('\\', '/');
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(normalizedLeft, normalizedRight, comparison);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
