using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Log retrieval service
    /// Single function: Retrieve Unity Console logs
    /// Related classes: LogGetter, GetLogsTool, GetLogsUseCase
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public class LogRetrievalService
    {
        /// <summary>
        /// Retrieve logs of specified log type
        /// </summary>
        /// <param name="logType">Log type to retrieve</param>
        /// <returns>Log data</returns>
        public LogDisplayDto GetLogs(string logType)
        {
            if (string.Equals(logType, McpLogType.All, StringComparison.OrdinalIgnoreCase))
            {
                return LogGetter.GetAllConsoleLogs();
            }
            else
            {
                return LogGetter.GetConsoleLogsByType(logType);
            }
        }

        /// <summary>
        /// Retrieve logs with search conditions
        /// </summary>
        /// <param name="logType">Log type to retrieve</param>
        /// <param name="searchText">Search text</param>
        /// <param name="useRegex">Whether to use regular expressions</param>
        /// <param name="searchInStackTrace">Whether to search within stack trace</param>
        /// <returns>Log data</returns>
        public LogDisplayDto GetLogsWithSearch(string logType, string searchText, bool useRegex, bool searchInStackTrace)
        {
            return LogGetter.SearchConsoleLogs(logType, searchText, useRegex, searchInStackTrace);
        }
    }
}