using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for monitoring connected LLM tools
    /// Single responsibility: Track connected/disconnected tools and persist state
    /// Related classes: McpBridgeServer, McpEditorSettings, ConnectedLLMToolData
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    [InitializeOnLoad]
    public static class ConnectedToolsMonitoringService
    {
        private static List<ConnectedLLMToolData> _connectedTools = new();
        private static List<ConnectedLLMToolData> _toolsBackup;
        private static CancellationTokenSource _cleanupCancellationTokenSource;

        // Events for UI notification
        public static event System.Action OnConnectedToolsChanged;

        static ConnectedToolsMonitoringService()
        {
            Initialize();
        }

        /// <summary>
        /// Initialize the monitoring service
        /// </summary>
        private static void Initialize()
        {
            SubscribeToServerEvents();
            if (ShouldRestorePersistedToolsForReconnect())
            {
                RestoreConnectedToolsFromSettings();
                return;
            }

            SynchronizeConnectedToolsWithCurrentServer();
        }

        /// <summary>
        /// Subscribe to server lifecycle events
        /// </summary>
        private static void SubscribeToServerEvents()
        {
            McpBridgeServer.OnServerStopping += OnServerStopping;
            McpBridgeServer.OnServerStarted += OnServerStarted;
            McpBridgeServer.OnToolConnected += OnToolConnected;
            McpBridgeServer.OnToolDisconnected += OnToolDisconnected;
            McpBridgeServer.OnAllToolsCleared += OnAllToolsCleared;
        }

        /// <summary>
        /// Handle server stopping event - keep the last live snapshot for reconnecting UI
        /// </summary>
        private static void OnServerStopping()
        {
            _toolsBackup = _connectedTools
                .Where(tool => tool != null && !string.IsNullOrEmpty(tool.Name) && tool.Name != McpConstants.UNKNOWN_CLIENT_NAME)
                .ToList();

            SyncConnectedToolsToSettings();
        }

        /// <summary>
        /// Handle server started event - restore the reconnecting snapshot first, then cleanup stale tools
        /// </summary>
        private static void OnServerStarted()
        {
            if (_toolsBackup != null && _toolsBackup.Count > 0)
            {
                RestoreConnectedTools(_toolsBackup);
                _toolsBackup = null;
                return;
            }

            if (ShouldRestorePersistedToolsForReconnect())
            {
                ConnectedLLMToolData[] persistedTools = McpEditorSettings.GetConnectedLLMTools();
                if (persistedTools != null && persistedTools.Length > 0)
                {
                    RestoreConnectedTools(persistedTools.ToList());
                    return;
                }
            }

            SynchronizeConnectedToolsWithCurrentServer();
        }

        /// <summary>
        /// Handle tool connected event - add tool to connected list
        /// </summary>
        private static void OnToolConnected(ConnectedClient client)
        {
            AddConnectedTool(client);
        }

        /// <summary>
        /// Handle tool disconnected event - remove tool from connected list
        /// </summary>
        private static void OnToolDisconnected(string toolName)
        {
            RemoveConnectedTool(toolName);
        }

        /// <summary>
        /// Handle all tools cleared event - clear all connected tools
        /// </summary>
        private static void OnAllToolsCleared()
        {
            ClearConnectedTools();
        }

        /// <summary>
        /// Add a connected LLM tool
        /// </summary>
        public static void AddConnectedTool(ConnectedClient client)
        {
            if (client.ClientName == McpConstants.UNKNOWN_CLIENT_NAME)
            {
                return;
            }

            // Remove existing tool if present, then add
            _connectedTools.RemoveAll(tool => tool.Name == client.ClientName);

            ConnectedLLMToolData toolData = new(
                client.ClientName,
                client.Endpoint,
                client.Port,
                client.ConnectedAt
            );
            _connectedTools.Add(toolData);
            
            // Persist to settings
            McpEditorSettings.AddConnectedLLMTool(toolData);
            
            // Notify UI
            OnConnectedToolsChanged?.Invoke();
        }

        /// <summary>
        /// Remove a connected LLM tool
        /// </summary>
        public static void RemoveConnectedTool(string toolName)
        {
            _connectedTools.RemoveAll(tool => tool.Name == toolName);
            
            // Persist to settings
            McpEditorSettings.RemoveConnectedLLMTool(toolName);
            
            // Notify UI
            OnConnectedToolsChanged?.Invoke();
        }

        /// <summary>
        /// Clear all connected LLM tools
        /// </summary>
        public static void ClearConnectedTools()
        {
            _connectedTools.Clear();
            
            // Persist to settings
            McpEditorSettings.ClearConnectedLLMTools();
            
            // Notify UI
            OnConnectedToolsChanged?.Invoke();
        }

        /// <summary>
        /// Get connected tools as ConnectedClient objects for UI display, sorted by name
        /// </summary>
        public static IEnumerable<ConnectedClient> GetConnectedToolsAsClients()
        {
            return _connectedTools.OrderBy(tool => tool.Name).Select(tool => ConvertToConnectedClient(tool));
        }

        /// <summary>
        /// Convert stored tool data to ConnectedClient for UI display
        /// </summary>
        private static ConnectedClient ConvertToConnectedClient(ConnectedLLMToolData toolData)
        {
            return new ConnectedClient(toolData.Endpoint, null, toolData.Port, toolData.Name);
        }

        /// <summary>
        /// Restore connected tools from backup after server restart.
        /// The reconnecting UI should keep the last live snapshot until clients rejoin.
        /// </summary>
        private static void RestoreConnectedTools(List<ConnectedLLMToolData> backup)
        {
            if (backup == null || backup.Count == 0)
            {
                return;
            }

            RestoreConnectedToolsImmediately(backup);

            _cleanupCancellationTokenSource?.Cancel();
            _cleanupCancellationTokenSource?.Dispose();
            _cleanupCancellationTokenSource = new CancellationTokenSource();

            DelayedCleanupAsync(_cleanupCancellationTokenSource.Token).ContinueWith(task =>
            {
                if (task.IsFaulted && !task.IsCanceled)
                {
                    EditorApplication.delayCall += () =>
                    {
                        Debug.LogError($"[uLoopMCP] Failed to perform delayed cleanup: {task.Exception?.GetBaseException().Message}");
                    };
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static void RestoreConnectedToolsImmediately(IEnumerable<ConnectedLLMToolData> tools)
        {
            List<ConnectedLLMToolData> restoredTools = tools
                .Where(tool => tool != null && !string.IsNullOrEmpty(tool.Name) && tool.Name != McpConstants.UNKNOWN_CLIENT_NAME)
                .GroupBy(tool => tool.Name)
                .Select(group => group.Last())
                .OrderBy(tool => tool.Name)
                .ToList();

            _connectedTools = restoredTools;
            SyncConnectedToolsToSettings();
            OnConnectedToolsChanged?.Invoke();
        }

        private static async Task DelayedCleanupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await TimerDelay.Wait(8000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!McpServerController.IsServerRunning || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyCollection<ConnectedClient> actualConnectedClients = McpServerController.CurrentServer?.GetConnectedClients();
            if (actualConnectedClients == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            HashSet<string> actualClientNames = new HashSet<string>(
                actualConnectedClients
                    .Where(client => client.ClientName != McpConstants.UNKNOWN_CLIENT_NAME)
                    .Select(client => client.ClientName)
            );

            List<ConnectedLLMToolData> toolsToRemove = _connectedTools
                .Where(tool => !actualClientNames.Contains(tool.Name))
                .ToList();

            foreach (ConnectedLLMToolData tool in toolsToRemove)
            {
                RemoveConnectedTool(tool.Name);
            }
        }

        /// <summary>
        /// Rebuild connected tools from the current live server state.
        /// The connected tools UI must reflect live MCP connections only.
        /// </summary>
        private static void SynchronizeConnectedToolsWithCurrentServer()
        {
            IReadOnlyCollection<ConnectedClient> liveClients = GetLiveConnectedClients();
            ReplaceConnectedTools(liveClients);
        }

        private static bool ShouldRestorePersistedToolsForReconnect()
        {
            return McpEditorSettings.GetShowReconnectingUI() || McpEditorSettings.GetShowPostCompileReconnectingUI();
        }

        /// <summary>
        /// Get live clients from the current server, ignoring persisted settings.
        /// </summary>
        private static IReadOnlyCollection<ConnectedClient> GetLiveConnectedClients()
        {
            if (!McpServerController.IsServerRunning)
            {
                return Array.Empty<ConnectedClient>();
            }

            IReadOnlyCollection<ConnectedClient> connectedClients = McpServerController.CurrentServer?.GetConnectedClients();
            return connectedClients ?? Array.Empty<ConnectedClient>();
        }

        /// <summary>
        /// Replace the in-memory and persisted connected tools state with the current live clients.
        /// </summary>
        private static void ReplaceConnectedTools(IEnumerable<ConnectedClient> connectedClients)
        {
            List<ConnectedLLMToolData> tools = connectedClients
                .Where(client => client != null && client.ClientName != McpConstants.UNKNOWN_CLIENT_NAME)
                .Select(client => new ConnectedLLMToolData(
                    client.ClientName,
                    client.Endpoint,
                    client.Port,
                    client.ConnectedAt
                ))
                .GroupBy(tool => tool.Name)
                .Select(group => group.Last())
                .OrderBy(tool => tool.Name)
                .ToList();

            _connectedTools = tools;

            ConnectedLLMToolData[] toolsArray = _connectedTools
                .Where(tool => tool != null && !string.IsNullOrEmpty(tool.Name))
                .ToArray();
            SaveConnectedToolsWhenChanged(
                toolsArray,
                McpEditorSettings.GetConnectedLLMTools,
                McpEditorSettings.SetConnectedLLMTools);

            OnConnectedToolsChanged?.Invoke();
        }

        /// <summary>
        /// Restore the persisted connected tools snapshot for reconnecting UI only.
        /// This path must not be used for normal startup because it can reintroduce stale entries.
        /// </summary>
        private static void RestoreConnectedToolsFromSettings()
        {
            ConnectedLLMToolData[] savedTools = McpEditorSettings.GetConnectedLLMTools();
            if (savedTools == null || savedTools.Length == 0)
            {
                _connectedTools = new List<ConnectedLLMToolData>();
                return;
            }

            RestoreConnectedToolsImmediately(savedTools);
        }

        private static void SyncConnectedToolsToSettings()
        {
            ConnectedLLMToolData[] toolsArray = _connectedTools
                .Where(tool => tool != null && !string.IsNullOrEmpty(tool.Name) && tool.Name != McpConstants.UNKNOWN_CLIENT_NAME)
                .ToArray();
            SaveConnectedToolsWhenChanged(
                toolsArray,
                McpEditorSettings.GetConnectedLLMTools,
                McpEditorSettings.SetConnectedLLMTools);
        }

        internal static bool SaveConnectedToolsWhenChanged(
            ConnectedLLMToolData[] toolsArray,
            Func<ConnectedLLMToolData[]> loadPersistedTools,
            Action<ConnectedLLMToolData[]> savePersistedTools)
        {
            Debug.Assert(toolsArray != null, "toolsArray must not be null");
            Debug.Assert(loadPersistedTools != null, "loadPersistedTools must not be null");
            Debug.Assert(savePersistedTools != null, "savePersistedTools must not be null");

            ConnectedLLMToolData[] persistedTools = loadPersistedTools() ?? Array.Empty<ConnectedLLMToolData>();
            if (AreConnectedToolSnapshotsEquivalent(toolsArray, persistedTools))
            {
                return false;
            }

            savePersistedTools(toolsArray);
            return true;
        }

        private static bool AreConnectedToolSnapshotsEquivalent(
            IReadOnlyList<ConnectedLLMToolData> first,
            IReadOnlyList<ConnectedLLMToolData> second)
        {
            if (first == null || second == null)
            {
                return first == second;
            }

            if (first.Count != second.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Count; i++)
            {
                ConnectedLLMToolData firstTool = first[i];
                ConnectedLLMToolData secondTool = second[i];
                if (!AreConnectedToolsEquivalent(firstTool, secondTool))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreConnectedToolsEquivalent(
            ConnectedLLMToolData first,
            ConnectedLLMToolData second)
        {
            if (first == null || second == null)
            {
                return first == second;
            }

            return string.Equals(first.Name, second.Name, StringComparison.Ordinal)
                && string.Equals(first.Endpoint, second.Endpoint, StringComparison.Ordinal)
                && first.Port == second.Port;
        }

        internal static void ReplaceConnectedToolsForTests(IReadOnlyCollection<ConnectedClient> connectedClients)
        {
            ReplaceConnectedTools(connectedClients);
        }

        internal static void RestorePersistedConnectedToolsForTests()
        {
            RestoreConnectedToolsFromSettings();
        }

        internal static void ResetStateForTests()
        {
            _connectedTools = new List<ConnectedLLMToolData>();
            _toolsBackup = null;
            _cleanupCancellationTokenSource?.Cancel();
            _cleanupCancellationTokenSource?.Dispose();
            _cleanupCancellationTokenSource = null;
        }
    }
}
