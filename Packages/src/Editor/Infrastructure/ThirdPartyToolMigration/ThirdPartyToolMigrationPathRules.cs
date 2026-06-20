using System;
using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Normalizes project file paths before migration assembly ancestry checks.
    /// </summary>
    internal static class ThirdPartyToolMigrationPathRules
    {
        internal static string[] GetRelativePathSegments(string filePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedRoot = NormalizeFullPath(projectRoot);
            string normalizedFilePath = Path.GetFullPath(filePath);
            string relativePath = Path.GetRelativePath(normalizedRoot, normalizedFilePath);
            char[] separators =
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            };
            return relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        }

        internal static bool IsSameOrChildPath(string childPath, string parentPath)
        {
            ThrowIfPathArgumentEmpty(childPath, nameof(childPath));
            ThrowIfPathArgumentEmpty(parentPath, nameof(parentPath));

            string normalizedChildPath = NormalizeFullPath(childPath);
            string normalizedParentPath = NormalizeFullPath(parentPath);
            StringComparison pathComparison = GetPathComparison();

            if (string.Equals(normalizedChildPath, normalizedParentPath, pathComparison))
            {
                return true;
            }

            string parentWithSeparator = normalizedParentPath
                + Path.DirectorySeparatorChar;
            return normalizedChildPath.StartsWith(parentWithSeparator, pathComparison);
        }

        private static string NormalizeFullPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be null or empty");

            string fullPath = Path.GetFullPath(path);
            string trimmedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmedPath.Length == 0 ? fullPath : trimmedPath;
        }

        private static void ThrowIfPathArgumentEmpty(string path, string argumentName)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), $"{argumentName} must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(argumentName), "argumentName must not be null or empty");

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException($"{argumentName} must not be null or empty", argumentName);
            }
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
