using System.IO;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Application service responsible for Domain Reload detection and state management
    /// Single responsibility: Domain Reload lifecycle management
    /// Related classes: McpSessionManager, McpServerController
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    [InitializeOnLoad]
    public static class DomainReloadDetectionService
    {
        private static bool IsBackgroundUnityProcess()
        {
            bool isAssetImportWorker = AssetDatabase.IsAssetImportWorkerProcess();
            return isAssetImportWorker;
        }

        static DomainReloadDetectionService()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_hook_skip", "Skipping domain reload hooks in background Unity process.");
                return;
            }

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
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
            // Lock file is deleted by McpBridgeServer when server startup completes
            // to avoid a gap between domain reload end and server ready
        }

        private const string LOCK_FILE_NAME = "domainreload.lock";

        private static string LockFilePath => Path.Combine(UnityEngine.Application.dataPath, "..", "Temp", LOCK_FILE_NAME);

        /// <summary>
        /// Execute Domain Reload start processing
        /// </summary>
        /// <param name="correlationId">Tracking ID for related operations</param>
        /// <param name="serverIsRunning">Whether server is running</param>
        public static void StartDomainReload(string correlationId, bool serverIsRunning)
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
                McpEditorSettings.UpdateSettings(s =>
                {
                    McpEditorSettingsData updatedSettings = s with
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
                McpEditorSettings.SetIsDomainReloadInProgress(true);
            }

            McpEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(true);

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
        public static void CompleteDomainReload(string correlationId)
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_complete_ignored", "background_process", correlationId: correlationId);
                return;
            }

            // Lock file is deleted by McpBridgeServer when server startup completes
            // to avoid a gap between domain reload completion and server ready

            // Clear Domain Reload completion flag
            McpEditorSettings.ClearDomainReloadFlag();
            McpEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);

            // Log recording
            VibeLogger.LogInfo(
                "domain_reload_complete",
                "Domain reload completed - starting server recovery process",
                new { transport = "project_ipc" },
                correlationId
            );
        }

        internal static void RollbackDomainReloadStart(string correlationId)
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("domain_reload_rollback_ignored", "background_process", correlationId: correlationId);
                return;
            }

            McpEditorSettings.UpdateSettings(s => s with
            {
                isDomainReloadInProgress = false,
                isAfterCompile = false,
                isReconnecting = false,
                showReconnectingUI = false,
                showPostCompileReconnectingUI = false
            });
            McpEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
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
        public static bool IsDomainReloadInProgress()
        {
            return McpEditorSettings.GetIsDomainReloadInProgress();
        }

        /// <summary>
        /// Check if reconnection UI display is required
        /// </summary>
        /// <returns>True if reconnection UI display is required</returns>
        public static bool ShouldShowReconnectingUI()
        {
            return McpEditorSettings.GetShowReconnectingUI();
        }

        /// <summary>
        /// Check if in after-compile state
        /// </summary>
        /// <returns>True if after compile</returns>
        public static bool IsAfterCompile()
        {
            return McpEditorSettings.GetIsAfterCompile();
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
        public static void DeleteLockFile()
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
        public static bool IsLockFilePresent()
        {
            return File.Exists(LockFilePath);
        }
    }
}
