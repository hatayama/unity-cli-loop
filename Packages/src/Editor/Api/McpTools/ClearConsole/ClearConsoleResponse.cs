using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response schema for ClearConsole command
    /// Provides type-safe response structure for console clearing operation
    /// </summary>
    public class ClearConsoleResponse : BaseToolResponse
    {
        /// <summary>
        /// Whether the console clear operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Number of logs that were cleared from the console
        /// </summary>
        public int ClearedLogCount { get; set; }

        /// <summary>
        /// Breakdown of cleared logs by type
        /// </summary>
        public ClearedLogCounts ClearedCounts { get; set; }

        /// <summary>
        /// Message describing the clear operation result
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Error message if the operation failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Create a new ClearConsoleResponse for successful operation
        /// </summary>
        public ClearConsoleResponse(bool success, int clearedLogCount, ClearedLogCounts clearedCounts, string message)
        {
            Success = success;
            ClearedLogCount = clearedLogCount;
            ClearedCounts = clearedCounts ?? new ClearedLogCounts();
            Message = message ?? string.Empty;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Create a new ClearConsoleResponse for failed operation
        /// </summary>
        public ClearConsoleResponse(string errorMessage)
        {
            Success = false;
            ClearedLogCount = 0;
            ClearedCounts = new ClearedLogCounts();
            Message = string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public ClearConsoleResponse()
        {
            ClearedCounts = new ClearedLogCounts();
            Message = string.Empty;
            ErrorMessage = string.Empty;
        }
    }

    /// <summary>
    /// Breakdown of cleared logs by type
    /// </summary>
    public class ClearedLogCounts
    {
        /// <summary>
        /// Number of error logs that were cleared
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// Number of warning logs that were cleared
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// Number of info logs that were cleared
        /// </summary>
        public int LogCount { get; set; }

        public ClearedLogCounts()
        {
        }

        public ClearedLogCounts(int errorCount, int warningCount, int logCount)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            LogCount = logCount;
        }
    }
} 