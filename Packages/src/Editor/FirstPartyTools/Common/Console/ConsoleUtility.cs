#if UNITY_6000_0_OR_NEWER
using UnityEditor;
#endif

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Universal Console Utility wrapper
    /// Uses Unity 6+ standard API when available, fallback to custom implementation for older versions
    /// </summary>
    public static class ConsoleUtility
    {
        /// <summary>
        /// Gets console log counts by type (Universal API)
        /// </summary>
        /// <param name="errorCount">Number of error logs</param>
        /// <param name="warningCount">Number of warning logs</param>
        /// <param name="logCount">Number of info logs</param>
        public static void GetConsoleLogCounts(out int errorCount, out int warningCount, out int logCount)
        {
#if UNITY_6000_0_OR_NEWER
            ConsoleWindowUtility.GetConsoleLogCounts(out errorCount, out warningCount, out logCount);
#else
            GenericConsoleWindowUtility.GetConsoleLogCounts(out errorCount, out warningCount, out logCount);
#endif
        }

        /// <summary>
        /// Clears the Unity Editor Console (Universal API)
        /// Unity does not provide a public API for clearing the Editor Console Window,
        /// so we use reflection-based implementation for all Unity versions
        /// Note: Debug.ClearDeveloperConsole() is for runtime developer console, not Editor Console
        /// </summary>
        public static void ClearConsole()
        {
            GenericConsoleWindowUtility.ClearConsole();
        }
    }
}
