using System;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Infrastructure implementation that persists Domain Reload readiness state through server state and editor settings.
    /// </summary>
    public sealed class DomainReloadDetectionFileService : IDomainReloadDetectionService
    {
        private readonly UnityCliLoopEditorSettingsService _editorSettingsService;
        private readonly ServerReadinessStateStore _stateStore;

        public DomainReloadDetectionFileService()
            : this(new UnityCliLoopEditorSettingsService(new UnityCliLoopEditorSettingsRepository()))
        {
        }

        internal DomainReloadDetectionFileService(
            UnityCliLoopEditorSettingsService editorSettingsService,
            ServerReadinessStateStore stateStore = null)
        {
            UnityEngine.Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");

            _editorSettingsService = editorSettingsService ?? throw new ArgumentNullException(nameof(editorSettingsService));
            _stateStore = stateStore ?? new ServerReadinessStateStore(UnityCliLoopPathResolver.GetProjectRoot());
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
                return;
            }

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private void OnBeforeAssemblyReload()
        {
            if (IsBackgroundUnityProcess())
            {
                return;
            }

            _stateStore.Write(
                ServerReadinessPhase.Reloading,
                ServerReadinessStateStore.CreateGenerationId(),
                "assembly-reload-before",
                null,
                null);
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

            _stateStore.Write(
                ServerReadinessPhase.Reloading,
                correlationId,
                "domain-reload-before",
                null,
                null);

            // Save session state if server is running
            if (serverIsRunning)
            {
                _editorSettingsService.UpdateSettings(s =>
                {
                    UnityCliLoopEditorSettingsData updatedSettings = s with
                    {
                        isDomainReloadInProgress = true,
                        isServerRunning = true,
                        isAfterCompile = true,
                        isReconnecting = true,
                        showReconnectingUI = true,
                        showPostCompileReconnectingUI = true
                    };

                    return updatedSettings;
                });
            }
            else
            {
                _editorSettingsService.SetIsDomainReloadInProgress(true);
            }

            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(true);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_start",
                "Domain reload starting",
                new
                {
                    server_running = serverIsRunning
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

            bool serverWillRecover = _editorSettingsService.GetIsServerRunning();

            _stateStore.Write(
                serverWillRecover ? ServerReadinessPhase.Recovering : ServerReadinessPhase.Stopped,
                correlationId,
                serverWillRecover ? "domain-reload-after" : "domain-reload-after-no-server",
                null,
                null);

            // Clear Domain Reload completion flag
            _editorSettingsService.ClearDomainReloadFlag();
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_complete",
                "Domain reload completed - starting server recovery process",
                new { transport = "project_ipc" },
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

            _editorSettingsService.UpdateSettings(s => s with
            {
                isDomainReloadInProgress = false,
                isAfterCompile = false,
                isReconnecting = false,
                showReconnectingUI = false,
                showPostCompileReconnectingUI = false
            });
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            _stateStore.Write(
                ServerReadinessPhase.Failed,
                correlationId,
                "domain-reload-rollback",
                null,
                "Failed to stop the server before domain reload.");

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
            return _editorSettingsService.GetShowReconnectingUI();
        }
    }
}
