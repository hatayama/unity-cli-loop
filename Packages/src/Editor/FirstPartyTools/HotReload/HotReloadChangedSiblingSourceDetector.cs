using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds compilation-assembly siblings whose on-disk bytes differ from the last compile snapshot.
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

            string snapshotDirectory = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblySnapshotDirectoryName);
            if (!Directory.Exists(snapshotDirectory))
            {
                return HotReloadChangedSiblingScanResult.Empty;
            }

            List<string> changedSiblingAbsolutePaths = new List<string>();
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string changedPath = TryResolveChangedSiblingAbsolutePath(
                    projectRoot,
                    snapshotDirectory,
                    sourceFiles[index],
                    editedProjectRelativePath);
                if (changedPath != null)
                {
                    changedSiblingAbsolutePaths.Add(changedPath);
                }
            }

            return LimitChangedSiblings(changedSiblingAbsolutePaths);
        }

        private static string TryResolveChangedSiblingAbsolutePath(
            string projectRoot,
            string snapshotDirectory,
            string projectRelativeSourcePath,
            string editedProjectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativeSourcePath))
            {
                return null;
            }

            string normalizedRelativePath = projectRelativeSourcePath.Replace('\\', '/');
            if (IsSameProjectRelativePath(normalizedRelativePath, editedProjectRelativePath))
            {
                return null;
            }

            string absoluteSourcePath = Path.GetFullPath(
                Path.Combine(projectRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
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

            return absoluteSourcePath;
        }

        private static HotReloadChangedSiblingScanResult LimitChangedSiblings(List<string> changedSiblingAbsolutePaths)
        {
            int totalChanged = changedSiblingAbsolutePaths.Count;
            if (totalChanged <= HotReloadConstants.SiblingConstDriftScanFileLimit)
            {
                return new HotReloadChangedSiblingScanResult(
                    changedSiblingAbsolutePaths.ToArray(),
                    string.Empty);
            }

            string[] limited = new string[HotReloadConstants.SiblingConstDriftScanFileLimit];
            changedSiblingAbsolutePaths.CopyTo(0, limited, 0, HotReloadConstants.SiblingConstDriftScanFileLimit);
            string warning = string.Format(
                HotReloadConstants.SiblingConstDriftScanLimitedWarningFormat,
                totalChanged);
            return new HotReloadChangedSiblingScanResult(limited, warning);
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
