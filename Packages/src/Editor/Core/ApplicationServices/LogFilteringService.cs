using System.Linq;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Log filtering service
    /// Single function: Filter and limit log entries
    /// Related classes: GetLogsTool, GetLogsUseCase, LogEntry
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public class LogFilteringService
    {
        /// <summary>
        /// Filter log entries and apply limits
        /// </summary>
        /// <param name="entries">Log entry array</param>
        /// <param name="maxCount">Maximum retrieval count</param>
        /// <param name="includeStackTrace">Whether to include stack trace</param>
        /// <returns>Filtered log entry array</returns>
        public LogEntry[] FilterAndLimitLogs(LogEntryDto[] entries, int maxCount, bool includeStackTrace)
        {
            // Get the most recent logs, limited by maxCount
            LogEntryDto[] limitedEntries = entries.Length > maxCount
                ? entries.Skip(entries.Length - maxCount).ToArray()
                : entries;
            
            // Reverse to show newest first
            limitedEntries = limitedEntries.Reverse().ToArray();

            // Convert from LogEntryDto to LogEntry
            return limitedEntries.Select(entry => new LogEntry(
                type: entry.LogType,
                message: entry.Message,
                stackTrace: includeStackTrace ? entry.StackTrace : null
            )).ToArray();
        }
    }
}