using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds compilation-assembly sources whose on-disk bytes differ from the last compile snapshot.
    /// </summary>
    internal static class HotReloadChangedSiblingSourceDetector
    {
        /// <summary>
        /// Returns changed sibling absolute paths for <paramref name="sourceFiles"/>, excluding
        /// <paramref name="editedProjectRelativePath"/>. Missing snapshots or DLLs yield an empty
        /// result with no extra warning — the existing missing-baseline path already covers that.
        /// </summary>
        internal static HotReloadChangedSiblingScanResult Detect(
            string projectRoot,
            string assemblyName,
            string targetDllPath,
            string[] sourceFiles,
            string editedProjectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(editedProjectRelativePath),
                "editedProjectRelativePath must not be null or empty.");

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
                editedProjectRelativePath);
        }

        // Why a directory-name entry: EditMode tests plant a snapshot tree without a real DLL.
        internal static HotReloadChangedSiblingScanResult DetectFromSnapshotDirectory(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string[] sourceFiles,
            string editedProjectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(assemblySnapshotDirectoryName),
                "assemblySnapshotDirectoryName must not be null or empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(editedProjectRelativePath),
                "editedProjectRelativePath must not be null or empty.");

            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return HotReloadChangedSiblingScanResult.Empty;
            }

            HotReloadChangedSourceScanResult sourceScan = DetectChangedFromSnapshotDirectory(
                projectRoot,
                assemblySnapshotDirectoryName,
                sourceFiles,
                editedProjectRelativePath);
            List<string> changedSiblingAbsolutePaths = new List<string>(
                sourceScan.ChangedProjectRelativePaths.Count);
            for (int index = 0; index < sourceScan.ChangedProjectRelativePaths.Count; index++)
            {
                changedSiblingAbsolutePaths.Add(
                    ToAbsoluteProjectPath(projectRoot, sourceScan.ChangedProjectRelativePaths[index]));
            }

            return new HotReloadChangedSiblingScanResult(
                changedSiblingAbsolutePaths.ToArray(),
                sourceScan.ScanLimitWarning);
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
                excludedProjectRelativePath: null);
        }

        private static HotReloadChangedSourceScanResult DetectChangedFromSnapshotDirectory(
            string projectRoot,
            string assemblySnapshotDirectoryName,
            string[] sourceFiles,
            string excludedProjectRelativePath)
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
                    excludedProjectRelativePath);
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
            string excludedProjectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativeSourcePath))
            {
                return null;
            }

            string normalizedRelativePath = projectRelativeSourcePath.Replace('\\', '/');
            if (!string.IsNullOrEmpty(excludedProjectRelativePath)
                && IsSameProjectRelativePath(normalizedRelativePath, excludedProjectRelativePath))
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
