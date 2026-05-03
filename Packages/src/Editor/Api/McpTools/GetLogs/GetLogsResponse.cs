using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Individual log entry information
    /// </summary>
    public class LogEntry
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }

        public LogEntry(string type, string message, string stackTrace)
        {
            Type = type;
            Message = message;
            StackTrace = stackTrace;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public LogEntry()
        {
        }
    }

    /// <summary>
    /// Response schema for GetLogs command
    /// Provides type-safe response structure
    /// </summary>
    public class GetLogsResponse : BaseToolResponse
    {
        /// <summary>
        /// Total number of logs available
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Number of logs displayed in this response
        /// </summary>
        public int DisplayedCount { get; set; }

        /// <summary>
        /// Log type filter used
        /// </summary>
        public string LogType { get; set; }

        /// <summary>
        /// Maximum count limit used
        /// </summary>
        public int MaxCount { get; set; }

        /// <summary>
        /// Search text filter used
        /// </summary>
        public string SearchText { get; set; }

        /// <summary>
        /// Whether stack trace was included
        /// </summary>
        public bool IncludeStackTrace { get; set; }

        /// <summary>
        /// Array of log entries
        /// </summary>
        public LogEntry[] Logs { get; set; }

        /// <summary>
        /// Create a new GetLogsResponse
        /// </summary>
        public GetLogsResponse(int totalCount, int displayedCount, string logType, int maxCount, 
                              string searchText, bool includeStackTrace, LogEntry[] logs)
        {
            TotalCount = totalCount;
            DisplayedCount = displayedCount;
            LogType = logType;
            MaxCount = maxCount;
            SearchText = searchText;
            IncludeStackTrace = includeStackTrace;
            Logs = logs ?? Array.Empty<LogEntry>();
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public GetLogsResponse()
        {
            LogType = string.Empty;
            SearchText = string.Empty;
            Logs = Array.Empty<LogEntry>();
        }
    }
} 