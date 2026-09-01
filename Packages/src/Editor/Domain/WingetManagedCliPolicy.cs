using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Classifies whether a detected uloop executable is owned by winget.
    /// </summary>
    public static class WingetManagedCliPolicy
    {
        private const char PATH_SEPARATOR = '/';
        private const char WINDOWS_PATH_SEPARATOR = '\\';

        /// <summary>
        /// Reports whether the executable path contains adjacent WinGet and managed-directory segments.
        /// </summary>
        public static bool IsWingetManagedPath(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string normalizedPath = executablePath.Replace(WINDOWS_PATH_SEPARATOR, PATH_SEPARATOR);
            string[] segments = normalizedPath.Split(PATH_SEPARATOR);
            for (int index = 0; index + 1 < segments.Length; index++)
            {
                if (!string.Equals(
                        segments[index],
                        CliConstants.WINGET_ROOT_DIR_NAME,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string managedDirectory = segments[index + 1];
                if (string.Equals(
                        managedDirectory,
                        CliConstants.WINGET_PACKAGES_DIR_NAME,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        managedDirectory,
                        CliConstants.WINGET_LINKS_DIR_NAME,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
