using System;
using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Writes migrated files atomically through temporary sidecar files.
    /// </summary>
    internal static class ThirdPartyToolMigrationFileWriter
    {
        internal static void Write(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            string tempFilePath = CreateUniqueSidecarPath(filePath, ".tmp");
            File.WriteAllText(tempFilePath, content);
            if (!File.Exists(filePath))
            {
                File.Move(tempFilePath, filePath);
                return;
            }

            string backupFilePath = CreateUniqueSidecarPath(filePath, ".bak");
            File.Replace(tempFilePath, filePath, backupFilePath);
            File.Delete(backupFilePath);
        }

        internal static string CreateUniqueSidecarPath(string filePath, string extension)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileName = Path.GetFileName(filePath);
            string sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            while (File.Exists(sidecarPath))
            {
                sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            }

            return sidecarPath;
        }
    }
}
