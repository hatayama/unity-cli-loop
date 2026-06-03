using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// AI-friendly structured logger for Unity CLI Loop.
    /// 
    /// Key features:
    /// - Structured JSON logging with operation, context, correlation_id
    /// - AI-friendly format for Claude Code analysis
    /// - Automatic file rotation and memory management
    /// - Correlation ID tracking for related operations
    /// </summary>
    public sealed class VibeLoggerService
    {
        private readonly string _logDirectory = Path.Combine(UnityEngine.Application.dataPath, "..", UnityCliLoopConstants.OUTPUT_ROOT_DIR, UnityCliLoopConstants.VIBE_LOGS_DIR);
        private const string LOG_FILE_PREFIX = "unity_vibe";
        private const int MAX_FILE_SIZE_MB = 10;
        private const int MAX_MEMORY_LOGS = 1000;
        private const int MAX_LOG_FILES = 3;
        private const int MAX_WRITE_RETRIES = 20;
        private const int WRITE_RETRY_DELAY_MS = 25;
        
        private readonly List<VibeLogEntry> _memoryLogs = new List<VibeLogEntry>();
        private readonly object _lockObject = new object();
        private bool _hasCleanedUpOnStartup = false;
        private bool _hasReportedInterleaving = false;
        
        /// <summary>
        /// Represents one Vibe Log entry in the owning workflow.
        /// </summary>
        [Serializable]
        public class VibeLogEntry
        {
            public string timestamp;
            public string level;
            public string operation;
            public string message;
            public object context;
            public string correlation_id;
            public string source;
            public string human_note;
            public string ai_todo;
            public string stack_trace;
            public EnvironmentInfo environment;
        }
        
        /// <summary>
        /// Describes Environment information collected by the owning workflow.
        /// </summary>
        [Serializable]
        public class EnvironmentInfo
        {
            public string domain_reload_state;
        }
        
        /// <summary>
        /// Log an info level message with structured context
        /// Only logs when ULOOP_DEBUG symbol is defined
        /// </summary>
        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public void LogInfo(string operation, string message, object context = null, 
                                  string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = false)
        {
            Log("INFO", operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }
        
        /// <summary>
        /// Log a warning level message with structured context
        /// Only logs when ULOOP_DEBUG symbol is defined
        /// </summary>
        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public void LogWarning(string operation, string message, object context = null, 
                                     string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = true)
        {
            Log("WARNING", operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }
        
        /// <summary>
        /// Log an error level message with structured context
        /// Only logs when ULOOP_DEBUG symbol is defined
        /// </summary>
        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public void LogError(string operation, string message, object context = null, 
                                   string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = true)
        {
            Log("ERROR", operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }
        
        /// <summary>
        /// Log a debug level message with structured context
        /// Only logs when ULOOP_DEBUG symbol is defined
        /// </summary>
        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public void LogDebug(string operation, string message, object context = null, 
                                   string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = false)
        {
            Log("DEBUG", operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }
        
        /// <summary>
        /// Log an exception with structured context
        /// Only logs when ULOOP_DEBUG symbol is defined
        /// </summary>
        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public void LogException(string operation, Exception exception, object context = null, 
                                       string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = true)
        {
            Dictionary<string, object> exceptionContext = new();
            if (context != null)
            {
                exceptionContext["original_context"] = context;
            }
            
            exceptionContext["exception"] = new
            {
                type = exception.GetType().Name,
                message = exception.Message,
                stack_trace = exception.StackTrace,
                inner_exception = exception.InnerException?.Message
            };
            
            Log("ERROR", operation, $"Exception occurred: {exception.Message}", exceptionContext, 
                correlationId, humanNote, aiTodo, includeStackTrace);
        }
        
        /// <summary>
        /// Generate a new correlation ID for tracking related operations
        /// </summary>
        public string GenerateCorrelationId()
        {
            return $"unity_{Guid.NewGuid().ToString("N")[..8]}_{DateTime.Now:HHmmss}";
        }
        
        /// <summary>
        /// Get logs for AI analysis (formatted for Claude Code)
        /// Output directory: {project_root}/.uloop/outputs/VibeLogs/
        /// </summary>
        public string GetLogsForAi(string operation = null, string correlationId = null, int maxCount = 100)
        {
            lock (_lockObject)
            {
                List<VibeLogEntry> filteredLogs = new(_memoryLogs);
                
                if (!string.IsNullOrEmpty(operation))
                {
                    filteredLogs = filteredLogs.FindAll(log => log.operation.Contains(operation));
                }
                
                if (!string.IsNullOrEmpty(correlationId))
                {
                    filteredLogs = filteredLogs.FindAll(log => log.correlation_id == correlationId);
                }
                
                if (filteredLogs.Count > maxCount)
                {
                    filteredLogs = filteredLogs.GetRange(filteredLogs.Count - maxCount, maxCount);
                }
                
                return JsonConvert.SerializeObject(filteredLogs, Formatting.Indented);
            }
        }
        
        /// <summary>
        /// Clear all memory logs
        /// </summary>
        public void ClearMemoryLogs()
        {
            lock (_lockObject)
            {
                _memoryLogs.Clear();
            }
        }
        
        /// <summary>
        /// Core logging method
        /// </summary>
        private void Log(string level, string operation, string message, object context,
                               string correlationId, string humanNote, string aiTodo, bool includeStackTrace = true)
        {
            VibeLogEntry logEntry = new()            {
                timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
                level = level,
                operation = operation,
                message = message,
                context = context,
                correlation_id = correlationId ?? GenerateCorrelationId(),
                source = "Unity",
                human_note = humanNote,
                ai_todo = aiTodo,
                stack_trace = includeStackTrace ? new StackTrace(true).ToString() : null,
                environment = GetEnvironmentInfo()
            };
            
            // Add to memory logs
            lock (_lockObject)
            {
                _memoryLogs.Add(logEntry);
                
                // Rotate memory logs if too many
                if (_memoryLogs.Count > MAX_MEMORY_LOGS)
                {
                    _memoryLogs.RemoveAt(0);
                }
            }
            
            // Save to file
            try
            {
                SaveLogToFile(logEntry);
            }
            catch (Exception ex)
            {
                TrySaveFileDiagnosticLog(
                    "vibe_log_write_failed",
                    "VibeLogger failed to append a JSONL entry.",
                    new
                    {
                        log_path_identity = LOG_FILE_PREFIX,
                        source = "Unity",
                        operation = "append",
                        failed_operation = operation,
                        error = ex.Message,
                        retry_count = MAX_WRITE_RETRIES
                    });
                // Fallback to Unity console if file logging fails
                UnityEngine.Debug.LogError($"[VibeLogger] Failed to save log to file: {ex.Message}");
                UnityEngine.Debug.Log($"[VibeLogger] {level} | {operation} | {message}");
            }
        }
        
        /// <summary>
        /// Save log entry to file with file locking for concurrent access
        /// </summary>
        private void SaveLogToFile(VibeLogEntry logEntry)
        {
            SaveLogToFile(logEntry, validateIntegrity: true);
        }

        private void SaveLogToFile(VibeLogEntry logEntry, bool validateIntegrity)
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
            
            lock (_lockObject)
            {
                if (!_hasCleanedUpOnStartup)
                {
                    CleanupOldLogFiles();
                    _hasCleanedUpOnStartup = true;
                }

                string fileName = $"{LOG_FILE_PREFIX}_{DateTime.UtcNow:yyyyMMdd}.json";
                string filePath = Path.Combine(_logDirectory, fileName);
                RotateLogFileIfNeeded(filePath);
                AppendJsonLineWithRetry(filePath, JsonConvert.SerializeObject(logEntry) + "\n");
                if (validateIntegrity)
                {
                    DetectInterleavingIfNeeded(filePath);
                }
            }
        }

        private void RotateLogFileIfNeeded(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            FileInfo fileInfo = new(filePath);
            if (fileInfo.Length <= MAX_FILE_SIZE_MB * 1024 * 1024)
            {
                return;
            }

            string rotatedFileName = $"{LOG_FILE_PREFIX}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            string rotatedFilePath = Path.Combine(_logDirectory, rotatedFileName);
            File.Move(filePath, rotatedFilePath);
            CleanupOldLogFiles();
        }

        private static void AppendJsonLineWithRetry(string filePath, string jsonLine)
        {
            byte[] payload = Encoding.UTF8.GetBytes(jsonLine);
            for (int retry = 0; retry < MAX_WRITE_RETRIES; retry++)
            {
                try
                {
                    using (FileStream fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fileStream.Write(payload, 0, payload.Length);
                        fileStream.Flush();
                        return;
                    }
                }
                catch (IOException ex) when (IsFileSharingViolation(ex) && retry < MAX_WRITE_RETRIES - 1)
                {
                    Thread.Sleep(WRITE_RETRY_DELAY_MS * (retry + 1));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to write VibeLogger entry to file: {ex.Message}", ex);
                }
            }

            throw new InvalidOperationException($"Failed to write VibeLogger entry after {MAX_WRITE_RETRIES} retries due to file sharing violations");
        }

        private void DetectInterleavingIfNeeded(string filePath)
        {
            if (_hasReportedInterleaving)
            {
                return;
            }

            (bool HasMalformedLine, int LastValidLineNumber) result =
                InspectJsonLines(filePath);
            if (!result.HasMalformedLine)
            {
                return;
            }

            _hasReportedInterleaving = true;
            SaveLogToFile(
                CreateDiagnosticLogEntry(
                    "WARNING",
                    "vibe_log_write_interleaving_detected",
                    "Detected a malformed VibeLog JSONL entry after append.",
                    new
                    {
                        log_path_identity = Path.GetFileName(filePath),
                        source = "Unity",
                        process_id = Process.GetCurrentProcess().Id,
                        thread_id = Thread.CurrentThread.ManagedThreadId,
                        last_valid_line_number = result.LastValidLineNumber
                    }),
                validateIntegrity: false);
        }

        private static (bool HasMalformedLine, int LastValidLineNumber) InspectJsonLines(string filePath)
        {
            int lineNumber = 0;
            int lastValidLineNumber = 0;
            foreach (string line in File.ReadLines(filePath))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    JToken.Parse(line);
                    lastValidLineNumber = lineNumber;
                }
                catch (JsonReaderException)
                {
                    return (true, lastValidLineNumber);
                }
            }

            return (false, lastValidLineNumber);
        }

        private void TrySaveFileDiagnosticLog(
            string operation,
            string message,
            object context)
        {
            try
            {
                SaveLogToFile(
                    CreateDiagnosticLogEntry("ERROR", operation, message, context),
                    validateIntegrity: false);
            }
            catch (Exception diagnosticException)
            {
                UnityEngine.Debug.LogWarning($"[VibeLogger] Failed to save diagnostic log: {diagnosticException.Message}");
            }
        }

        private static VibeLogEntry CreateDiagnosticLogEntry(
            string level,
            string operation,
            string message,
            object context)
        {
            return new VibeLogEntry
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
                level = level,
                operation = operation,
                message = message,
                context = context,
                correlation_id = $"unity_{Guid.NewGuid().ToString("N")[..8]}_{DateTime.Now:HHmmss}",
                source = "Unity",
                environment = GetEnvironmentInfo()
            };
        }
        
        /// <summary>
        /// Check if exception is a file sharing violation
        /// </summary>
        private static bool IsFileSharingViolation(IOException ex)
        {
            // ERROR_SHARING_VIOLATION (0x80070020)
            const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
            return ex.HResult == ERROR_SHARING_VIOLATION;
        }
        
        /// <summary>
        /// Clean up old log files, keeping only the most recent MAX_LOG_FILES
        /// </summary>
        private void CleanupOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    return;
                    
                // Get all vibe log files, sorted by creation time (newest first)
                FileInfo[] logFiles = Directory.GetFiles(_logDirectory, $"{LOG_FILE_PREFIX}_*.json")
                    .Select(file => new FileInfo(file))
                    .OrderByDescending(file => file.CreationTime)
                    .ToArray();
                    
                // Delete files beyond the limit
                for (int i = MAX_LOG_FILES; i < logFiles.Length; i++)
                {
                    try
                    {
                        logFiles[i].Delete();
                    }
                    catch (Exception ex)
                    {
                        // Log deletion failure but don't crash
                        UnityEngine.Debug.LogWarning($"[VibeLogger] Failed to delete old log file {logFiles[i].Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[VibeLogger] Failed to cleanup old log files: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current environment information
        /// </summary>
        private static EnvironmentInfo GetEnvironmentInfo()
        {
            bool isDomainReloadInProgress = EditorApplication.isCompiling;

            return new EnvironmentInfo
            {
                domain_reload_state = isDomainReloadInProgress ? "InProgress" : "Idle"
            };
        }
    }

    /// <summary>
    /// Provides Vibe Logger behavior for Unity CLI Loop.
    /// </summary>
    public static class VibeLogger
    {
        private static readonly VibeLoggerService ServiceValue = new VibeLoggerService();

        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public static void LogInfo(string operation, string message, object context = null,
                                   string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = false)
        {
            ServiceValue.LogInfo(operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }

        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public static void LogWarning(string operation, string message, object context = null,
                                      string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = true)
        {
            ServiceValue.LogWarning(operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }

        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public static void LogError(string operation, string message, object context = null,
                                    string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = true)
        {
            ServiceValue.LogError(operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }

        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public static void LogDebug(string operation, string message, object context = null,
                                    string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = false)
        {
            ServiceValue.LogDebug(operation, message, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }

        [Conditional(UnityCliLoopConstants.ENV_KEY_ULOOP_DEBUG)]
        public static void LogException(string operation, Exception exception, object context = null,
                                        string correlationId = null, string humanNote = null, string aiTodo = null, bool includeStackTrace = true)
        {
            ServiceValue.LogException(operation, exception, context, correlationId, humanNote, aiTodo, includeStackTrace);
        }

        public static string GenerateCorrelationId()
        {
            return ServiceValue.GenerateCorrelationId();
        }

        public static string GetLogsForAi(string operation = null, string correlationId = null, int maxCount = 100)
        {
            return ServiceValue.GetLogsForAi(operation, correlationId, maxCount);
        }

        public static void ClearMemoryLogs()
        {
            ServiceValue.ClearMemoryLogs();
        }
    }
}
