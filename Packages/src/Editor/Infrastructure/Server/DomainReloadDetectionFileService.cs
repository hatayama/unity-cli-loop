using System;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Infrastructure implementation that persists Domain Reload recovery state through Editor SessionState.
    /// </summary>
    public sealed class DomainReloadDetectionFileService : IDomainReloadDetectionService
    {
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;
        private readonly IUnityCliLoopEditorLegacySessionStateReader _legacySessionStateReader;

        public DomainReloadDetectionFileService()
            : this(new UnityCliLoopEditorSessionStateService(new UnityCliLoopEditorSessionStateRepository()))
        {
        }

        internal DomainReloadDetectionFileService(
            UnityCliLoopEditorSessionStateService sessionStateService,
            IUnityCliLoopEditorLegacySessionStateReader legacySessionStateReader = null)
        {
            UnityEngine.Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            _sessionStateService = sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));
            _legacySessionStateReader =
                legacySessionStateReader ?? new UnityCliLoopEditorLegacySessionStateReader();
        }

        private static bool IsBackgroundUnityProcess()
        {
            bool isAssetImportWorker = AssetDatabase.IsAssetImportWorkerProcess();
            return isAssetImportWorker;
        }

        public void RegisterForEditorStartup()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_hook_skip", "Skipping domain reload hooks in background Unity process.");
            }
        }

        /// <summary>
        /// Execute Domain Reload start processing
        /// </summary>
        /// <param name="correlationId">Tracking ID for related operations</param>
        /// <param name="serverIsRunning">Whether server is running</param>
        public void StartDomainReload(string correlationId, bool serverIsRunning)
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_start_ignored", "background_process", correlationId: correlationId);
                return;
            }

            _sessionStateService.MarkDomainReloadStarted(serverIsRunning);

            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(true);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_start",
                "Domain reload starting",
                BuildDomainReloadStartContext(serverIsRunning),
                correlationId
            );
        }

        /// <summary>
        /// Execute Domain Reload completion processing
        /// </summary>
        /// <param name="correlationId">Tracking ID for related operations</param>
        public void CompleteDomainReload(string correlationId)
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_complete_ignored", "background_process", correlationId: correlationId);
                return;
            }

            MigrateLegacySessionStateIfNeeded();
            bool serverWillRecover = !_sessionStateService.GetIsServerManuallyStopped();

            // Clear Domain Reload completion flag
            _sessionStateService.ClearDomainReloadFlag();
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_complete",
                serverWillRecover
                    ? "Domain reload completed - starting server recovery process"
                    : "Domain reload completed - server was manually stopped before recovery",
                BuildDomainReloadCompleteContext(serverWillRecover),
                correlationId
            );
        }

        public void RollbackDomainReloadStart(string correlationId)
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_rollback_ignored", "background_process", correlationId: correlationId);
                return;
            }

            _sessionStateService.ClearDomainReloadRecoveryFlags();
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);

            VibeLogger.LogWarning(
                "domain_reload_start_rollback",
                "Rolled back domain reload start state after pre-reload failure.",
                correlationId: correlationId
            );
        }

        /// <summary>
        /// Check if reconnection UI display is required
        /// </summary>
        /// <returns>True if reconnection UI display is required</returns>
        public bool ShouldShowReconnectingUI()
        {
            return _sessionStateService.GetShowReconnectingUI();
        }

        private void MigrateLegacySessionStateIfNeeded()
        {
            UnityCliLoopEditorLegacySessionState legacySessionState = _legacySessionStateReader.Read();
            if (!legacySessionState.HasDomainReloadRecoveryState)
            {
                return;
            }

            if (legacySessionState.IsServerRunning)
            {
                _sessionStateService.MarkServerStarted();
            }

            if (legacySessionState.IsAfterCompile)
            {
                _sessionStateService.SetIsAfterCompile(true);
            }

            if (legacySessionState.IsDomainReloadInProgress)
            {
                _sessionStateService.SetIsDomainReloadInProgress(true);
            }

            if (legacySessionState.IsReconnecting)
            {
                _sessionStateService.SetIsReconnecting(true);
            }

            if (legacySessionState.ShowReconnectingUI)
            {
                _sessionStateService.SetShowReconnectingUI(true);
            }

            if (legacySessionState.ShowPostCompileReconnectingUI)
            {
                _sessionStateService.SetShowPostCompileReconnectingUI(true);
            }

            _legacySessionStateReader.Clear();
        }

        private object BuildDomainReloadStartContext(bool serverIsRunning)
        {
            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            return new
            {
                transport = "project_ipc",
                server_running = serverIsRunning,
                session_server_running = _sessionStateService.GetIsServerRunning(),
                session_domain_reload_in_progress = _sessionStateService.GetIsDomainReloadInProgress(),
                pending_compile_request = pendingCompileRequest.HasRequest,
                pending_compile_request_id = pendingCompileRequest.RequestId,
                pending_compile_force_recompile = pendingCompileRequest.ForceRecompile,
                pending_compile_expires_at_utc_ticks = pendingCompileRequest.ExpiresAtUtcTicks
            };
        }

        private object BuildDomainReloadCompleteContext(bool serverWillRecover)
        {
            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            return new
            {
                transport = "project_ipc",
                server_will_recover = serverWillRecover,
                session_server_running = _sessionStateService.GetIsServerRunning(),
                session_domain_reload_in_progress = _sessionStateService.GetIsDomainReloadInProgress(),
                session_reconnecting = _sessionStateService.GetIsReconnecting(),
                pending_compile_request = pendingCompileRequest.HasRequest,
                pending_compile_request_id = pendingCompileRequest.RequestId,
                pending_compile_force_recompile = pendingCompileRequest.ForceRecompile,
                pending_compile_expires_at_utc_ticks = pendingCompileRequest.ExpiresAtUtcTicks
            };
        }
    }
}
