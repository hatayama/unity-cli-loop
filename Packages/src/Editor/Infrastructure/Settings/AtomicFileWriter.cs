using System.IO;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Writes files atomically via temp file + rename to prevent
    /// external processes (e.g. CLI) from reading partially-written data.
    /// </summary>
    internal static class AtomicFileWriter
    {
        internal const string CompletedTempFileSuffix = ".tmp";
        internal const string BackupFileSuffix = ".bak";
        internal const string InProgressTempFileSuffix = ".tmp.write";

        /// <summary>
        /// Writes content atomically: .tmp.write → .tmp → .bak → target.
        /// </summary>
        public static void Write(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            string tempFilePath = filePath + CompletedTempFileSuffix;
            string inProgressTempFilePath = filePath + InProgressTempFileSuffix;
            string backupFilePath = filePath + BackupFileSuffix;
            CleanupInProgressTemp(inProgressTempFilePath);
            File.WriteAllText(inProgressTempFilePath, content);
            CleanupCompletedTemp(tempFilePath);
            File.Move(inProgressTempFilePath, tempFilePath);

            // .NET Framework 4.7.1 lacks File.Move(src, dst, overwrite), so we
            // rotate old → .bak before moving .tmp → target to minimize the window
            // where the target file is absent for external readers (CLI).
            if (File.Exists(filePath))
            {
                if (File.Exists(backupFilePath))
                {
                    File.Delete(backupFilePath);
                }
                File.Move(filePath, backupFilePath);
            }
            File.Move(tempFilePath, filePath);
        }

        internal static void RecoverSidecarFiles(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            string tempFilePath = filePath + CompletedTempFileSuffix;
            string backupFilePath = filePath + BackupFileSuffix;

            if (File.Exists(filePath))
            {
                CleanupBackup(backupFilePath);
                CleanupCompletedTemp(tempFilePath);
                return;
            }

            // .tmp is newer than .bak once the previous target has been rotated away,
            // so preferring it avoids rolling back to older session state after a crash.
            if (File.Exists(tempFilePath))
            {
                File.Move(tempFilePath, filePath);
                CleanupBackup(backupFilePath);
                return;
            }

            if (File.Exists(backupFilePath))
            {
                File.Move(backupFilePath, filePath);
            }
        }

        internal static void CleanupInProgressTemp(string inProgressTempFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(inProgressTempFilePath), "inProgressTempFilePath must not be null or empty");

            if (File.Exists(inProgressTempFilePath))
            {
                File.Delete(inProgressTempFilePath);
            }
        }

        public static void CleanupBackup(string backupFilePath)
        {
            if (File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
            }
        }

        internal static void CleanupCompletedTemp(string completedTempFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(completedTempFilePath), "completedTempFilePath must not be null or empty");

            if (File.Exists(completedTempFilePath))
            {
                File.Delete(completedTempFilePath);
            }
        }
    }
}
