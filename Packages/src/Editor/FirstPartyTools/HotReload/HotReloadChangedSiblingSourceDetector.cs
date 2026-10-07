using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compares compilation-assembly sources with the last compile snapshot: finds the sources whose
    /// on-disk bytes differ from it, and tells whether one source still holds its bytes.
    /// </summary>
    internal static class HotReloadChangedSiblingSourceDetector
    {
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

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return false;
            }

            string snapshotPath = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblySnapshotDirectoryName,
                HotReloadSourceSnapshotter.HashProjectRelativePath(projectRelativePath.Replace('\\', '/')) + ".cs");
            if (!File.Exists(snapshotPath))
            {
                return false;
            }

            return BytesEqual(File.ReadAllBytes(sourcePath), File.ReadAllBytes(snapshotPath));
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
                    excludedProjectRelativePaths);
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
            IReadOnlyCollection<string> excludedProjectRelativePaths)
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
            if (!File.Exists(absoluteSourcePath))
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

            byte[] diskBytes = File.ReadAllBytes(absoluteSourcePath);
            byte[] snapshotBytes = File.ReadAllBytes(snapshotPath);
            if (BytesEqual(diskBytes, snapshotBytes))
            {
                return null;
            }

            // Why project-relative: the default --files path must work across machines and is the CLI contract.
            return normalizedRelativePath;
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
