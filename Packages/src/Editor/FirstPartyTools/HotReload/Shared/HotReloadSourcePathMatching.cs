// This file is compiled twice: into the Unity editor assembly (host side) and into the
// out-of-process transform worker (see TransformWorkerBootstrap.CollectWorkerSourcePaths).
// It must therefore stay free of Unity, Newtonsoft and Roslyn references.
using System;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Matches an absolute source path against a project-relative one regardless of the
    /// directory separator, so a worker input built on Windows compares the same way.
    /// </summary>
    internal static class HotReloadSourcePathMatching
    {
        // True when absolutePath is projectRelativePath under some root.
        internal static bool EndsWithProjectRelativePath(string absolutePath, string projectRelativePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(projectRelativePath))
            {
                return false;
            }

            string absolute = absolutePath.Replace('\\', '/');
            string relative = projectRelativePath.Replace('\\', '/').TrimStart('/');
            if (absolute.Length <= relative.Length)
            {
                return false;
            }

            // Why the separator check: "Other.cs" must not match "AnOther.cs".
            return absolute.EndsWith("/" + relative, PathComparison());
        }

        // The project-relative form of otherAbsolutePath, given one absolute path whose
        // project-relative form is known; null when the two do not share that root.
        internal static string ToProjectRelativeOrNull(
            string otherAbsolutePath,
            string knownAbsolutePath,
            string knownProjectRelativePath)
        {
            if (!EndsWithProjectRelativePath(knownAbsolutePath, knownProjectRelativePath))
            {
                return null;
            }

            string known = knownAbsolutePath.Replace('\\', '/');
            string relative = knownProjectRelativePath.Replace('\\', '/').TrimStart('/');
            // Ends with '/', because EndsWithProjectRelativePath matched at a separator.
            string root = known.Substring(0, known.Length - relative.Length);
            string other = otherAbsolutePath.Replace('\\', '/');
            if (!other.StartsWith(root, PathComparison()))
            {
                return null;
            }

            return other.Substring(root.Length);
        }

        private static StringComparison PathComparison()
        {
            return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }
    }
}
