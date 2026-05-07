using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Log retrieval service
    /// Single function: Retrieve Unity Console logs
    /// Related classes: LogGetter, GetLogsTool, GetLogsUseCase
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public class LogRetrievalService : IUnityCliLoopConsoleLogService
    {
        /// <summary>
        /// Retrieve logs of specified log type
        /// </summary>
        /// <param name="logType">Log type to retrieve</param>
        /// <returns>Log data</returns>
        public LogDisplayDto GetLogs(string logType)
        {
            if (string.Equals(logType, UnityCliLoopLogType.All, StringComparison.OrdinalIgnoreCase))
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

        UnityCliLoopConsoleLogResult IUnityCliLoopConsoleLogService.GetLogs(string logType)
        {
            return ToConsoleLogResult(GetLogs(logType));
        }

        UnityCliLoopConsoleLogResult IUnityCliLoopConsoleLogService.GetLogsWithSearch(
            string logType,
            string searchText,
            bool useRegex,
            bool searchInStackTrace)
        {
            return ToConsoleLogResult(GetLogsWithSearch(logType, searchText, useRegex, searchInStackTrace));
        }

        private static UnityCliLoopConsoleLogResult ToConsoleLogResult(LogDisplayDto logData)
        {
            LogEntryDto[] entries = logData?.LogEntries ?? Array.Empty<LogEntryDto>();
            UnityCliLoopConsoleLogEntry[] snapshots = new UnityCliLoopConsoleLogEntry[entries.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                LogEntryDto entry = entries[i];
                snapshots[i] = new UnityCliLoopConsoleLogEntry(
                    entry.LogType,
                    entry.Message,
                    entry.StackTrace);
            }

            return new UnityCliLoopConsoleLogResult(snapshots, logData?.TotalCount ?? 0);
        }
    }
}
