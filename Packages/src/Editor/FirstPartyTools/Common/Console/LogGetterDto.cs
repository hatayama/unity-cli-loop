using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    [Serializable]
    public record LogEntryDto
    {
        public readonly string Message;
        public readonly string LogType;
        public readonly string StackTrace;

        public LogEntryDto(string logType, string message, string stackTrace)
        {
            Message = message;
            LogType = logType;
            StackTrace = stackTrace;
        }
    }

    public record LogDisplayDto
    {
        public readonly LogEntryDto[] LogEntries;
        public readonly int TotalCount;

        public LogDisplayDto(LogEntryDto[] logEntries, int totalCount)
        {
            LogEntries = logEntries;
            TotalCount = totalCount;
        }
    }
}
