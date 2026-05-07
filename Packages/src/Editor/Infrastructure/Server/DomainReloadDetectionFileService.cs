using System.IO;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Application service responsible for Domain Reload detection and state management
    /// Single responsibility: Domain Reload lifecycle management
    /// Related classes: UnityCliLoopEditorSettings, UnityCliLoopServerController
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public sealed class DomainReloadDetectionFileService : IDomainReloadDetectionService
    {
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
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            if (IsBackgroundUnityProcess())
            {
                return;
            }

            CreateLockFile();
        }

        private static void OnAfterAssemblyReload()
        {
            // Lock file is deleted when server startup completes.
            // to avoid a gap between domain reload end and server ready
        }

        private const string LOCK_FILE_NAME = "domainreload.lock";

        private static string LockFilePath => Path.Combine(UnityEngine.Application.dataPath, "..", "Temp", LOCK_FILE_NAME);

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

            // Create lock file for external process detection (e.g., CLI tools)
            CreateLockFile();

            // Save session state if server is running
            if (serverIsRunning)
            {
                UnityCliLoopEditorSettings.UpdateSettings(s =>
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
                UnityCliLoopEditorSettings.SetIsDomainReloadInProgress(true);
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

            // Lock file is deleted when server startup completes.
            // to avoid a gap between domain reload completion and server ready

            // Clear Domain Reload completion flag
            UnityCliLoopEditorSettings.ClearDomainReloadFlag();
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

            UnityCliLoopEditorSettings.UpdateSettings(s => s with
            {
                isDomainReloadInProgress = false,
                isAfterCompile = false,
                isReconnecting = false,
                showReconnectingUI = false,
                showPostCompileReconnectingUI = false
            });
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            DeleteLockFile();

            VibeLogger.LogWarning(
                "domain_reload_start_rollback",
                "Rolled back domain reload start state after pre-reload failure.",
                correlationId: correlationId
            );
        }

        /// <summary>
        /// Check if currently in Domain Reload
        /// </summary>
        /// <returns>True if Domain Reload is in progress</returns>
        public bool IsDomainReloadInProgress()
        {
            return UnityCliLoopEditorSettings.GetIsDomainReloadInProgress();
        }

        /// <summary>
        /// Check if reconnection UI display is required
        /// </summary>
        /// <returns>True if reconnection UI display is required</returns>
        public bool ShouldShowReconnectingUI()
        {
            return UnityCliLoopEditorSettings.GetShowReconnectingUI();
        }

        /// <summary>
        /// Check if in after-compile state
        /// </summary>
        /// <returns>True if after compile</returns>
        public bool IsAfterCompile()
        {
            return UnityCliLoopEditorSettings.GetIsAfterCompile();
        }

        private static void CreateLockFile()
        {
            string lockPath = LockFilePath;
            string tempDir = Path.GetDirectoryName(lockPath);

            if (!Directory.Exists(tempDir))
            {
                return;
            }

            File.WriteAllText(lockPath, System.DateTime.UtcNow.ToString("o"));
        }

        /// <summary>
        /// Delete lock file to signal Domain Reload completion.
        /// </summary>
        public void DeleteLockFile()
        {
            string lockPath = LockFilePath;
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }

        /// <summary>
        /// Check if Domain Reload lock file exists.
        /// Used by external processes to detect Domain Reload state.
        /// </summary>
        /// <returns>True if lock file exists</returns>
        public bool IsLockFilePresent()
        {
            return File.Exists(LockFilePath);
        }
    }
}
