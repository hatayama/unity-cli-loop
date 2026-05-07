
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schema for GetLogs command parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class GetLogsSchema : UnityCliLoopToolSchema
    {
        /// <summary>
        /// Log type to filter (Error, Warning, Log, All)
        /// </summary>
        public string LogType { get; set; } = UnityCliLoopLogType.All;

        /// <summary>
        /// Maximum number of logs to retrieve
        /// </summary>
        public int MaxCount { get; set; } = 100;

        /// <summary>
        /// Text to search within log messages (retrieve all if empty)
        /// </summary>
        public string SearchText { get; set; } = "";

        /// <summary>
        /// Whether to use regular expression for search
        /// </summary>
        public bool UseRegex { get; set; } = false;

        /// <summary>
        /// Whether to search within stack trace as well
        /// </summary>
        public bool SearchInStackTrace { get; set; } = false;

        /// <summary>
        /// Whether to display stack trace
        /// </summary>
        public bool IncludeStackTrace { get; set; } = false;
    }
}
