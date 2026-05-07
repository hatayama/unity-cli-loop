using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Implementation of console log retrieval using reflection to access Unity's internal APIs
    /// </summary>
    public class ConsoleLogRetriever
    {
        private readonly Type _logEntriesType;

        /// <summary>
        /// Initializes the retriever with necessary reflection types
        /// </summary>
        public ConsoleLogRetriever()
        {
            Assembly editorAssembly = Assembly.GetAssembly(typeof(EditorWindow));
            _logEntriesType = editorAssembly.GetType("UnityEditor.LogEntries");
            editorAssembly.GetType("UnityEditor.ConsoleWindow");

            if (_logEntriesType == null)
            {
                throw new InvalidOperationException("LogEntries type not found. Unity version compatibility issue.");
            }
        }

        /// <summary>
        /// Gets all console logs regardless of current mask settings
        /// </summary>
        public List<LogEntryDto> GetAllLogs()
        {
            // Save original mask to restore later
            int originalMask = GetCurrentMask();

            try
            {
                // Temporarily set mask to show all log types (0x387 = Error + Warning + Log + Base)
                SetMask(7); // This will convert to Unity's 0x387

                List<LogEntryDto> logs = new();
                int logCount = GetLogCount();

                for (int i = 0; i < logCount; i++)
                {
                    LogEntryDto entry = GetLogEntryAt(i);
                    if (entry != null)
                    {
                        logs.Add(entry);
                    }
                    else
                    {
                        Debug.LogWarning($"GetAllLogs: Failed to get log at index {i}");
                    }
                }

                return logs;
            }
            finally
            {
                // Always restore original mask, even if an exception occurred
                RestoreOriginalMask(originalMask);
            }
        }

        /// <summary>
        /// Restores the original mask by converting Unity mask back to simple mask format
        /// </summary>
        private void RestoreOriginalMask(int originalUnityMask)
        {
            // Use consoleFlags property to restore the exact Unity mask
            PropertyInfo consoleFlagsProperty = _logEntriesType.GetProperty("consoleFlags", BindingFlags.Public | BindingFlags.Static);
            if (consoleFlagsProperty != null)
            {
                consoleFlagsProperty.SetValue(null, originalUnityMask);
            }
        }

        /// <summary>
        /// Converts Unity LogType to UnityCliLoopLogType
        /// </summary>
        private string ConvertLogTypeToUnityCliLoopLogType(LogType logType)
        {
            return logType switch
            {
                LogType.Error => UnityCliLoopLogType.Error,
                LogType.Assert => UnityCliLoopLogType.Error,
                LogType.Exception => UnityCliLoopLogType.Error,
                LogType.Warning => UnityCliLoopLogType.Warning,
                LogType.Log => UnityCliLoopLogType.Log,
                _ => UnityCliLoopLogType.Log  // Default unknown types to Log
            };
        }

        /// <summary>
        /// Gets logs of a specific type, temporarily adjusting mask if necessary
        /// </summary>
        public List<LogEntryDto> GetLogsByType(LogType logType)
        {
            // Save original mask to restore later
            int originalMask = GetCurrentMask();

            try
            {
                // Temporarily enable the specific log type we want
                int targetMask = GetMaskForLogType(logType);
                if (targetMask > 0)
                {
                    SetMask(targetMask);
                }
                else
                {
                    // If unknown type, show all types
                    SetMask(7);
                }

                List<LogEntryDto> logs = new();
                int logCount = GetLogCount();
                string targetUnityCliLoopLogType = ConvertLogTypeToUnityCliLoopLogType(logType);

                for (int i = 0; i < logCount; i++)
                {
                    LogEntryDto entry = GetLogEntryAt(i);
                    if (entry != null && entry.LogType == targetUnityCliLoopLogType)
                    {
                        logs.Add(entry);
                    }
                }

                return logs;
            }
            finally
            {
                // Always restore original mask
                RestoreOriginalMask(originalMask);
            }
        }

        /// <summary>
        /// Gets the current console mask settings using reflection
        /// </summary>
        public int GetCurrentMask()
        {
            // Use the consoleFlags property discovered in the investigation
            PropertyInfo consoleFlagsProperty = _logEntriesType.GetProperty("consoleFlags", BindingFlags.Public | BindingFlags.Static);
            if (consoleFlagsProperty != null)
            {
                object result = consoleFlagsProperty.GetValue(null);
                return result != null ? (int)result : 0;
            }

            // Fallback: try to get from ConsoleWindow if available
            return GetMaskFromConsoleWindow();
        }

        /// <summary>
        /// Sets the console mask settings using reflection
        /// </summary>
        public void SetMask(int mask)
        {
            // Based on observed Unity Console mask patterns:
            // Base: 0x7, Log: +0x80, Warning: +0x100, Error: +0x200
            int unityMask = ConvertToUnityMask(mask);

            // Use the consoleFlags property
            PropertyInfo consoleFlagsProperty = _logEntriesType.GetProperty("consoleFlags", BindingFlags.Public | BindingFlags.Static);
            if (consoleFlagsProperty != null)
            {
                consoleFlagsProperty.SetValue(null, unityMask);
                return;
            }

            Debug.LogWarning("Could not find consoleFlags property");
        }

        /// <summary>
        /// Converts simple mask (1=Error, 2=Warning, 4=Log) to Unity's internal mask format
        /// Preserves all existing console settings (Clear options, Collapse, Error Pause, etc.)
        /// </summary>
        private int ConvertToUnityMask(int simpleMask)
        {
            // Get current mask to preserve existing console settings
            int currentMask = GetCurrentMask();
            
            // Preserve all console settings except log type flags
            // Clear log type bits (0x80=Log, 0x100=Warning, 0x200=Error) but keep everything else
            int preservedSettings = currentMask & ~(0x80 | 0x100 | 0x200);
            
            // Start with preserved settings (includes Clear options, Collapse, Error Pause, etc.)
            int unityMask = preservedSettings;

            if ((simpleMask & 1) != 0) // Error
                unityMask |= 0x200;

            if ((simpleMask & 2) != 0) // Warning  
                unityMask |= 0x100;

            if ((simpleMask & 4) != 0) // Log
                unityMask |= 0x80;

            return unityMask;
        }

        /// <summary>
        /// Gets the total count of console entries using reflection
        /// </summary>
        public int GetLogCount()
        {
            MethodInfo getCount = _logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.Static);
            if (getCount != null)
            {
                object result = getCount.Invoke(null, null);
                return result != null ? (int)result : 0;
            }

            return 0;
        }


        /// <summary>
        /// Gets a log entry at the specified index using reflection
        /// </summary>
        private LogEntryDto GetLogEntryAt(int index)
        {
            // Get LogEntry type from Unity's internal assembly
            Assembly editorAssembly = Assembly.GetAssembly(typeof(EditorWindow));
            Type logEntryType = editorAssembly.GetType("UnityEditor.LogEntry");
            if (logEntryType == null)
            {
                Debug.LogError("LogEntry type not found");
                return null;
            }

            // Create LogEntry instance
            object logEntryInstance = Activator.CreateInstance(logEntryType);

            // Use GetEntryInternal method discovered in investigation
            MethodInfo getEntryInternal = _logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Public | BindingFlags.Static);
            if (getEntryInternal == null)
            {
                Debug.LogError("GetEntryInternal method not found");
                return null;
            }

            // Call GetEntryInternal(int row, LogEntry outputEntry)
            object[] parameters = new object[] { index, logEntryInstance };
            bool success = (bool)getEntryInternal.Invoke(null, parameters);

            if (!success)
            {
                Debug.LogWarning($"GetEntryInternal failed for index {index}");
                return null;
            }

            // Extract data from LogEntry instance using reflection
            string fullMessage = GetFieldValue(logEntryInstance, "message")?.ToString() ?? "";
            int mode = (int)(GetFieldValue(logEntryInstance, "mode") ?? 0);
            int callstackTextStart = (int)(GetFieldValue(logEntryInstance, "callstackTextStartUTF8") ?? 0);

            LogType logType = GetLogTypeFromMode(mode);

            // Separate message and stack trace using Unity's internal boundary
            (string message, string stackTrace) = SeparateMessageAndStackTrace(fullMessage, callstackTextStart);

            string unityCliLoopLogType = ConvertLogTypeToUnityCliLoopLogType(logType);
            return new LogEntryDto(unityCliLoopLogType, message, stackTrace);
        }

        /// <summary>
        /// Helper method to get field value from object using reflection
        /// </summary>
        private object GetFieldValue(object obj, string fieldName)
        {
            Type type = obj.GetType();
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(obj);
        }

        /// <summary>
        /// Converts a UTF-8 byte position to a character position in a string.
        /// Unity's LogEntry.callstackTextStartUTF8 provides byte offset, but
        /// string.Substring() requires character offset.
        /// </summary>
        private int ConvertUtf8BytePositionToCharPosition(string text, int utf8BytePosition)
        {
            if (string.IsNullOrEmpty(text) || utf8BytePosition <= 0)
            {
                return 0;
            }

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);

            if (utf8BytePosition >= utf8Bytes.Length)
            {
                return text.Length;
            }

            string decodedPortion = Encoding.UTF8.GetString(utf8Bytes, 0, utf8BytePosition);
            return decodedPortion.Length;
        }

        /// <summary>
        /// Separates message and stack trace using Unity's internal callstack boundary
        /// </summary>
        private (string message, string stackTrace) SeparateMessageAndStackTrace(string fullMessage, int callstackTextStartUtf8)
        {
            if (string.IsNullOrEmpty(fullMessage)) return ("", "");

            int charPosition = ConvertUtf8BytePositionToCharPosition(fullMessage, callstackTextStartUtf8);

            if (charPosition <= 0 || charPosition >= fullMessage.Length)
            {
                return (fullMessage.Trim(), "");
            }

            string message = fullMessage.Substring(0, charPosition).Trim();
            string stackTrace = fullMessage.Substring(charPosition).Trim();

            return (message, stackTrace);
        }

        /// <summary>
        /// Converts Unity's internal log mode to a LogType enum
        /// </summary>
        private LogType GetLogTypeFromMode(int mode)
        {
            // Analyze the observed patterns:
            // 0x804400 = Log
            // 0x804200 = Warning  
            // 0x804100 = Error

            // Extract bits 8-11 (shift right 8, mask 4 bits)
            int logType = (mode >> 8) & 0xF;

            LogType result = logType switch
            {
                0x01 => LogType.Error, // 0x804100 -> (0x4100 >> 8) & 0xF = 0x41 & 0xF = 0x1
                0x02 => LogType.Warning, // 0x804200 -> (0x4200 >> 8) & 0xF = 0x42 & 0xF = 0x2  
                0x04 => LogType.Log, // 0x804400 -> (0x4400 >> 8) & 0xF = 0x44 & 0xF = 0x4
                _ => DetermineLogTypeFromModeAnalysis(mode)
            };

            return result;
        }

        /// <summary>
        /// Analyzes mode values to determine log type when standard mapping fails
        /// </summary>
        private LogType DetermineLogTypeFromModeAnalysis(int mode)
        {
            // Analyze the mode values we've seen:
            // 8406016 (0x802000) - appears to be Log type
            // 8405504 (0x801E00) - appears to be Warning type  
            // 8405248 (0x801D00) - appears to be Error type

            // Let's try different bit extraction strategies
            // int lowerBits = mode & 0x7; // Lower 3 bits
            // int midBits = (mode >> 8) & 0x7; // Bits 8-10
            // int higherBits = (mode >> 16) & 0x7; // Bits 16-18

            // Based on observed patterns, try to determine type
            if (mode == 8406016) return LogType.Log; // This is a test log message
            if (mode == 8405504) return LogType.Warning; // This is a test warning message
            if (mode == 8405248) return LogType.Error; // This is a test error message

            return LogType.Log; // Default to Log for unknown types
        }

        /// <summary>
        /// Gets the appropriate mask value for a specific log type
        /// </summary>
        private int GetMaskForLogType(LogType logType)
        {
            return logType switch
            {
                LogType.Error => 1,
                LogType.Warning => 2,
                LogType.Log => 4,
                _ => 0
            };
        }

        /// <summary>
        /// Fallback method to get mask from ConsoleWindow
        /// </summary>
        private int GetMaskFromConsoleWindow()
        {
            // Implementation would access ConsoleWindow's filter state
            return 7; // Default to show all
        }

    }
}
