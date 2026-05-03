using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using UnityEditor;
using Newtonsoft.Json;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    // Related classes:
    // - McpBridgeServer: The bridge server instance that this class manages.
    // - McpEditorWindow: The UI for starting and stopping the server.
    // - AssemblyReloadEvents: Used to handle server state across domain reloads.
    /// <summary>
    /// Manages the Unity CLI bridge server state and restores it after assembly reload.
    /// </summary>
    [InitializeOnLoad]
    public static class McpServerController
    {
        private static McpBridgeServer mcpServer;
        private static readonly SemaphoreSlim StartupSemaphore = new SemaphoreSlim(1, 1);
        private static long startupProtectionUntilTicks = 0; // UTC ticks
        private static Task _currentRecoveryTask;

        private static bool IsBackgroundUnityProcess()
        {
            bool isAssetImportWorker = AssetDatabase.IsAssetImportWorkerProcess();
            return isAssetImportWorker;
        }

        /// <summary>
        /// The current Unity CLI bridge server instance.
        /// </summary>
        public static McpBridgeServer CurrentServer => mcpServer;

        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public static bool IsServerRunning => mcpServer?.IsRunning ?? false;

        internal static void RegisterRecoveredServer(McpBridgeServer server)
        {
            System.Diagnostics.Debug.Assert(server != null, "server must not be null");

            mcpServer = server;
            SaveRunningServerState();
        }

        /// <summary>
        /// Current recovery task. Can be awaited by other components to ensure recovery completes first.
        /// </summary>
        public static Task RecoveryTask => _currentRecoveryTask;

        static McpServerController()
        {
            InitializeOnLoad();
        }

        private static void InitializeOnLoad()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_controller_background_skip", "Skipping Unity CLI bridge controller initialization in background Unity process.");
                return;
            }

            // Register cleanup for when Unity exits.
            EditorApplication.quitting += OnEditorQuitting;

            // Processing before assembly reload.
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Processing after assembly reload.
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

            // Domain Reload disabled (Enter Play Mode Settings) causes static constructor re-entry
            McpBridgeServer.OnServerLoopExited -= OnServerLoopUnexpectedlyExited;
            McpBridgeServer.OnServerLoopExited += OnServerLoopUnexpectedlyExited;

            // Initialize connected tools monitoring service
            // Note: ConnectedToolsMonitoringService has [InitializeOnLoad] so it's automatically initialized
            // This comment ensures the service initialization order is documented

            // Recovery binds the project IPC endpoint and may touch config files, so keep it off the
            // synchronous InitializeOnLoad path while preserving automatic startup.
            ScheduleStartupRecovery(
                action => EditorApplication.delayCall += () => action(),
                RestoreServerStateIfNeeded);
        }

        internal static Task ScheduleStartupRecovery(
            Action<Action> scheduleDelayCall,
            Func<Task> restoreServerState)
        {
            Debug.Assert(scheduleDelayCall != null, "scheduleDelayCall must not be null");
            Debug.Assert(restoreServerState != null, "restoreServerState must not be null");

            TaskCompletionSource<bool> scheduledRecoveryCompletionSource = new TaskCompletionSource<bool>();
            _currentRecoveryTask = scheduledRecoveryCompletionSource.Task;

            scheduleDelayCall(() =>
            {
                Task restoreTask;
                try
                {
                    restoreTask = restoreServerState();
                }
                catch (Exception ex)
                {
                    CompleteScheduledStartupRecovery(Task.FromException(ex), scheduledRecoveryCompletionSource);
                    return;
                }

                if (restoreTask.IsCompleted)
                {
                    CompleteScheduledStartupRecovery(restoreTask, scheduledRecoveryCompletionSource);
                    return;
                }

                _ = restoreTask.ContinueWith(task =>
                {
                    CompleteScheduledStartupRecovery(task, scheduledRecoveryCompletionSource);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.FromCurrentSynchronizationContext());
            });

            return scheduledRecoveryCompletionSource.Task;
        }

        private static void CompleteScheduledStartupRecovery(
            Task restoreTask,
            TaskCompletionSource<bool> scheduledRecoveryCompletionSource)
        {
            if (ReferenceEquals(_currentRecoveryTask, scheduledRecoveryCompletionSource.Task))
            {
                _currentRecoveryTask = null;
            }

            if (restoreTask.IsCanceled)
            {
                scheduledRecoveryCompletionSource.SetCanceled();
                return;
            }

            if (restoreTask.IsFaulted)
            {
                VibeLogger.LogError("server_startup_restore_failed",
                    $"Failed to restore server: {restoreTask.Exception?.GetBaseException().Message}");
                scheduledRecoveryCompletionSource.SetException(restoreTask.Exception.GetBaseException());
                return;
            }

            scheduledRecoveryCompletionSource.SetResult(true);
        }

        public static async void StartServer()
        {
            await StartServerWithUseCaseAsync();
        }

        /// <summary>
        /// Starts the server using new UseCase implementation.
        /// </summary>
        private static async Task StartServerWithUseCaseAsync()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_start_ignored", "background_process");
                return;
            }

            // Signal server is starting for CLI detection
            string serverStartingLockToken = CreateOptionalServerStartingLock();

            bool startupLockReleasedByPrewarm = false;
            try
            {
                // Always stop the existing server first so the project IPC endpoint is released.
                if (mcpServer != null)
                {
                    await StopServerWithUseCaseAsync();
                }

                DynamicCodeStartupTelemetry.Reset();
                DynamicCodeForegroundWarmupState.Reset();

                // Execute initialization UseCase
                McpServerInitializationUseCase useCase = new();
                ServerInitializationSchema schema = new()
                {
                    PreserveStartupLockUntilExplicitRelease = true
                };
                System.Threading.CancellationToken cancellationToken = System.Threading.CancellationToken.None;

                var result = await useCase.ExecuteAsync(schema, cancellationToken);

                if (!result.Success)
                {
                    // Error message already handled by UseCase
                    UnityEngine.Debug.LogError($"Server startup failed: {result.Message}");
                    return;
                }

                // UseCase creates a new server instance, so we keep a reference here
                // for compatibility with existing code
                mcpServer = result.ServerInstance;

                DynamicCodeStartupTelemetry.MarkServerReady();
                CustomToolManager.WarmupRegistry();
                DynamicCodeServices.ResetServerScopedServices();
                IPrewarmDynamicCodeUseCase prewarmDynamicCodeUseCase =
                    await DynamicCodeServices.GetPrewarmDynamicCodeUseCaseAsync(serverStartingLockToken);
                prewarmDynamicCodeUseCase.Request();
                startupLockReleasedByPrewarm = true;
            }
            finally
            {
                if (!startupLockReleasedByPrewarm)
                {
                    ServerStartingLockService.DeleteOwnedLockFile(serverStartingLockToken);
                }
            }
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public static async void StopServer()
        {
            await StopServerWithUseCaseAsync();
        }

        /// <summary>
        /// Stops the server using new UseCase implementation.
        /// </summary>
        private static async Task StopServerWithUseCaseAsync()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_stop_ignored", "background_process");
                return;
            }

            ClearStartupProtection();

            // Execute shutdown UseCase
            McpServerShutdownUseCase useCase = new(new McpServerStartupService());
            ServerShutdownSchema schema = new() { ForceShutdown = false };
            System.Threading.CancellationToken cancellationToken = System.Threading.CancellationToken.None;

            var result = await useCase.ExecuteAsync(schema, cancellationToken);

            if (result.Success)
            {
                // Server stopped by UseCase, so clear the reference
                mcpServer = null;

                // Clear session state to reflect server stopped
                McpEditorSettings.ClearServerSession();
                DynamicCodeStartupTelemetry.Reset();
                DynamicCodeForegroundWarmupState.Reset();
                DynamicCodeServices.ResetServerScopedServices();
            }
            else
            {
                // Error message already handled by UseCase
                UnityEngine.Debug.LogError($"Server shutdown failed: {result.Message}");
            }
        }

        /// <summary>
        /// Processing before assembly reload.
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            ClearStartupProtection();

            // Create and execute DomainReloadRecoveryUseCase instance
            DomainReloadRecoveryUseCase useCase = new();
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(mcpServer);
            
            // Clear instance if server shutdown succeeded
            if (result.Success)
            {
                mcpServer = null;
            }

            DynamicCodeStartupTelemetry.Reset();
            DynamicCodeForegroundWarmupState.Reset();
            DynamicCodeServices.ResetServerScopedServices();
        }

        /// <summary>
        /// Processing after assembly reload.
        /// </summary>
        private static void OnAfterAssemblyReload()
        {
            // Create and execute DomainReloadRecoveryUseCase instance
            DomainReloadRecoveryUseCase useCase = new();
            _ = useCase.ExecuteAfterDomainReloadAsync(System.Threading.CancellationToken.None).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    UnityEngine.Debug.LogError($"Domain reload recovery failed: {task.Exception}");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());

        }

        /// <summary>
        /// Restores the server state if necessary.
        /// </summary>
        private static Task RestoreServerStateIfNeeded()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_restore_skipped", "background_process");
                return Task.CompletedTask;
            }

            bool isAfterCompile = McpEditorSettings.GetIsAfterCompile();

            if (mcpServer?.IsRunning == true)
            {
                if (isAfterCompile)
                {
                    McpEditorSettings.ClearAfterCompileFlag();
                }

                return Task.CompletedTask;
            }

            if (isAfterCompile)
            {
                McpEditorSettings.ClearAfterCompileFlag();
            }

            // Centralized, coalesced startup request
            // Store the task so McpEditorWindow can await it to prevent race conditions
            return StartRecoveryIfNeededAsync(isAfterCompile, CancellationToken.None);
        }

        private static void TryRestoreServerWithRetry(int retryCount)
        {
            const int maxRetries = 3;

            try
            {
                // If there is an existing server instance, ensure it is stopped.
                if (mcpServer != null)
                {
                    mcpServer.Dispose();
                    mcpServer = null;
                }

                mcpServer = new McpBridgeServer();
                mcpServer.StartServer();
                SaveRunningServerState();

                // Clear server-side reconnecting flag on successful restoration
                // NOTE: Do NOT clear UI display flag here - let it be cleared by timeout or client connection
                McpEditorSettings.SetIsReconnecting(false);

                // Tools changed notification will be sent by OnAfterAssemblyReload
            }
            catch (System.Exception)
            {
                // If the maximum number of retries has not been reached, try again.
                if (retryCount < maxRetries)
                {
                    RetryServerRestoreAsync(retryCount).Forget();
                }
                else
                {
                    // If it ultimately fails, clear the SessionState.
                    McpEditorSettings.ClearServerSession();
                }
            }
        }

        /// <summary>
        /// Prevent CLI from misdetecting a busy state when server startup is intentionally skipped.
        /// </summary>
        private static void DeleteAllLockFiles()
        {
            CompilationLockService.DeleteLockFile();
            DomainReloadDetectionService.DeleteLockFile();
        }

        /// <summary>
        /// Cleanup on Unity exit.
        /// Disposes the bridge listener and marks the server as stopped so the CLI
        /// does not attempt to connect to a stale IPC endpoint after the editor closes.
        /// </summary>
        private static void OnEditorQuitting()
        {
            if (mcpServer != null)
            {
                try
                {
                    mcpServer.Dispose();
                }
                finally
                {
                    mcpServer = null;
                }
            }
            DynamicCodeForegroundWarmupState.Reset();
            DynamicCodeServices.ResetServerScopedServices();
            McpEditorSettings.ClearServerSession();
        }

        /// <summary>
        /// OnServerLoopExited fires from the thread pool, but Unity APIs (EditorSettings,
        /// VibeLogger with SerializedObject, etc.) are main-thread-only.
        /// EditorApplication.delayCall marshals the recovery to the next editor tick.
        /// </summary>
        private static void OnServerLoopUnexpectedlyExited()
        {
            // OnServerLoopExited fires from thread pool — marshal to main thread for Unity API safety
            EditorApplication.delayCall += () =>
            {
                ClearStartupProtection();

                VibeLogger.LogWarning(
                    "server_loop_exit_detected",
                    "Detected unexpected server loop exit. Initiating automatic recovery.",
                    new { transport = "project_ipc" }
                );

                // Resources already cleaned up by CleanupAfterUnexpectedLoopExit — just clear the reference
                mcpServer = null;

                // The server just crashed — startup protection blocks recovery if the crash happens
                // within the 5-second protection window after a successful start
                System.Threading.Volatile.Write(ref startupProtectionUntilTicks, 0L);

                _currentRecoveryTask = StartRecoveryIfNeededAsync(false, CancellationToken.None);
                _ = _currentRecoveryTask.ContinueWith(task =>
                {
                    if (ReferenceEquals(_currentRecoveryTask, task))
                    {
                        _currentRecoveryTask = null;
                    }
                    if (task.IsFaulted)
                    {
                        VibeLogger.LogError(
                            "server_auto_recovery_failed",
                            $"Automatic recovery after unexpected exit failed: {task.Exception?.GetBaseException().Message}"
                        );
                        McpEditorSettings.ClearServerSession();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            };
        }

        /// <summary>
        /// Processes pending compile requests.
        /// </summary>
        private static void ProcessPendingCompileRequests()
        {
            // Temporarily disabled to avoid main thread errors due to SessionState operations.
            // TODO: Re-enable after resolving the main thread issue.
            // CompileSessionState.StartForcedRecompile();
        }

        /// <summary>
        /// Send tools changed notification to client side
        /// </summary>
        private static void SendToolsChangedNotification()
        {
            // Log with stack trace to identify caller
            System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(true);
            string callerInfo = stackTrace.GetFrame(1)?.GetMethod()?.Name ?? "Unknown";

            if (mcpServer == null)
            {
                return;
            }

            // Send MCP standard notification only
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
            mcpServer.SendNotificationToClients(mcpNotificationJson);
        }

        /// <summary>
        /// Manually trigger tool change notification
        /// Public method for external calls (e.g., from UnityToolRegistry)
        /// </summary>
        public static void TriggerToolChangeNotification()
        {
            if (IsServerRunning)
            {
                SendToolsChangedNotification();
            }
        }

        /// <summary>
        /// Send tool notification after compilation with frame delay
        /// </summary>
        private static async Task SendToolNotificationAfterCompilationAsync()
        {
            // Use frame delay for timing adjustment after domain reload
            // This ensures Unity Editor is in a stable state before sending notifications
            await EditorDelay.DelayFrame(1);

            CustomToolManager.NotifyToolChanges();
        }

        /// <summary>
        /// Restore server after compilation with frame delay.
        /// Currently kept as a helper; recovery logic is unified in StartRecoveryIfNeededAsync.
        /// </summary>
        private static async Task RestoreServerAfterCompileAsync()
        {
            // Wait a short while for Unity editor state to settle after compilation.
            await EditorDelay.DelayFrame(1);

            TryRestoreServerWithRetry(0);
        }

        /// <summary>
        /// Restore server on startup with frame delay
        /// </summary>
        private static async Task RestoreServerOnStartupAsync()
        {
            // Wait for Unity Editor to be ready before auto-starting
            await EditorDelay.DelayFrame(1);
            _ = StartRecoveryIfNeededAsync(false, CancellationToken.None).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    VibeLogger.LogError("server_startup_restore_failed",
                        $"Failed to restore server: {task.Exception?.GetBaseException().Message}");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Retry server restore with frame delay on the same project IPC endpoint.
        /// </summary>
        private static async Task RetryServerRestoreAsync(int retryCount)
        {
            await EditorDelay.DelayFrame(5);
            TryRestoreServerWithRetry(retryCount + 1);
        }

        /// <summary>
        /// Start UI display timeout timer for reconnecting message
        /// </summary>
        private static async Task StartReconnectionUITimeoutAsync()
        {
            // Wait for the timeout period (convert seconds to frames at ~60fps)
            int timeoutFrames = McpConstants.RECONNECTION_TIMEOUT_SECONDS * 60;
            await EditorDelay.DelayFrame(timeoutFrames);

            // Check if UI flag is still set after timeout
            bool isStillShowingUI = McpEditorSettings.GetShowReconnectingUI();
            if (isStillShowingUI)
            {
                McpEditorSettings.ClearReconnectingFlags();
            }
        }

        /// <summary>
        /// Clear reconnecting flags when client connects
        /// Called by UI or bridge server when client connection is detected
        /// </summary>
        public static void ClearReconnectingFlag()
        {
            bool wasReconnecting = McpEditorSettings.GetIsReconnecting();
            bool wasShowingUI = McpEditorSettings.GetShowReconnectingUI();

            if (wasReconnecting || wasShowingUI)
            {
                McpEditorSettings.ClearReconnectingFlags();
            }
        }

        /// <summary>
        /// Validates server configuration before starting
        /// Implements fail-fast behavior for invalid configurations
        /// </summary>
        private static void ValidateServerConfiguration()
        {
            // Validate Unity Editor state
            if (EditorApplication.isCompiling)
            {
                throw new System.InvalidOperationException(
                    "Cannot start Unity CLI bridge while Unity is compiling. Please wait for compilation to complete.");
            }

        }

        public static bool IsStartupProtectionActive()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            return nowTicks < System.Threading.Volatile.Read(ref startupProtectionUntilTicks);
        }

        private static void ActivateStartupProtection(int milliseconds)
        {
            long untilTicks = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
            System.Threading.Volatile.Write(ref startupProtectionUntilTicks, untilTicks);
            VibeLogger.LogInfo("startup_protection_active", $"window={milliseconds}ms");
        }

        /// <summary>
        /// Clears startup protection so recovery paths can restart the server immediately.
        /// </summary>
        private static void ClearStartupProtection()
        {
            System.Threading.Volatile.Write(ref startupProtectionUntilTicks, 0L);
        }

        /// <summary>
        /// Centralized, coalesced recovery start.
        /// Attempts recovery on the project IPC endpoint for up to 5 seconds.
        /// </summary>
        public static async Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken cancellationToken)
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_start_ignored", "background_process");
                return;
            }

            // Ensure stale reload locks are cleaned up before recovery.
            // Why not clear serverstarting.lock here: a previous generation may still be finishing
            // and ownership is now tracked per startup token below.
            DomainReloadDetectionService.DeleteLockFile();
            CompilationLockService.DeleteLockFile();

            VibeLogger.LogInfo("startup_request", "transport=project_ipc");

            if (IsStartupProtectionActive())
            {
                VibeLogger.LogInfo("server_start_ignored", "startup_protection_active");
                return;
            }

            await StartupSemaphore.WaitAsync(cancellationToken);
            string serverStartingLockToken = null;
            try
            {
                // If any server is already running, ignore this request to prevent double-binding
                if (mcpServer != null && mcpServer.IsRunning)
                {
                    VibeLogger.LogInfo("server_start_ignored", $"already_running endpoint={mcpServer.Endpoint}");
                    return;
                }

                serverStartingLockToken = CreateOptionalServerStartingLock();

                // Ensure previous instance is fully disposed before trying to bind a new one
                if (mcpServer != null)
                {
                    try
                    {
                        mcpServer.Dispose();
                        VibeLogger.LogInfo("server_disposed_before_bind", "disposed previous server instance");
                    }
                    catch (Exception ex)
                    {
                        VibeLogger.LogWarning("server_dispose_failed", ex.Message);
                    }
                    finally
                    {
                        mcpServer = null;
                    }
                }

                bool started = await TryBindWithWaitAsync(
                    5000,
                    250,
                    cancellationToken,
                    clearServerStartingLockWhenReady: false);

                if (!started)
                {
                    // Ensure session reflects stopped state on failure
                    McpEditorSettings.ClearServerSession();
                    McpEditorSettings.ClearReconnectingFlags();
                    Debug.LogError($"[{McpConstants.PROJECT_NAME}] Recovery failed: no project IPC endpoint to bind.");
                    throw new InvalidOperationException("Failed to bind recovery endpoint.");
                }

                // Mark running and update settings
                SaveRunningServerState();

                // Clear reconnection-related flags on successful recovery
                McpEditorSettings.ClearReconnectingFlags();
                McpEditorSettings.ClearPostCompileReconnectingUI();
                DynamicCodeStartupTelemetry.MarkServerReady();
                CustomToolManager.WarmupRegistry();
                DynamicCodeServices.ResetServerScopedServices();
                IPrewarmDynamicCodeUseCase prewarmDynamicCodeUseCase =
                    await DynamicCodeServices.GetPrewarmDynamicCodeUseCaseAsync(serverStartingLockToken);
                prewarmDynamicCodeUseCase.Request();

                ActivateStartupProtection(5000);
            }
            catch
            {
                ServerStartingLockService.DeleteOwnedLockFile(serverStartingLockToken);
                throw;
            }
            finally
            {
                StartupSemaphore.Release();
            }
        }

        private static async Task<bool> TryBindWithWaitAsync(
            int maxWaitMs,
            int stepMs,
            CancellationToken cancellationToken,
            bool clearServerStartingLockWhenReady = true)
        {
            int remainingMs = maxWaitMs;
            while (true)
            {
                VibeLogger.LogInfo("binding_attempt", "transport=project_ipc");
                McpBridgeServer server = null;
                try
                {
                    // Defensive: dispose any non-running stale instance before creating a new one
                    if (mcpServer != null && !mcpServer.IsRunning)
                    {
                        try
                        {
                            mcpServer.Dispose();
                            VibeLogger.LogInfo("server_disposed_before_bind", "disposed stale instance");
                        }
                        catch (Exception ex)
                        {
                            VibeLogger.LogWarning("server_dispose_failed", ex.Message);
                        }
                        finally
                        {
                            mcpServer = null;
                        }
                    }

                    server = new McpBridgeServer();
                    server.StartServer(clearServerStartingLockWhenReady);
                    mcpServer = server;
                    VibeLogger.LogInfo("binding_success", $"endpoint={server.Endpoint}");
                    return true;
                }
                catch (Exception ex)
                {
                    // Ensure partially created server is cleaned up on failure
                    try { server?.Dispose(); } catch { }
                    // Unwrap SocketException details if present
                    SocketException sockEx = ex as SocketException;
                    if (ex is InvalidOperationException && ex.InnerException is SocketException innerSock)
                    {
                        sockEx = innerSock;
                    }

                    if (sockEx != null)
                    {
                        VibeLogger.LogWarning("binding_failed", $"target=project_ipc code={sockEx.SocketErrorCode} hresult={sockEx.HResult} native={sockEx.ErrorCode}");
                    }
                    else
                    {
                        VibeLogger.LogWarning("binding_failed", $"target=project_ipc code=Unknown hresult={ex.HResult}");
                    }

                    if (remainingMs <= 0)
                    {
                        return false;
                    }

                    int delay = stepMs <= 0 ? remainingMs : Math.Min(stepMs, remainingMs);
                    await TimerDelay.Wait(delay, cancellationToken);
                    remainingMs -= delay;
                }
            }
        }

        private static void SaveRunningServerState()
        {
            McpEditorSettings.SetIsServerRunning(true);
        }

        internal static string CreateOptionalServerStartingLock(Func<string> createLockFile = null)
        {
            Func<string> createLockFileCore = createLockFile ?? ServerStartingLockService.CreateLockFile;
            string serverStartingLockToken = createLockFileCore();
            if (!string.IsNullOrEmpty(serverStartingLockToken))
            {
                return serverStartingLockToken;
            }

            // Why: serverstarting.lock only improves busy diagnostics for external callers; the
            // listener itself can still start and recover safely without it.
            // Why not fail fast here: a transient file lock would otherwise turn an optional
            // readiness hint into a full startup outage for launch and recovery paths.
            VibeLogger.LogWarning(
                "server_starting_lock_optional",
                "Proceeding without serverstarting.lock because the readiness hint could not be created.");
            return null;
        }
    }
}
