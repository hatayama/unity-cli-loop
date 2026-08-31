using System.ComponentModel;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Provides helper operations for Process Start behavior.
    /// </summary>
    public static class ProcessStartHelper
    {
        // NativeErrorCode 2 (ERROR_FILE_NOT_FOUND) and 3 (ERROR_PATH_NOT_FOUND) on Windows
        // map to ENOENT on POSIX; other Win32Exception causes (e.g. permission denied) should
        // propagate so callers fail fast instead of silently treating them as "not installed".
        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_PATH_NOT_FOUND = 3;

        public static Process TryStart(ProcessStartInfo startInfo)
        {
            try
            {
                return Process.Start(startInfo);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_FILE_NOT_FOUND
                                            || ex.NativeErrorCode == ERROR_PATH_NOT_FOUND)
            {
                return null;
            }
        }
    }
}
