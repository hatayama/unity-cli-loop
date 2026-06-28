using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Publishes the package CLI pin contract into the project .uloop directory for dispatcher lookup.
    /// </summary>
    internal static class CliPinSynchronizer
    {
        internal static void SyncCurrentProjectPin()
        {
            SyncProjectPinFile(UnityCliLoopConstants.PackageResolvedPath, ResolveCurrentProjectRoot(UnityEngine.Application.dataPath));
        }

        internal static string ResolveCurrentProjectRoot(string assetsPath)
        {
            if (string.IsNullOrWhiteSpace(assetsPath))
            {
                return string.Empty;
            }

            DirectoryInfo assetsDirectory = Directory.GetParent(assetsPath);
            if (assetsDirectory == null)
            {
                return string.Empty;
            }

            return assetsDirectory.FullName;
        }

        internal static bool SyncProjectPinFile(string packageRoot, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                Debug.LogWarning(
                    $"Unity CLI Loop skipped {UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME} synchronization because the package root is empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                Debug.LogWarning(
                    $"Unity CLI Loop skipped {UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME} synchronization because the project root is empty.");
                return false;
            }

            bool projectRunnerPinChanged = SyncProjectPinFileByName(
                packageRoot,
                projectRoot,
                UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
            bool legacyCliPinChanged = SyncProjectPinFileByName(
                packageRoot,
                projectRoot,
                UnityCliLoopConstants.ULOOP_LEGACY_CLI_PIN_FILE_NAME);
            return projectRunnerPinChanged || legacyCliPinChanged;
        }

        private static bool SyncProjectPinFileByName(string packageRoot, string projectRoot, string pinFileName)
        {
            string sourcePath = Path.Combine(packageRoot, pinFileName);
            string destinationPath = Path.Combine(projectRoot, UnityCliLoopConstants.ULOOP_DIR, pinFileName);

            if (!File.Exists(sourcePath))
            {
                return false;
            }

            string sourceContent = File.ReadAllText(sourcePath);
            if (File.Exists(destinationPath) && string.Equals(File.ReadAllText(destinationPath), sourceContent))
            {
                return false;
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            AtomicFileWriter.Write(destinationPath, sourceContent);
            AtomicFileWriter.CleanupBackup(destinationPath + AtomicFileWriter.BackupFileSuffix);
            return true;
        }
    }
}
