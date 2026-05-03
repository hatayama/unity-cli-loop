using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Unity 6 ConsoleWindowUtility API recreation for older Unity versions
    /// Provides event-driven console log monitoring and simple count retrieval
    /// </summary>
    public static class GenericConsoleWindowUtility
    {
        private static ConsoleLogRetriever logRetriever;
        
        static GenericConsoleWindowUtility()
        {
            Initialize();
        }

        /// <summary>
        /// Initialize the utility and start monitoring
        /// </summary>
        private static void Initialize()
        {
            logRetriever = new ConsoleLogRetriever();
        }

        /// <summary>
        /// Gets console log counts by type (Unity 6 compatible)
        /// </summary>
        /// <param name="errorCount">Number of error logs</param>
        /// <param name="warningCount">Number of warning logs</param>
        /// <param name="logCount">Number of info logs</param>
        public static void GetConsoleLogCounts(out int errorCount, out int warningCount, out int logCount)
        {
            errorCount = 0;
            warningCount = 0;
            logCount = 0;

            if (logRetriever == null) return;

            try
            {
                // Save current mask state
                int originalMask = logRetriever.GetCurrentMask();
                
                try
                {
                    // Temporarily set mask to show all log types to get accurate counts
                    logRetriever.SetMask(7); // Show all: Error(1) + Warning(2) + Log(4) = 7
                    
                    // Use Unity's internal GetCount method which respects Console clear state
                    int totalCount = logRetriever.GetLogCount();
                    
                    if (totalCount == 0)
                    {
                        // Console is empty, all counts are 0
                        return;
                    }
                    
                    // Get all logs and count by type
                    List<LogEntryDto> allLogs = logRetriever.GetAllLogs();
                    
                    foreach (LogEntryDto log in allLogs)
                    {
                        switch (log.LogType)
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
                }
                finally
                {
                    // Always restore original mask
                    logRetriever.SetMask(GetSimpleMaskFromUnityMask(originalMask));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Console log count retrieval failed: {ex.Message}");
                // Don't suppress this exception - caller needs to know count retrieval failed
                throw new InvalidOperationException(
                    "Failed to retrieve console log counts. Console monitoring may be compromised.", ex);
            }
        }

        /// <summary>
        /// Clears the Unity Console (Unity 6 compatible)
        /// </summary>
        public static void ClearConsole()
        {
            try
            {
                Type _logEntriesType = Assembly.GetAssembly(typeof(EditorWindow))
                    .GetType("UnityEditor.LogEntries");
                
                MethodInfo clearMethod = _logEntriesType.GetMethod("Clear", 
                    BindingFlags.Public | BindingFlags.Static);
                
                if (clearMethod != null)
                {
                    clearMethod.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Console clear operation failed: {ex.Message}");
                // Don't suppress this exception - caller needs to know clear failed
                throw new InvalidOperationException("Failed to clear Unity console. Console operations may be compromised.", ex);
            }
        }

        /// <summary>
        /// Converts Unity internal mask back to simple mask format
        /// </summary>
        /// <param name="unityMask">Unity's internal mask value</param>
        /// <returns>Simple mask (1=Error, 2=Warning, 4=Log)</returns>
        private static int GetSimpleMaskFromUnityMask(int unityMask)
        {
            int simpleMask = 0;
            
            if ((unityMask & 0x200) != 0) // Error bit
                simpleMask |= 1;
            
            if ((unityMask & 0x100) != 0) // Warning bit
                simpleMask |= 2;
                
            if ((unityMask & 0x80) != 0) // Log bit
                simpleMask |= 4;
            
            return simpleMask;
        }
    }
}