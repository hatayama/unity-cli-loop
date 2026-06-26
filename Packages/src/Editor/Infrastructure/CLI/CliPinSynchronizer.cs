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
            SyncProjectPinFile(UnityCliLoopConstants.PackageResolvedPath, Directory.GetCurrentDirectory());
        }

        internal static bool SyncProjectPinFile(string packageRoot, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(packageRoot), "packageRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrWhiteSpace(projectRoot), "projectRoot must not be null or empty");

            string sourcePath = Path.Combine(packageRoot, UnityCliLoopConstants.ULOOP_CLI_PIN_FILE_NAME);
            string destinationPath = Path.Combine(
                projectRoot,
                UnityCliLoopConstants.ULOOP_DIR,
                UnityCliLoopConstants.ULOOP_CLI_PIN_FILE_NAME);

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
            AtomicFileWriter.CleanupBackup(destinationPath + ".bak");
            return true;
        }
    }
}
