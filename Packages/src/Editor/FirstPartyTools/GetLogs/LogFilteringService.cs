using System;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Log filtering service
    /// Single function: Filter and limit log entries
    /// Related classes: GetLogsTool, GetLogsUseCase, LogEntry
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
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

            UnityCliLoopConsoleLogEntry[] limitedEntries = entries.Length > maxCount
                ? entries.Skip(entries.Length - maxCount).ToArray()
                : entries;

            limitedEntries = limitedEntries.Reverse().ToArray();

            return limitedEntries.Select(entry => new LogEntry(
                type: entry.Type,
                message: entry.Message,
                stackTrace: includeStackTrace ? entry.StackTrace : null
            )).ToArray();
        }
    }
}
