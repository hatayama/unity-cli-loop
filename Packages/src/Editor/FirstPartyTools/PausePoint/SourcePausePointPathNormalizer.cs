using System;
using System.Diagnostics;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compares a PDB sequence point's document URL against a project-relative script path,
    /// tolerating separator and absolute-vs-relative differences across platforms.
    /// </summary>
    internal static class SourcePausePointPathNormalizer
    {
        public static string ToForwardSlashes(string path)
        {
            Debug.Assert(path != null, "path must not be null.");
            return path.Replace('\\', '/');
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

            if (normalizedDocumentUrl.EndsWith("/" + normalizedRelativePath, comparison))
            {
                return true;
            }

            // Why reverse suffix: either argument may be the absolute side. Shim compiles route
            // through the shared worker (relative literal documents) or the one-shot csc
            // (project-rooted absolute documents), while the pause-point request may arrive
            // relative or absolute.
            return normalizedRelativePath.EndsWith("/" + normalizedDocumentUrl, comparison);
        }
    }
}
