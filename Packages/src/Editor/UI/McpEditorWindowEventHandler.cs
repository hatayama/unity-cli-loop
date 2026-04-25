using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
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

            McpBridgeServer currentServer = McpServerController.CurrentServer;
            if (currentServer != null)
            {
                currentServer.OnClientConnected -= OnClientConnected;
                currentServer.OnClientDisconnected -= OnClientDisconnected;
            }
        }

        private void OnServerStateChanged()
        {
            _model.RequestRepaint();
        }

        private void OnConnectedToolsChanged()
        {
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
    }
}
