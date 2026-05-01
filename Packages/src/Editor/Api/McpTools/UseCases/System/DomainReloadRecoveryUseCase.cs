using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// UseCase responsible for temporal cohesion of Domain Reload recovery processing
    /// Processing sequence: 1. Pre-stop processing, 2. Recovery processing, 3. Notification processing
    /// Related classes: DomainReloadDetectionService, SessionRecoveryService, ClientNotificationService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class DomainReloadRecoveryUseCase
    {
        /// <summary>
        /// Execute processing before Domain Reload starts
        /// </summary>
        /// <param name="currentServer">Current server instance</param>
        /// <returns>Processing result</returns>
        public ServiceResult<string> ExecuteBeforeDomainReload(McpBridgeServer currentServer)
        {
            // 1. Generate tracking ID for related operations
            string correlationId = VibeLogger.GenerateCorrelationId();

            // 2. Check server state from instance
            bool serverRunning = currentServer?.IsRunning ?? false;
            int? serverPort = currentServer?.Port;

            // 3. Fallback to session state if instance is null but session says server was running
                    // Handles case where bridge server instance became null unexpectedly
            if (currentServer == null && McpEditorSettings.GetIsServerRunning())
            {
                int sessionPort = McpEditorSettings.GetCustomPort();
                if (NetworkUtility.IsValidPort(sessionPort))
                {
                    serverRunning = true;
                    serverPort = sessionPort;
                    VibeLogger.LogWarning(
                        "domain_reload_session_fallback",
                        "Server instance is null but session state indicates running. Using session state for recovery.",
                        new { session_port = sessionPort },
                        correlationId
                    );
                }
                else
                {
                    VibeLogger.LogWarning(
                        "domain_reload_session_fallback_invalid_port",
                        "Session indicates running but port is invalid. Ignoring session state fallback.",
                        new { session_port = sessionPort },
                        correlationId
                    );
                }
            }

            // 4. Detect and record Domain Reload start
            DomainReloadDetectionService.StartDomainReload(correlationId, serverRunning, serverPort);

            // 4. If server is running, execute stop processing
            if (currentServer?.IsRunning == true)
            {
                int portToSave = currentServer.Port;
                
                try
                {
                    // 4.1. Notify client of server stop
                    ClientNotificationService.LogServerStoppingBeforeDomainReload(correlationId, portToSave);

                    // 4.2. Stop server
                    currentServer.Dispose();

                    // 4.3. Notify client of stop completion
                    ClientNotificationService.LogServerStoppedAfterDomainReload(correlationId);

                    return ServiceResult<string>.SuccessResult(correlationId);
                }
                catch (System.Exception ex)
                {
                    // 4.4. Error notification
                    ClientNotificationService.LogServerShutdownError(correlationId, ex, portToSave);
                    DomainReloadDetectionService.RollbackDomainReloadStart(correlationId);

                    // Server stop failure is a critical error (causes port conflicts)
                    throw new System.InvalidOperationException(
                        $"Failed to properly shutdown Unity CLI bridge before assembly reload. This may cause port conflicts on restart.", ex);
                }
            }

            return ServiceResult<string>.SuccessResult(correlationId);
        }

        /// <summary>
        /// Execute recovery processing after Domain Reload completion
        /// </summary>
        /// <returns>Processing result</returns>
        public async Task<ServiceResult<string>> ExecuteAfterDomainReloadAsync(CancellationToken cancellationToken = default)
        {
            // 1. Generate tracking ID for related operations
            string correlationId = VibeLogger.GenerateCorrelationId();

            // 2. Record Domain Reload completion
            DomainReloadDetectionService.CompleteDomainReload(correlationId);

            // 3. Start timeout for reconnection UI display if needed
            if (DomainReloadDetectionService.ShouldShowReconnectingUI())
            {
                SessionRecoveryService.StartReconnectionUITimeoutAsync().Forget();
            }

            // 4. Restore server state
            ValidationResult restoreResult = SessionRecoveryService.RestoreServerStateIfNeeded();
            if (!restoreResult.IsValid)
            {
                return ServiceResult<string>.FailureResult($"Server restoration failed: {restoreResult.ErrorMessage}");
            }

            // 5. Process pending compile requests (currently disabled)
            ProcessPendingCompileRequests(correlationId);

            // 6. Send tool change notification if server is running
            if (McpServerController.IsServerRunning)
            {
                try
                {
                    await ClientNotificationService.SendToolNotificationAfterCompilationAsync();
                }
                catch (System.Exception ex)
                {
                    VibeLogger.LogWarning("tool_notification_failed", $"Failed to send tool notification: {ex.Message}", correlationId: correlationId);
                }
            }

            return ServiceResult<string>.SuccessResult(correlationId);
        }

        /// <summary>
        /// Process pending compile requests
        /// Note: Currently disabled by feature flag (to avoid main thread errors)
        /// </summary>
        /// <param name="correlationId">Correlation ID for tracking related operations</param>
        private void ProcessPendingCompileRequests(string correlationId)
        {
            // Feature flag control - currently disabled, can be enabled via editor settings in the future
            // TODO: Add McpEditorSettings.GetEnablePendingCompileProcessing() when needed
            bool enablePendingCompileProcessing = false;
            
            if (enablePendingCompileProcessing)
            {
                // Planned to be enabled after main thread issue resolution
                // CompileSessionState.StartForcedRecompile();
                VibeLogger.LogInfo(
                    "pending_compile_processing", 
                    "Processing pending compile requests", 
                    correlationId: correlationId
                );
            }
            else
            {
                VibeLogger.LogInfo(
                    "pending_compile_processing_disabled", 
                    "Pending compile request processing is disabled via feature flag", 
                    correlationId: correlationId
                );
            }
        }
    }
}
