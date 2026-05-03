using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Supported log types for filtering: Log, Warning, Error, All
    /// </summary>
    public static class McpLogType
    {
        public const string Log = "Log";
        public const string Warning = "Warning";
        public const string Error = "Error";
        public const string All = "All";
    }

    /// <summary>
    /// Schema for GetLogs command parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class GetLogsSchema : BaseToolSchema
    {
        /// <summary>
        /// Log type to filter (Error, Warning, Log, All)
        /// </summary>
        [Description("Log type to filter (Error, Warning, Log, All)")]
        public string LogType { get; set; } = McpLogType.All;

        /// <summary>
        /// Maximum number of logs to retrieve
        /// </summary>
        [Description("Maximum number of logs to retrieve")]
        public int MaxCount { get; set; } = 100;

        /// <summary>
        /// Text to search within log messages (retrieve all if empty)
        /// </summary>
        [Description("Text to search within log messages (retrieve all if empty)")]
        public string SearchText { get; set; } = "";

        /// <summary>
        /// Whether to use regular expression for search
        /// </summary>
        [Description("Whether to use regular expression for search")]
        public bool UseRegex { get; set; } = false;

        /// <summary>
        /// Whether to search within stack trace as well
        /// </summary>
        [Description("Whether to search within stack trace as well")]
        public bool SearchInStackTrace { get; set; } = false;

        /// <summary>
        /// Whether to display stack trace
        /// </summary>
        [Description("Whether to display stack trace")]
        public bool IncludeStackTrace { get; set; } = false;
    }
} 