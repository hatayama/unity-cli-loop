using System;
using System.Collections.Generic;
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
            ThirdPartyToolMigrationFileAccess.WriteAllText(tempFilePath, content);
            if (!ThirdPartyToolMigrationFileAccess.Exists(filePath))
            {
                ThirdPartyToolMigrationFileAccess.Move(tempFilePath, filePath);
                return;
            }

            string backupFilePath = CreateUniqueSidecarPath(filePath, ".bak");
            ThirdPartyToolMigrationFileAccess.Replace(tempFilePath, filePath, backupFilePath);
            ThirdPartyToolMigrationFileAccess.Delete(backupFilePath);
        }

        internal static string CreateUniqueSidecarPath(string filePath, string extension)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileName = Path.GetFileName(filePath);
            string sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            while (ThirdPartyToolMigrationFileAccess.Exists(sidecarPath))
            {
                sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            }

            return sidecarPath;
        }
    }

    /// <summary>
    /// Routes migration file IO through Windows extended-length paths when needed.
    /// </summary>
    internal static class ThirdPartyToolMigrationFileAccess
    {
        internal static string ReadAllText(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.ReadAllText(GetFileSystemPath(filePath));
        }

        internal static void WriteAllText(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            File.WriteAllText(GetFileSystemPath(filePath), content);
        }

        internal static IEnumerable<string> ReadLines(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.ReadLines(GetFileSystemPath(filePath));
        }

        internal static bool Exists(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.Exists(GetFileSystemPath(filePath));
        }

        internal static void Move(string sourceFilePath, string destinationFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceFilePath), "sourceFilePath must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(destinationFilePath),
                "destinationFilePath must not be null or empty");

            File.Move(GetFileSystemPath(sourceFilePath), GetFileSystemPath(destinationFilePath));
        }

        internal static void Replace(
            string sourceFilePath,
            string destinationFilePath,
            string destinationBackupFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceFilePath), "sourceFilePath must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(destinationFilePath),
                "destinationFilePath must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(destinationBackupFilePath),
                "destinationBackupFilePath must not be null or empty");

            File.Replace(
                GetFileSystemPath(sourceFilePath),
                GetFileSystemPath(destinationFilePath),
                GetFileSystemPath(destinationBackupFilePath));
        }

        internal static void Delete(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            File.Delete(GetFileSystemPath(filePath));
        }

        private static string GetFileSystemPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be null or empty");

            string fullPath = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar != '\\' ||
                fullPath.Length < ThirdPartyToolMigrationFileServiceConstants.WindowsLegacyMaxPathLength ||
                fullPath.StartsWith(
                    ThirdPartyToolMigrationFileServiceConstants.WindowsExtendedLengthPathPrefix,
                    StringComparison.Ordinal))
            {
                return fullPath;
            }

            // Windows Mono still needs the extended-length prefix for file IO beyond legacy MAX_PATH.
            if (fullPath.StartsWith(
                    ThirdPartyToolMigrationFileServiceConstants.WindowsUncPathPrefix,
                    StringComparison.Ordinal))
            {
                return ThirdPartyToolMigrationFileServiceConstants.WindowsExtendedLengthUncPathPrefix +
                    fullPath.Substring(
                        ThirdPartyToolMigrationFileServiceConstants.WindowsUncPathPrefix.Length);
            }

            return ThirdPartyToolMigrationFileServiceConstants.WindowsExtendedLengthPathPrefix + fullPath;
        }
    }
}
