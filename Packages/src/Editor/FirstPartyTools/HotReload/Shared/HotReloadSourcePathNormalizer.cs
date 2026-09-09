using System;
using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compares a PDB document URL against a project-relative script path for snapshot adoption.
    /// Same semantics as SourcePausePointPathNormalizer; kept local because PausePoint is a
    /// separate asmdef that HotReload must not reference.
    /// </summary>
    internal static class HotReloadSourcePathNormalizer
    {
        public static string ToForwardSlashes(string path)
        {
            Debug.Assert(path != null, "path must not be null.");
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// The comparer for project-relative script paths on this platform: two spellings that
        /// differ only in case name the same file on Windows and different files elsewhere.
        /// </summary>
        public static StringComparer ProjectRelativePathComparer()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        public static bool PathsReferToSameFile(string documentUrl, string projectRelativePath)
        {
            Debug.Assert(documentUrl != null, "documentUrl must not be null.");
            Debug.Assert(projectRelativePath != null, "projectRelativePath must not be null.");

            string normalizedDocumentUrl = ToForwardSlashes(documentUrl);
            string normalizedRelativePath = ToForwardSlashes(projectRelativePath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(normalizedDocumentUrl, normalizedRelativePath, comparison))
            {
                return true;
            }

            return normalizedDocumentUrl.EndsWith("/" + normalizedRelativePath, comparison);
        }
    }
}
