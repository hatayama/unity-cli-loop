using System;
using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// A class that provides a general-purpose static API for retrieving console logs.
    /// Uses ConsoleLogRetriever to access Unity's console logs directly via reflection.
    /// </summary>
    [InitializeOnLoad]
    public static class LogGetter
    {
        private static readonly ConsoleLogRetriever LogRetriever;

        // Unity classifies csc.rsp compiler diagnostics as LogType.Log internally,
        // so without message-based detection they would be invisible to Error/Warning filters.
        // Note: Debug.Log messages matching ": error CSxxxx" / ": warning CSxxxx" will also
        // be treated as compiler diagnostics. This is by design.
        private static readonly Regex CompilerErrorPattern = new Regex(@":\s*error CS\d+\b", RegexOptions.Compiled);
        private static readonly Regex CompilerWarningPattern = new Regex(@":\s*warning CS\d+\b", RegexOptions.Compiled);

        static LogGetter()
        {
            LogRetriever = new ConsoleLogRetriever();
        }

        private static string NormalizeMcpLogType(string mcpLogType)
        {
            if (string.Equals(mcpLogType, McpLogType.Error, StringComparison.OrdinalIgnoreCase))
            {
                return McpLogType.Error;
            }

            if (string.Equals(mcpLogType, McpLogType.Warning, StringComparison.OrdinalIgnoreCase))
            {
                return McpLogType.Warning;
            }

            if (string.Equals(mcpLogType, McpLogType.Log, StringComparison.OrdinalIgnoreCase))
            {
                return McpLogType.Log;
            }

            if (string.Equals(mcpLogType, McpLogType.All, StringComparison.OrdinalIgnoreCase))
            {
                return McpLogType.All;
            }

            return McpLogType.Log;
        }

        private static bool IsAssertionLikeEntry(LogEntryDto entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(entry.StackTrace))
            {
                return false;
            }

            if (entry.StackTrace.IndexOf("Debug:LogAssertion", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (entry.StackTrace.IndexOf("Debug.LogAssertion", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            if (entry.StackTrace.IndexOf("Debug:Assert", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            return entry.StackTrace.IndexOf("Debug.Assert", StringComparison.Ordinal) >= 0;
        }

        private static bool IsCompilerDiagnosticMessage(LogEntryDto entry, Regex pattern)
        {
            if (entry == null)
            {
                return false;
            }

            if (!string.Equals(entry.LogType, McpLogType.Log, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return pattern.IsMatch(entry.Message);
        }

        private static bool IsErrorFamilyEntry(LogEntryDto entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.Equals(entry.LogType, McpLogType.Error, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsAssertionLikeEntry(entry))
            {
                return true;
            }

            return IsCompilerDiagnosticMessage(entry, CompilerErrorPattern);
        }

        private static bool IsWarningFamilyEntry(LogEntryDto entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.Equals(entry.LogType, McpLogType.Warning, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsCompilerDiagnosticMessage(entry, CompilerWarningPattern);
        }

        private static List<LogEntryDto> GetFamilyEntries(
            List<LogEntryDto> allEntries,
            Func<LogEntryDto, bool> familyPredicate,
            string targetLogType)
        {
            List<LogEntryDto> familyEntries = new(allEntries.Count);

            foreach (LogEntryDto entry in allEntries)
            {
                if (!familyPredicate(entry))
                {
                    continue;
                }

                if (string.Equals(entry.LogType, targetLogType, StringComparison.OrdinalIgnoreCase))
                {
                    familyEntries.Add(entry);
                    continue;
                }

                LogEntryDto normalizedEntry = new(targetLogType, entry.Message, entry.StackTrace);
                familyEntries.Add(normalizedEntry);
            }

            return familyEntries;
        }

        private static List<LogEntryDto> GetLogsByMcpLogType(string logType)
        {
            string normalizedLogType = NormalizeMcpLogType(logType);

            if (string.Equals(normalizedLogType, McpLogType.All, StringComparison.Ordinal))
            {
                return LogRetriever.GetAllLogs();
            }

            if (string.Equals(normalizedLogType, McpLogType.Error, StringComparison.Ordinal))
            {
                List<LogEntryDto> allEntries = LogRetriever.GetAllLogs();
                return GetFamilyEntries(allEntries, IsErrorFamilyEntry, McpLogType.Error);
            }

            if (string.Equals(normalizedLogType, McpLogType.Warning, StringComparison.Ordinal))
            {
                List<LogEntryDto> allEntries = LogRetriever.GetAllLogs();
                return GetFamilyEntries(allEntries, IsWarningFamilyEntry, McpLogType.Warning);
            }

            UnityEngine.LogType unityLogType = ConvertMcpLogTypeToLogType(normalizedLogType);
            return LogRetriever.GetLogsByType(unityLogType);
        }

        /// <summary>
        /// Converts McpLogType to Unity's LogType
        /// </summary>
        /// <param name="mcpLogType">Unity console log type</param>
        /// <returns>Corresponding Unity LogType</returns>
        private static LogType ConvertMcpLogTypeToLogType(string mcpLogType)
        {
            string normalizedLogType = NormalizeMcpLogType(mcpLogType);

            return normalizedLogType switch
            {
                McpLogType.Error => LogType.Error,
                McpLogType.Warning => LogType.Warning,
                McpLogType.Log => LogType.Log,
                _ => LogType.Log  // Default for unknown types, None will be handled separately
            };
        }

        /// <summary>
        /// Retrieves all console logs and returns them as a LogDisplayDto.
        /// </summary>
        /// <returns>The retrieved log data.</returns>
        public static LogDisplayDto GetAllConsoleLogs()
        {
            System.Collections.Generic.List<LogEntryDto> logEntries = LogRetriever.GetAllLogs();
            return new LogDisplayDto(logEntries.ToArray(), logEntries.Count);
        }

        /// <summary>
        /// Directly retrieves an array of console log entries.
        /// </summary>
        /// <returns>An array of log entries.</returns>
        public static LogEntryDto[] GetConsoleLogEntries()
        {
            return LogRetriever.GetAllLogs().ToArray();
        }

        /// <summary>
        /// Filters and retrieves console logs by log type.
        /// </summary>
        /// <param name="logType">The log type to filter by (if "All", all types are retrieved).</param>
        /// <returns>The filtered log data.</returns>
        public static LogDisplayDto GetConsoleLogsByType(string logType)
        {
            List<LogEntryDto> allEntries = GetLogsByMcpLogType(logType);
            return new LogDisplayDto(allEntries.ToArray(), allEntries.Count);
        }

        /// <summary>
        /// Filters and retrieves console logs by log type and searches within message content.
        /// </summary>
        /// <param name="logType">The log type to filter by (if "All", all types are included).</param>
        /// <param name="searchText">The text to search for within messages (if null or empty, no search is performed).</param>
        /// <param name="useRegex">Whether to use regular expression for search.</param>
        /// <param name="searchInStackTrace">Whether to search within stack trace as well.</param>
        /// <returns>The filtered log data.</returns>
        public static LogDisplayDto SearchConsoleLogs(string logType, string searchText, bool useRegex, bool searchInStackTrace)
        {
            // Get logs based on type
            List<LogEntryDto> allEntries = GetLogsByMcpLogType(logType);
            
            // Filter by search text if provided
            if (!string.IsNullOrEmpty(searchText))
            {
                if (useRegex)
                {
                    Regex regex = new Regex(searchText);
                    allEntries = allEntries.FindAll(entry => 
                    {
                        bool messageMatch = regex.IsMatch(entry.Message);
                        bool stackTraceMatch = searchInStackTrace && !string.IsNullOrEmpty(entry.StackTrace) && regex.IsMatch(entry.StackTrace);
                        return messageMatch || stackTraceMatch;
                    });
                }
                else
                {
                    allEntries = allEntries.FindAll(entry => 
                    {
                        bool messageMatch = entry.Message.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
                        bool stackTraceMatch = searchInStackTrace && !string.IsNullOrEmpty(entry.StackTrace) && 
                                             entry.StackTrace.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
                        return messageMatch || stackTraceMatch;
                    });
                }
            }
            
            return new LogDisplayDto(allEntries.ToArray(), allEntries.Count);
        }

        /// <summary>
        /// Gets the total number of console logs.
        /// </summary>
        /// <returns>The total number of logs.</returns>
        public static int GetConsoleLogCount()
        {
            return LogRetriever.GetLogCount();
        }

        /// <summary>
        /// Clears the logs of the custom log manager.
        /// </summary>
        public static void ClearCustomLogs()
        {
            // This method is no longer needed since we're using ConsoleLogRetriever
            // Console logs are managed by Unity itself
        }
    }
}
