using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Event handler for McpEditorWindow - Manages Unity and server events
    /// Helper class for Presenter layer in MVP architecture
    /// Related classes:
    /// - McpEditorWindow: Main presenter that owns this handler
    /// - McpEditorModel: Model layer for state management
    /// - McpBridgeServer: Server that provides events
    /// - McpServerController: Server lifecycle management
    /// </summary>
    internal class McpEditorWindowEventHandler
    {
        private static readonly ProfilerMarker s_onEditorUpdateMarker =
            new ProfilerMarker("McpEditorWindow.OnEditorUpdate");
        private static readonly ProfilerMarker s_refreshUiMarker =
            new ProfilerMarker("McpEditorWindow.RefreshUI");

        private readonly McpEditorModel _model;
        private readonly McpEditorWindow _window;

        public McpEditorWindowEventHandler(McpEditorModel model, McpEditorWindow window)
        {
            _model = model;
            _window = window;
        }

        /// <summary>
        /// Initialize all event subscriptions
        /// </summary>
        public void Initialize()
        {
            SubscribeToUnityEvents();
            SubscribeToServerEvents();
        }

        /// <summary>
        /// Cleanup all event subscriptions
        /// </summary>
        public void Cleanup()
        {
            UnsubscribeFromUnityEvents();
            UnsubscribeFromServerEvents();
        }

        /// <summary>
        /// Subscribe to Unity Editor events
        /// </summary>
        private void SubscribeToUnityEvents()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>
        /// Unsubscribe from Unity Editor events
        /// </summary>
        private void UnsubscribeFromUnityEvents()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void SubscribeToServerEvents()
        {
            UnsubscribeFromServerEvents();

            McpBridgeServer.OnServerStarted += OnServerStateChanged;
            McpBridgeServer.OnServerStopping += OnServerStateChanged;
            ConnectedToolsMonitoringService.OnConnectedToolsChanged += OnConnectedToolsChanged;
            CustomToolManager.OnToolsChanged += OnToolsChanged;

            McpBridgeServer currentServer = McpServerController.CurrentServer;
            if (currentServer != null)
            {
                currentServer.OnClientConnected += OnClientConnected;
                currentServer.OnClientDisconnected += OnClientDisconnected;
            }
        }

        private void UnsubscribeFromServerEvents()
        {
            McpBridgeServer.OnServerStarted -= OnServerStateChanged;
            McpBridgeServer.OnServerStopping -= OnServerStateChanged;
            ConnectedToolsMonitoringService.OnConnectedToolsChanged -= OnConnectedToolsChanged;
            CustomToolManager.OnToolsChanged -= OnToolsChanged;

            McpBridgeServer currentServer = McpServerController.CurrentServer;
            if (currentServer != null)
            {
                currentServer.OnClientConnected -= OnClientConnected;
                currentServer.OnClientDisconnected -= OnClientDisconnected;
            }
        }

        private void OnServerStateChanged()
        {
            _window.InvalidateToolSettingsCatalog();
            _model.RequestRepaint();
        }

        private void OnConnectedToolsChanged()
        {
            _model.RequestRepaint();
        }

        private void OnToolsChanged()
        {
            _window.InvalidateToolSettingsCatalog();
            _model.RequestRepaint();
        }

        /// <summary>
        /// Handle client connection event - force UI repaint for immediate update
        /// </summary>
        private void OnClientConnected(string clientEndpoint)
        {
            // Enhanced logging for debugging client connection
            // Count check for debugging purposes only
            
            // Clear reconnecting flags when client connects
            McpServerController.ClearReconnectingFlag();
            
            // Mark that repaint is needed since events are called from background thread
            _model.RequestRepaint();

            // Exit post-compile mode when client connects
            if (_model.Runtime.IsPostCompileMode)
            {
                _model.DisablePostCompileMode();
            }
        }

        /// <summary>
        /// Handle client disconnection event - force UI repaint for immediate update
        /// </summary>
        private void OnClientDisconnected(string clientEndpoint)
        {
            // Enhanced logging for debugging client disconnection issues
            // Count check for debugging purposes only
            
            
            // Mark that repaint is needed since events are called from background thread
            _model.RequestRepaint();
        }

        private void OnEditorUpdate()
        {
            using (s_onEditorUpdateMarker.Auto())
            {
                if (!McpEditorWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(_model.Runtime))
                {
                    return;
                }

                _model.ClearRepaintRequest();
                using (s_refreshUiMarker.Auto())
                {
                    _window.RefreshAllSections();
                }
            }
        }

        /// <summary>
        /// Re-subscribe to server events (called after server start)
        /// </summary>
        public void RefreshServerEventSubscriptions()
        {
            SubscribeToServerEvents();
        }
    }

    // Post-compile recovery can stay active while UI data is unchanged, so explicit repaint
    // requests gate expensive full-section refreshes.
    internal static class McpEditorWindowRefreshPolicy
    {
        public static bool ShouldRefreshOnEditorUpdate(RuntimeState runtimeState)
        {
            Debug.Assert(runtimeState != null, "runtimeState must not be null");

            return runtimeState.NeedsRepaint;
        }

        public static bool ShouldRunExpensiveChecks(McpEditorWindowRefreshMode refreshMode)
        {
            return refreshMode == McpEditorWindowRefreshMode.Full;
        }

        public static bool ShouldRefreshSkillInstallState(
            McpEditorWindowRefreshMode refreshMode,
            bool refreshRequested)
        {
            return refreshRequested && ShouldRunExpensiveChecks(refreshMode);
        }

        public static bool ShouldKeepToolSettingsCatalogDirty(ToolSettingsSectionData toolSettingsData)
        {
            Debug.Assert(toolSettingsData != null, "toolSettingsData must not be null");

            return toolSettingsData.ShowToolSettings && !toolSettingsData.IsRegistryAvailable;
        }

        public static bool ShouldStartToolSettingsRegistryWarmup(
            bool isAlreadyScheduled,
            int attemptCount,
            int maxAttempts)
        {
            Debug.Assert(attemptCount >= 0, "attemptCount must not be negative");
            Debug.Assert(maxAttempts > 0, "maxAttempts must be positive");

            return !isAlreadyScheduled && attemptCount < maxAttempts;
        }

        public static double CalculateToolSettingsRegistryWarmupDelaySeconds(
            double initialDelaySeconds,
            double maxDelaySeconds,
            int attemptCount)
        {
            Debug.Assert(initialDelaySeconds > 0.0, "initialDelaySeconds must be positive");
            Debug.Assert(maxDelaySeconds >= initialDelaySeconds, "maxDelaySeconds must not be smaller than initialDelaySeconds");
            Debug.Assert(attemptCount >= 0, "attemptCount must not be negative");

            double delaySeconds = initialDelaySeconds;
            for (int i = 0; i < attemptCount; i++)
            {
                delaySeconds *= 2.0;
            }

            return System.Math.Min(delaySeconds, maxDelaySeconds);
        }
    }

    internal enum McpEditorWindowRefreshMode
    {
        InitialPaint,
        Full
    }
}
