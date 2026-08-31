namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Host capability used by tools that need a read-only snapshot of Unity Console entries.
    /// </summary>
    public interface IUnityCliLoopConsoleLogService
    {
        UnityCliLoopConsoleLogResult GetLogs(string logType);

        UnityCliLoopConsoleLogResult GetLogsWithSearch(
            string logType,
            string searchText,
            bool useRegex,
            bool searchInStackTrace);
    }

    /// <summary>
    /// Host capability used by tools that need to clear Unity Console entries.
    /// </summary>
    public interface IUnityCliLoopConsoleClearService
    {
        UnityCliLoopConsoleClearResult Clear(bool addConfirmationMessage);
    }

    /// <summary>
    /// Supported Unity Console log families used by log-related tools.
    /// </summary>
    public static class UnityCliLoopLogType
    {
        public const string Log = "Log";
        public const string Warning = "Warning";
        public const string Error = "Error";
        public const string All = "All";
    }

    /// <summary>
    /// Immutable Unity Console entry snapshot shared across tool boundaries.
    /// </summary>
    public sealed class UnityCliLoopConsoleLogEntry
    {
        public readonly string Type;
        public readonly string Message;
        public readonly string StackTrace;

        public UnityCliLoopConsoleLogEntry(string type, string message, string stackTrace)
        {
            Type = type;
            Message = message;
            StackTrace = stackTrace;
        }
    }

    /// <summary>
    /// Immutable Unity Console result snapshot shared across tool boundaries.
    /// </summary>
    public sealed class UnityCliLoopConsoleLogResult
    {
        public readonly UnityCliLoopConsoleLogEntry[] LogEntries;
        public readonly int TotalCount;

        public UnityCliLoopConsoleLogResult(UnityCliLoopConsoleLogEntry[] logEntries, int totalCount)
        {
            LogEntries = logEntries ?? new UnityCliLoopConsoleLogEntry[0];
            TotalCount = totalCount;
        }
    }

    /// <summary>
    /// Immutable Unity Console clear-count snapshot shared across tool boundaries.
    /// </summary>
    public sealed class UnityCliLoopConsoleClearCounts
    {
        public readonly int ErrorCount;
        public readonly int WarningCount;
        public readonly int LogCount;

        public UnityCliLoopConsoleClearCounts(int errorCount, int warningCount, int logCount)
        {
            ErrorCount = errorCount;
            WarningCount = warningCount;
            LogCount = logCount;
        }
    }

    /// <summary>
    /// Immutable Unity Console clear result shared across tool boundaries.
    /// </summary>
    public sealed class UnityCliLoopConsoleClearResult
    {
        public readonly bool Success;
        public readonly int ClearedLogCount;
        public readonly UnityCliLoopConsoleClearCounts ClearedCounts;
        public readonly string Message;

        public UnityCliLoopConsoleClearResult(
            bool success,
            int clearedLogCount,
            UnityCliLoopConsoleClearCounts clearedCounts,
            string message)
        {
            Success = success;
            ClearedLogCount = clearedLogCount;
            ClearedCounts = clearedCounts ?? new UnityCliLoopConsoleClearCounts(0, 0, 0);
            Message = message ?? string.Empty;
        }
    }
}
