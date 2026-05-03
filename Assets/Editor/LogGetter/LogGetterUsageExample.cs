using UnityEngine;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Usage examples for the LogGetter generic API.
    /// </summary>
    public class LogGetterUsageExample
    {
        
        [MenuItem("UnityCliLoop/Debug/LogGetter Tests/Direct Test")]
        public static void TestLogGetter()
        {
            Debug.Log("=== LogGetter Direct Test Start ===");

            var logData = LogGetter.GetAllConsoleLogs();
            Debug.Log($"LogGetter Result: TotalCount={logData.TotalCount}, LogEntries.Length={logData.LogEntries.Length}");

            foreach (var entry in logData.LogEntries)
            {
                Debug.Log($"Log Entry: Type={entry.LogType}, Message={entry.Message}");
            }

            Debug.Log("=== LogGetter Direct Test End ===");
        }

        [MenuItem("UnityCliLoop/Debug/LogGetter Tests/Run Usage Examples")]
        public static void RunUsageExamples()
        {
            Debug.Log("=== LogGetter Usage Examples Start ===");

            // Basic usage
            BasicUsage();

            // Filtering usage example
            FilteringUsage();

            // Example of getting the log count
            CountUsage();

            Debug.Log("=== LogGetter Usage Examples Complete ===");
        }

        private static void BasicUsage()
        {
            Debug.Log("--- Basic Usage ---");

            // How Masamichi requested to use it
            LogDisplayDto log = LogGetter.GetAllConsoleLogs();
            Debug.Log($"Number of logs retrieved: {log.TotalCount}");

            // Get log entries directly
            LogEntryDto[] entries = LogGetter.GetConsoleLogEntries();
            Debug.Log($"Length of log entries array: {entries.Length}");

            // Display details of each log
            foreach (LogEntryDto entry in entries)
            {
                Debug.Log($"[{entry.LogType}] {entry.Message}");
            }
        }

        private static void FilteringUsage()
        {
            Debug.Log("--- Filtering Usage Example ---");

            // Get only error logs
            LogDisplayDto errorLogs = LogGetter.GetConsoleLogsByType(McpLogType.Error);
            Debug.Log($"Number of error logs: {errorLogs.TotalCount}");

            // Get only warning logs
            LogDisplayDto warningLogs = LogGetter.GetConsoleLogsByType(McpLogType.Warning);
            Debug.Log($"Number of warning logs: {warningLogs.TotalCount}");

            // Get only normal logs
            LogDisplayDto normalLogs = LogGetter.GetConsoleLogsByType(McpLogType.Log);
            Debug.Log($"Number of normal logs: {normalLogs.TotalCount}");
        }

        private static void CountUsage()
        {
            Debug.Log("--- Log Count Retrieval Example ---");

            // Get only the total number of logs (lightweight)
            int totalCount = LogGetter.GetConsoleLogCount();
            Debug.Log($"Total number of console logs: {totalCount}");
        }

        [MenuItem("UnityCliLoop/Debug/LogGetter Tests/Custom Processing Example")]
        public static void CustomProcessingExample()
        {
            Debug.Log("=== Custom Processing Example ===");

            // Get logs and perform custom processing
            LogDisplayDto logs = LogGetter.GetAllConsoleLogs();

            int errorCount = 0;
            int warningCount = 0;
            int logCount = 0;

            foreach (LogEntryDto entry in logs.LogEntries)
            {
                switch (entry.LogType)
                {
                    case McpLogType.Error:
                        errorCount++;
                        break;
                    case McpLogType.Warning:
                        warningCount++;
                        break;
                    case McpLogType.Log:
                        logCount++;
                        break;
                }
            }

            Debug.Log($"Log Statistics - Errors: {errorCount}, Warnings: {warningCount}, Normal: {logCount}");
        }
    }
}