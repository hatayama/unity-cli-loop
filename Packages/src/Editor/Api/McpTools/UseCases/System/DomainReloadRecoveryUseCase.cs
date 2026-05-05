using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// UseCase responsible for temporal cohesion of Domain Reload recovery processing
    /// Processing sequence: 1. Pre-stop processing, 2. Recovery processing, 3. Pending compile processing
    /// Related classes: DomainReloadDetectionService, SessionRecoveryService
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

            // 3. Fallback to session state if instance is null but session says server was running
            // Handles case where bridge server instance became null unexpectedly
            if (currentServer == null && McpEditorSettings.GetIsServerRunning())
            {
                serverRunning = true;
                VibeLogger.LogWarning(
                    "domain_reload_session_fallback",
                    "Server instance is null but session state indicates running. Using project IPC session state for recovery.",
                    new { project_root = UnityMcpPathResolver.GetProjectRoot() },
                    correlationId
                );
            }

            // 4. Detect and record Domain Reload start
            DomainReloadDetectionService.StartDomainReload(correlationId, serverRunning);

            // 4. If server is running, execute stop processing
            if (currentServer?.IsRunning == true)
            {
                try
                {
                    LogServerStoppingBeforeDomainReload(correlationId);

                    // 4.2. Stop server
                    currentServer.Dispose();

                    LogServerStoppedAfterDomainReload(correlationId);

                    return ServiceResult<string>.SuccessResult(correlationId);
                }
                catch (System.Exception ex)
                {
                    LogServerShutdownError(correlationId, ex);
                    DomainReloadDetectionService.RollbackDomainReloadStart(correlationId);

                    // Server stop failure is a critical error because recovery must restart cleanly.
                    throw new System.InvalidOperationException(
                        "Failed to properly shutdown Unity CLI bridge before assembly reload.", ex);
                }
            }

            return ServiceResult<string>.SuccessResult(correlationId);
        }

        /// <summary>
        /// Execute recovery processing after Domain Reload completion
        /// </summary>
        /// <returns>Processing result</returns>
        public Task<ServiceResult<string>> ExecuteAfterDomainReloadAsync(CancellationToken cancellationToken = default)
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
                return Task.FromResult(ServiceResult<string>.FailureResult($"Server restoration failed: {restoreResult.ErrorMessage}"));
            }

            // 5. Process pending compile requests (currently disabled)
            ProcessPendingCompileRequests(correlationId);

            return Task.FromResult(ServiceResult<string>.SuccessResult(correlationId));
        }

        private static void LogServerStoppingBeforeDomainReload(string correlationId)
        {
            VibeLogger.LogInfo(
                "domain_reload_server_stopping",
                "Stopping Unity CLI bridge before domain reload",
                new { transport = "project_ipc" },
                correlationId
            );
        }

        private static void LogServerStoppedAfterDomainReload(string correlationId)
        {
            VibeLogger.LogInfo(
                "domain_reload_server_stopped",
                "Unity CLI bridge stopped successfully",
                new { transport = "project_ipc" },
                correlationId
            );
        }

        private static void LogServerShutdownError(string correlationId, System.Exception ex)
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
