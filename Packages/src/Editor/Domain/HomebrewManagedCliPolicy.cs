using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Classifies whether a detected uloop executable is owned by Homebrew.
    /// </summary>
    public static class HomebrewManagedCliPolicy
    {
        private const char PATH_SEPARATOR = '/';
        private const char WINDOWS_PATH_SEPARATOR = '\\';
        private const string HOMEBREW_LINK_DIR_NAME = "bin";

        /// <summary>
        /// Reports whether the executable at the given path is installed and owned by Homebrew.
        /// </summary>
        /// <remarks>
        /// Why two shapes: brew expands every formula under a Cellar segment and symlinks it from
        /// prefix/bin, and shell detection reports the unresolved prefix/bin path returned by
        /// "command -v". Unity's .NET profile cannot resolve symlinks, so a linked path is
        /// recognized by probing the sibling Cellar formula directory instead.
        /// </remarks>
        public static bool IsHomebrewManagedPath(string executablePath, Func<string, bool> directoryExists)
        {
            Debug.Assert(directoryExists != null, "directoryExists must not be null");

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string normalizedPath = executablePath.Replace(WINDOWS_PATH_SEPARATOR, PATH_SEPARATOR);
            string[] segments = normalizedPath.Split(PATH_SEPARATOR);
            foreach (string segment in segments)
            {
                if (string.Equals(segment, CliConstants.HOMEBREW_CELLAR_DIR_NAME, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            string cellarFormulaDirectory = BuildCellarFormulaDirectory(segments);
            return cellarFormulaDirectory != null && directoryExists(cellarFormulaDirectory);
        }

        /// <summary>
        /// Builds the Cellar formula directory that a prefix/bin executable would be linked from.
        /// </summary>
        /// <remarks>
        /// Why the bin check: brew links formulae only into prefix/bin, so any other parent directory
        /// cannot be a Homebrew link and must not be classified by the sibling-Cellar probe.
        /// </remarks>
        private static string BuildCellarFormulaDirectory(string[] segments)
        {
            if (segments.Length < 3)
            {
                return null;
            }

            if (!string.Equals(segments[segments.Length - 2], HOMEBREW_LINK_DIR_NAME, StringComparison.Ordinal))
            {
                return null;
            }

            string formulaName = segments[segments.Length - 1];
            string prefix = string.Join(
                PATH_SEPARATOR.ToString(),
                segments,
                0,
                segments.Length - 2);
            return prefix + PATH_SEPARATOR + CliConstants.HOMEBREW_CELLAR_DIR_NAME + PATH_SEPARATOR + formulaName;
        }
    }
}
