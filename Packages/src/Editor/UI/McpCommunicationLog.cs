using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// An entry for the MCP communication log.
    /// Log information that includes a request/response pair.
    /// </summary>
    public record McpCommunicationLogEntry
    {
        public readonly string CommandName;
        public readonly DateTime Timestamp;
        public readonly string Request;
        public readonly string Response;
        public readonly bool IsError;
        public bool IsExpanded { get; set; }

        public string HeaderText => $"[{CommandName}: {Timestamp:HH:mm:ss}]";

        public McpCommunicationLogEntry(string commandName, DateTime timestamp, string request, string response, bool isError, bool isExpanded = false)
        {
            CommandName = commandName;
            Timestamp = timestamp;
            Request = request;
            Response = response;
            IsError = isError;
            IsExpanded = isExpanded;
        }
    }

    /// <summary>
    /// A class for managing MCP communication logs.
    /// </summary>
    public static class McpCommunicationLogger
    {
        // Note: Communication logs are now managed via McpSessionManager

        private static List<McpCommunicationLogEntry> _logs;
        private static Dictionary<string, PendingRequest> _pendingRequests;

        /// <summary>
        /// Event for when the log is updated (for UI updates).
        /// </summary>
        public static event Action OnLogUpdated;

        /// <summary>
        /// Static constructor (automatically executed after a domain reload).
        /// </summary>
        static McpCommunicationLogger()
        {
            LoadFromSessionState();
        }

        /// <summary>
        /// Information about pending requests.
        /// </summary>
        private record PendingRequest
        {
            public readonly string CommandName;
            public readonly DateTime Timestamp;
            public readonly string RequestJson;

            public PendingRequest(string commandName, DateTime timestamp, string requestJson)
            {
                CommandName = commandName;
                Timestamp = timestamp;
                RequestJson = requestJson;
            }
        }

        /// <summary>
        /// Gets all logs.
        /// </summary>
        public static IReadOnlyList<McpCommunicationLogEntry> GetAllLogs()
        {
            lock (_logs)
            {
                return new List<McpCommunicationLogEntry>(_logs).AsReadOnly();
            }
        }

        /// <summary>
        /// Records a request (awaiting response).
        /// </summary>
        public static async Task LogRequest(string jsonRequest)
        {
            // Skip if communication logs are disabled
            if (!McpEditorSettings.GetSettings().enableCommunicationLogs)
            {
                return;
            }
            
            JObject request = JObject.Parse(jsonRequest);
            string method = request["method"]?.ToString() ?? "unknown";
            string id = NormalizeId(request["id"]);

            PendingRequest pendingRequest = new(method, DateTime.Now, jsonRequest);

            // Store in memory immediately (on any thread)
            lock (_pendingRequests)
            {
                _pendingRequests[id] = pendingRequest;
            }

            // Switch to main thread for SessionState operations
            await MainThreadSwitcher.SwitchToMainThread();
            
            SaveToSessionState();
            OnLogUpdated?.Invoke();
        }

        /// <summary>
        /// Records a response (adds it to the log paired with its request).
        /// </summary>
        public static async Task RecordLogResponse(string jsonResponse)
        {
            // Skip if communication logs are disabled
            if (!McpEditorSettings.GetSettings().enableCommunicationLogs)
            {
                return;
            }
            
            JObject response = JObject.Parse(jsonResponse);
            string id = NormalizeId(response["id"]);

            PendingRequest pendingRequest;
            bool foundPendingRequest;
            
            // Process response in memory immediately (on any thread)
            lock (_pendingRequests)
            {
                foundPendingRequest = _pendingRequests.TryGetValue(id, out pendingRequest);
                if (foundPendingRequest)
                {
                    _pendingRequests.Remove(id);
                }
            }
            
            if (foundPendingRequest)
            {
                bool isError = response["error"] != null;

                // Create new logs in a closed state.
                McpCommunicationLogEntry logEntry = new(
                    pendingRequest.CommandName,
                    pendingRequest.Timestamp,
                    pendingRequest.RequestJson,
                    jsonResponse,
                    isError,
                    false // Start with the toggle closed.
                );

                lock (_logs)
                {
                    // If there are existing logs, close them all (before adding a new log).
                    foreach (McpCommunicationLogEntry existingLog in _logs)
                    {
                        existingLog.IsExpanded = false;
                    }
                    
                    _logs.Add(logEntry);
                    
                    // Remove old entries if exceeding maximum
                    while (_logs.Count > McpUIConstants.MAX_COMMUNICATION_LOG_ENTRIES)
                    {
                        _logs.RemoveAt(0); // Remove oldest entry
                    }
                }

                // Switch to main thread for SessionState operations
                await MainThreadSwitcher.SwitchToMainThread();
                
                SaveToSessionState();
                OnLogUpdated?.Invoke();
            }
            else
            {
            }
        }

        /// <summary>
        /// Clears all logs.
        /// </summary>
        public static void ClearLogs()
        {
            lock (_logs)
            {
                _logs.Clear();
            }
            
            lock (_pendingRequests)
            {
                _pendingRequests.Clear();
            }

            // Also completely delete from SessionState and execute UI update on the main thread.
            EditorApplication.delayCall += () =>
            {
                ClearLogSessionState();
                OnLogUpdated?.Invoke();
            };
        }

        /// <summary>
        /// Restores data from SessionState.
        /// </summary>
        private static void LoadFromSessionState()
        {
            // Restore logs.
            string logsJson = McpEditorSettings.GetCommunicationLogsJson();
            _logs = JsonConvert.DeserializeObject<List<McpCommunicationLogEntry>>(logsJson) ?? new List<McpCommunicationLogEntry>();

            // Restore pending requests.
            string pendingJson = McpEditorSettings.GetPendingRequestsJson();
            _pendingRequests = JsonConvert.DeserializeObject<Dictionary<string, PendingRequest>>(pendingJson) ?? new Dictionary<string, PendingRequest>();
        }

        /// <summary>
        /// Saves data to SessionState.
        /// </summary>
        public static void SaveToSessionState()
        {
            // Create snapshots to avoid collection modification during serialization
            List<McpCommunicationLogEntry> logsSnapshot;
            Dictionary<string, PendingRequest> pendingSnapshot;
            
            lock (_logs)
            {
                logsSnapshot = new List<McpCommunicationLogEntry>(_logs);
            }
            
            lock (_pendingRequests)
            {
                pendingSnapshot = new Dictionary<string, PendingRequest>(_pendingRequests);
            }

            string logsJson = JsonConvert.SerializeObject(logsSnapshot);
            string pendingJson = JsonConvert.SerializeObject(pendingSnapshot);

            McpEditorSettings.SetCommunicationLogsJson(logsJson);
            McpEditorSettings.SetPendingRequestsJson(pendingJson);
        }

        /// <summary>
        /// Clears SessionState related to communication logs (for fixing log issues).
        /// </summary>
        public static void ClearLogSessionState()
        {
            McpEditorSettings.ClearCommunicationLogs();
            
            lock (_logs)
            {
                _logs?.Clear();
            }
            
            lock (_pendingRequests)
            {
                _pendingRequests?.Clear();
            }

            // SessionState clear complete (no log output).

            // Notify UI of update.
            EditorApplication.delayCall += () => OnLogUpdated?.Invoke();
        }

        /// <summary>
        /// Normalize ID to consistent string representation for proper matching
        /// </summary>
        private static string NormalizeId(JToken idToken)
        {
            if (idToken == null)
            {
                return "unknown";
            }

            // Convert both string and numeric IDs to string for consistent matching
            return idToken.ToString();
        }
    }
}