using System.Threading.Tasks;
using Newtonsoft.Json;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for client notification processing
    /// Single responsibility: Sending notifications to MCP clients
    /// Related classes: McpBridgeServer, CustomToolManager
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public static class ClientNotificationService
    {
        /// <summary>
        /// Send tool change notification to clients
        /// </summary>
        /// <remarks>
        /// This notification serves dual purposes in this project:
        /// 1. Original MCP purpose: Notify that available tools have changed
        /// 2. uLoopMCP additional purpose: Signal that Unity is ready after Domain Reload
        ///
        /// After Domain Reload completes, this notification is sent to indicate Unity
        /// has finished initialization and can reliably process requests. TypeScript side
        /// uses this as a "Unity ready" signal. The name doesn't perfectly match this
        /// secondary purpose, but it avoids adding custom notification complexity.
        /// </remarks>
        public static void SendToolsChangedNotification()
        {
            McpBridgeServer currentServer = McpServerController.CurrentServer;
            if (currentServer == null)
            {
                return;
            }

            // Send only MCP standard notification
            var notificationParams = new
            {
                timestamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                message = "Unity tools have been updated"
            };

            var mcpNotification = new
            {
                jsonrpc = McpServerConfig.JSONRPC_VERSION,
                method = "notifications/tools/list_changed",
                @params = notificationParams
            };

            string mcpNotificationJson = JsonConvert.SerializeObject(mcpNotification);
            currentServer.SendNotificationToClients(mcpNotificationJson);

            // Log recording
            System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(true);
            string callerInfo = stackTrace.GetFrame(1)?.GetMethod()?.Name ?? "Unknown";
            
            VibeLogger.LogInfo(
                "tools_list_changed_notification",
                "Sent tools changed notification to MCP clients",
                new { caller = callerInfo, timestamp = notificationParams.timestamp }
            );
        }

        /// <summary>
        /// Check if server is running and send tool change notification
        /// </summary>
        public static void TriggerToolChangeNotification()
        {
            if (McpServerController.IsServerRunning)
            {
                SendToolsChangedNotification();
            }
        }

        /// <summary>
        /// Send tool notification with frame delay after compilation
        /// </summary>
        /// <returns>Task for notification sending process</returns>
        public static async Task SendToolNotificationAfterCompilationAsync()
        {
            // Frame delay for Unity editor stabilization after Domain Reload
            await EditorDelay.DelayFrame(1);
            
            CustomToolManager.NotifyToolChanges();
        }

        /// <summary>
        /// Send notification to specific clients
        /// </summary>
        /// <param name="notification">JSON data of notification to send</param>
        public static void SendNotificationToClients(string notification)
        {
            McpBridgeServer currentServer = McpServerController.CurrentServer;
            if (currentServer == null)
            {
                return;
            }

            currentServer.SendNotificationToClients(notification);
        }

        /// <summary>
        /// Log before server stop
        /// </summary>
        /// <param name="correlationId">Tracking ID for related operations</param>
        public static void LogServerStoppingBeforeDomainReload(string correlationId)
        {
            VibeLogger.LogInfo(
                "domain_reload_server_stopping",
                "Stopping Unity CLI bridge before domain reload",
                new { transport = "project_ipc" },
                correlationId
            );
        }

        /// <summary>
        /// Log server stop completion
        /// </summary>
        /// <param name="correlationId">Tracking ID for related operations</param>
        public static void LogServerStoppedAfterDomainReload(string correlationId)
        {
            VibeLogger.LogInfo(
                "domain_reload_server_stopped",
                "Unity CLI bridge stopped successfully",
                new { transport = "project_ipc" },
                correlationId
            );
        }

        /// <summary>
        /// Log server stop error
        /// </summary>
        /// <param name="correlationId">Tracking ID for related operations</param>
        /// <param name="ex">Exception that occurred</param>
        public static void LogServerShutdownError(string correlationId, System.Exception ex)
        {
            VibeLogger.LogException(
                "domain_reload_server_shutdown_error",
                ex,
                new
                {
                    transport = "project_ipc",
                    server_was_running = true
                },
                correlationId,
                "Critical error during server shutdown before assembly reload.",
                "Investigate server shutdown process and project IPC recovery."
            );
        }
    }
}
