// Intentionally duplicated from
// Packages/src/Editor/Infrastructure/ThirdPartyToolMigration/ThirdPartyToolMigrationFileWriter.cs.
// The source method is private in another assembly; duplicating these few lines is less coupling
// than adding an asmdef reference and InternalsVisibleTo. A follow-up PR can centralize the helper.
using System;
using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Routes hot-reload file IO through Windows extended-length paths when needed.
    /// </summary>
    internal static class HotReloadFileSystemPath
    {
        private const int WindowsLegacyMaxPathLength = 260;
        private const string WindowsExtendedLengthPathPrefix = @"\\?\";
        private const string WindowsExtendedLengthUncPathPrefix = @"\\?\UNC\";
        private const string WindowsUncPathPrefix = @"\\";

        internal static string GetFileSystemPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be null or empty");

            string fullPath = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar != '\\' ||
                fullPath.Length < WindowsLegacyMaxPathLength ||
                fullPath.StartsWith(WindowsExtendedLengthPathPrefix, StringComparison.Ordinal))
            {
                return fullPath;
            }

            // Windows Mono still needs the extended-length prefix for file IO beyond legacy MAX_PATH.
            if (fullPath.StartsWith(WindowsUncPathPrefix, StringComparison.Ordinal))
            {
                return WindowsExtendedLengthUncPathPrefix +
                    fullPath.Substring(WindowsUncPathPrefix.Length);
            }

            return WindowsExtendedLengthPathPrefix + fullPath;
        }
    }
}
