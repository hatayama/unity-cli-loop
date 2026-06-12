using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Log filtering service
    /// Single function: Filter and limit log entries
    /// Related classes: GetLogsTool, GetLogsUseCase, LogEntry
    /// </summary>
    public class LogFilteringService
    {
        public LogEntry[] FilterAndLimitLogs(UnityCliLoopConsoleLogEntry[] entries, int maxCount, bool includeStackTrace)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (maxCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be zero or greater.");
            }

            // Take the newest maxCount entries and return them newest-first in a single
            // pass; the previous LINQ chain copied the entries into three arrays per call.
            int resultCount = Math.Min(entries.Length, maxCount);
            LogEntry[] result = new LogEntry[resultCount];
            for (int i = 0; i < resultCount; i++)
            {
                UnityCliLoopConsoleLogEntry entry = entries[entries.Length - 1 - i];
                result[i] = new LogEntry(
                    type: entry.Type,
                    message: entry.Message,
                    stackTrace: includeStackTrace ? entry.StackTrace : null
                );
            }

            return result;
        }
    }
}
