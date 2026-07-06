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
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;
        private readonly UnityCliLoopCompileSessionLifecycleService _compileSessionLifecycleService;
        private readonly IUnityCliLoopEditorLegacySessionStateReader _legacySessionStateReader;

        internal DomainReloadDetectionFileService(
            ISessionFlagsRepository sessionFlagsRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository,
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService,
            IUnityCliLoopEditorLegacySessionStateReader legacySessionStateReader = null)
        {
            UnityEngine.Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            UnityEngine.Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");
            UnityEngine.Debug.Assert(compileSessionLifecycleService != null, "compileSessionLifecycleService must not be null");

            _sessionFlagsRepository = sessionFlagsRepository ??
                throw new ArgumentNullException(nameof(sessionFlagsRepository));
            _pendingCompileSessionRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
            _compileSessionLifecycleService = compileSessionLifecycleService ??
                throw new ArgumentNullException(nameof(compileSessionLifecycleService));
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

            UnityCliLoopPendingCompileRequest[] pendingCompileRequests =
                _pendingCompileSessionRepository.GetPendingCompileRequests();
            _compileSessionLifecycleService.MarkDomainReloadStarted(serverIsRunning);

            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(true);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_start",
                "Domain reload starting",
                new
                {
                    server_running = serverIsRunning,
                    pending_compile_request_count = pendingCompileRequests.Length,
                    pending_compile_request_ids = ToPendingCompileRequestIds(pendingCompileRequests)
                },
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
            bool serverWillRecover = !_sessionFlagsRepository.GetIsServerManuallyStopped();
            UnityCliLoopPendingCompileRequest[] pendingCompileRequests =
                _pendingCompileSessionRepository.GetPendingCompileRequests();

            // Clear Domain Reload completion flag
            _sessionFlagsRepository.ClearDomainReloadFlag();
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_complete",
                serverWillRecover
                    ? "Domain reload completed - starting server recovery process"
                    : "Domain reload completed - server was manually stopped before recovery",
                new
                {
                    transport = "project_ipc",
                    pending_compile_request_count = pendingCompileRequests.Length,
                    pending_compile_request_ids = ToPendingCompileRequestIds(pendingCompileRequests)
                },
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

            _sessionFlagsRepository.ClearDomainReloadRecoveryFlags();
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
            return _sessionFlagsRepository.GetShowReconnectingUI();
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
                _sessionFlagsRepository.MarkServerStarted();
            }

            if (legacySessionState.IsAfterCompile)
            {
                _sessionFlagsRepository.SetIsAfterCompile(true);
            }

            if (legacySessionState.IsDomainReloadInProgress)
            {
                _sessionFlagsRepository.SetIsDomainReloadInProgress(true);
            }

            if (legacySessionState.IsReconnecting)
            {
                _sessionFlagsRepository.SetIsReconnecting(true);
            }

            if (legacySessionState.ShowReconnectingUI)
            {
                _sessionFlagsRepository.SetShowReconnectingUI(true);
            }

            if (legacySessionState.ShowPostCompileReconnectingUI)
            {
                _sessionFlagsRepository.SetShowPostCompileReconnectingUI(true);
            }

            _legacySessionStateReader.Clear();
        }

        private static string[] ToPendingCompileRequestIds(
            UnityCliLoopPendingCompileRequest[] pendingCompileRequests)
        {
            UnityEngine.Debug.Assert(pendingCompileRequests != null, "pendingCompileRequests must not be null");

            string[] requestIds = new string[pendingCompileRequests.Length];
            for (int i = 0; i < pendingCompileRequests.Length; i++)
            {
                requestIds[i] = pendingCompileRequests[i].RequestId;
            }

            return requestIds;
        }
    }
}
