using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Manages the Unity CLI bridge server state and restores it after assembly reload.
    /// </summary>
    public sealed class UnityCliLoopServerControllerService :
        IUnityCliLoopServerController,
        IUnityCliLoopServerRecoveryCoordinator,
        IUnityCliLoopServerStateReader
    {
        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly UnityCliLoopServerLifecycleRegistryService _serverLifecycleRegistry;
        private readonly SessionRecoveryService _sessionRecoveryService;
        private IUnityCliLoopServerInstance _bridgeServer;
        private readonly SemaphoreSlim _startupSemaphore = new SemaphoreSlim(1, 1);
        private long _startupProtectionUntilTicks = 0;
        private Task _currentRecoveryTask;

        internal UnityCliLoopServerControllerService(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            UnityCliLoopServerLifecycleRegistryService serverLifecycleRegistry)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(serverLifecycleRegistry != null, "serverLifecycleRegistry must not be null");

            _serverInstanceFactory = serverInstanceFactory ?? throw new ArgumentNullException(nameof(serverInstanceFactory));
            _serverLifecycleRegistry = serverLifecycleRegistry ?? throw new ArgumentNullException(nameof(serverLifecycleRegistry));
            _sessionRecoveryService = new SessionRecoveryService(this);
        }

        private bool IsBackgroundUnityProcess()
        {
            bool isAssetImportWorker = AssetDatabase.IsAssetImportWorkerProcess();
            return isAssetImportWorker;
        }

        /// <summary>
        /// The current Unity CLI bridge server instance.
        /// </summary>
        public IUnityCliLoopServerInstance CurrentServer => _bridgeServer;

        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public bool IsServerRunning => _bridgeServer?.IsRunning ?? false;

        internal void RegisterRecoveredServer(IUnityCliLoopServerInstance server)
        {
            System.Diagnostics.Debug.Assert(server != null, "server must not be null");

            _bridgeServer = server;
            SaveRunningServerState();
        }

        /// <summary>
        /// Current recovery task. Can be awaited by other components to ensure recovery completes first.
        /// </summary>
        public Task RecoveryTask => _currentRecoveryTask;

        public void InitializeForEditorStartup()
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
            _serverLifecycleRegistry.ServerLoopExited -= OnServerLoopUnexpectedlyExited;
            _serverLifecycleRegistry.ServerLoopExited += OnServerLoopUnexpectedlyExited;

            // Recovery binds the project IPC endpoint and may touch config files, so keep it off the
            // synchronous Editor startup path while preserving automatic startup.
            ScheduleStartupRecovery(
                action => EditorApplication.delayCall += () => action(),
                RestoreServerStateIfNeeded);
        }

        internal Task ScheduleStartupRecovery(
            Action<Action> scheduleDelayCall,
            Func<Task> restoreServerState)
        {
            Debug.Assert(scheduleDelayCall != null, "scheduleDelayCall must not be null");
            Debug.Assert(restoreServerState != null, "restoreServerState must not be null");

            TaskCompletionSource<bool> scheduledRecoveryCompletionSource = new();
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

        private void CompleteScheduledStartupRecovery(
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

        public async void StartServer()
        {
            await StartServerWithUseCaseAsync();
        }

        /// <summary>
        /// Starts the server using new UseCase implementation.
        /// </summary>
        private async Task StartServerWithUseCaseAsync()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_start_ignored", "background_process");
                return;
            }

            // Always stop the existing server first so the project IPC endpoint is released.
            if (_bridgeServer != null)
            {
                await StopServerWithUseCaseAsync();
            }

            UnityCliLoopServerStartupService startupService =
                new UnityCliLoopServerStartupService(_serverInstanceFactory);
            UnityCliLoopServerInitializationUseCase useCase =
                new UnityCliLoopServerInitializationUseCase(
                    new EditorSecurityValidationService(),
                    startupService);
            ServerInitializationSchema schema = new()
            {
                PreserveStartupLockUntilExplicitRelease = false
            };
            System.Threading.CancellationToken cancellationToken = System.Threading.CancellationToken.None;

            ServerInitializationResponse result = await useCase.ExecuteAsync(schema, cancellationToken);

            if (!result.Success)
            {
                // Error message already handled by UseCase
                UnityEngine.Debug.LogError($"Server startup failed: {result.Message}");
                return;
            }

            // UseCase creates a new server instance, so we keep a reference here
            // for compatibility with existing code
            _bridgeServer = result.ServerInstance;

            UnityCliLoopToolRegistrar.WarmupRegistry();
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public async void StopServer()
        {
            await StopServerWithUseCaseAsync();
        }

        /// <summary>
        /// Stops the server using new UseCase implementation.
        /// </summary>
        internal async Task StopServerWithUseCaseAsync()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_stop_ignored", "background_process");
                return;
            }

            PrepareForServerShutdown();

            UnityCliLoopServerStartupService startupService =
                new UnityCliLoopServerStartupService(_serverInstanceFactory);
            UnityCliLoopServerShutdownUseCase useCase =
                new UnityCliLoopServerShutdownUseCase(startupService, this);
            ServerShutdownSchema schema = new() { ForceShutdown = false };
            System.Threading.CancellationToken cancellationToken = System.Threading.CancellationToken.None;

            ServerShutdownResponse result = await useCase.ExecuteAsync(schema, cancellationToken);

            if (result.Success)
            {
                // Server stopped by UseCase, so clear the reference
                _bridgeServer = null;

                // Clear session state to reflect server stopped
                UnityCliLoopEditorSettings.ClearServerSession();
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
        internal void OnBeforeAssemblyReload()
        {
            ClearStartupProtection();

            DomainReloadRecoveryUseCase useCase =
                new DomainReloadRecoveryUseCase(_sessionRecoveryService);
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(_bridgeServer);
            
            // Clear instance if server shutdown succeeded
            if (result.Success)
            {
                _bridgeServer = null;
            }

        }

        /// <summary>
        /// Processing after assembly reload.
        /// </summary>
        private void OnAfterAssemblyReload()
        {
            DomainReloadRecoveryUseCase useCase =
                new DomainReloadRecoveryUseCase(_sessionRecoveryService);
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
        private Task RestoreServerStateIfNeeded()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_restore_skipped", "background_process");
                return Task.CompletedTask;
            }

            bool isAfterCompile = UnityCliLoopEditorSettings.GetIsAfterCompile();

            if (_bridgeServer?.IsRunning == true)
            {
                if (isAfterCompile)
                {
                    UnityCliLoopEditorSettings.ClearAfterCompileFlag();
                }

                return Task.CompletedTask;
            }

            if (isAfterCompile)
            {
                UnityCliLoopEditorSettings.ClearAfterCompileFlag();
            }

            // Centralized, coalesced startup request
            // Store the task so UnityCliLoopSettingsWindow can await it to prevent race conditions
            return StartRecoveryIfNeededAsync(isAfterCompile, CancellationToken.None);
        }

        private void TryRestoreServerWithRetry(int retryCount)
        {
            const int maxRetries = 3;

            try
            {
                // If there is an existing server instance, ensure it is stopped.
                if (_bridgeServer != null)
                {
                    _bridgeServer.Dispose();
                    _bridgeServer = null;
                }

                _bridgeServer = _serverInstanceFactory.Create();
                _bridgeServer.StartServer();
                SaveRunningServerState();

                // Clear server-side reconnecting flag on successful restoration
                // NOTE: Do NOT clear UI display flag here - let it be cleared by timeout or client connection
                UnityCliLoopEditorSettings.SetIsReconnecting(false);

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
                    UnityCliLoopEditorSettings.ClearServerSession();
                }
            }
        }

        /// <summary>
        /// Cleanup on Unity exit.
        /// Disposes the bridge listener and marks the server as stopped so the CLI
        /// does not attempt to connect to a stale IPC endpoint after the editor closes.
        /// </summary>
        private void OnEditorQuitting()
        {
            if (_bridgeServer != null)
            {
                try
                {
                    _bridgeServer.Dispose();
                }
                finally
                {
                    _bridgeServer = null;
                }
            }
            UnityCliLoopEditorSettings.ClearServerSession();
        }

        /// <summary>
        /// OnServerLoopExited fires from the thread pool, but Unity APIs (EditorSettings,
        /// VibeLogger with SerializedObject, etc.) are main-thread-only.
        /// EditorApplication.delayCall marshals the recovery to the next editor tick.
        /// </summary>
        private void OnServerLoopUnexpectedlyExited()
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
                _bridgeServer = null;

                // The server just crashed — startup protection blocks recovery if the crash happens
                // within the 5-second protection window after a successful start
                System.Threading.Volatile.Write(ref _startupProtectionUntilTicks, 0L);

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
                        UnityCliLoopEditorSettings.ClearServerSession();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            };
        }

        /// <summary>
        /// Retry server restore with frame delay on the same project IPC endpoint.
        /// </summary>
        private async Task RetryServerRestoreAsync(int retryCount)
        {
            await EditorDelay.DelayFrame(5);
            TryRestoreServerWithRetry(retryCount + 1);
        }

        public bool IsStartupProtectionActive()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            return nowTicks < System.Threading.Volatile.Read(ref _startupProtectionUntilTicks);
        }

        internal void ActivateStartupProtection(int milliseconds)
        {
            long untilTicks = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
            System.Threading.Volatile.Write(ref _startupProtectionUntilTicks, untilTicks);
            VibeLogger.LogInfo("startup_protection_active", $"window={milliseconds}ms");
        }

        internal void PrepareForServerShutdown()
        {
            ClearStartupProtection();
        }

        /// <summary>
        /// Clears startup protection so recovery paths can restart the server immediately.
        /// </summary>
        internal void ClearStartupProtection()
        {
            System.Threading.Volatile.Write(ref _startupProtectionUntilTicks, 0L);
        }

        /// <summary>
        /// Centralized, coalesced recovery start.
        /// Attempts recovery on the project IPC endpoint for up to 5 seconds.
        /// </summary>
        public async Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken cancellationToken)
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

            await _startupSemaphore.WaitAsync(cancellationToken);
            string serverStartingLockToken = null;
            try
            {
                // If any server is already running, ignore this request to prevent double-binding
                if (_bridgeServer != null && _bridgeServer.IsRunning)
                {
                    VibeLogger.LogInfo("server_start_ignored", $"already_running endpoint={_bridgeServer.Endpoint}");
                    return;
                }

                serverStartingLockToken = CreateOptionalServerStartingLock();

                // Ensure previous instance is fully disposed before trying to bind a new one
                if (_bridgeServer != null)
                {
                    try
                    {
                        _bridgeServer.Dispose();
                        VibeLogger.LogInfo("server_disposed_before_bind", "disposed previous server instance");
                    }
                    catch (Exception ex)
                    {
                        VibeLogger.LogWarning("server_dispose_failed", ex.Message);
                    }
                    finally
                    {
                        _bridgeServer = null;
                    }
                }

                bool started = await TryBindWithWaitAsync(
                    5000,
                    250,
                    cancellationToken,
                    clearServerStartingLockWhenReady: true);

                if (!started)
                {
                    // Ensure session reflects stopped state on failure
                    UnityCliLoopEditorSettings.ClearServerSession();
                    UnityCliLoopEditorSettings.ClearReconnectingFlags();
                    Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] Recovery failed: no project IPC endpoint to bind.");
                    throw new InvalidOperationException("Failed to bind recovery endpoint.");
                }

                // Mark running and update settings
                SaveRunningServerState();

                // Clear reconnection-related flags on successful recovery
                UnityCliLoopEditorSettings.ClearReconnectingFlags();
                UnityCliLoopEditorSettings.ClearPostCompileReconnectingUI();
                UnityCliLoopToolRegistrar.WarmupRegistry();

                ActivateStartupProtection(5000);
            }
            catch
            {
                ServerStartingLockService.DeleteOwnedLockFile(serverStartingLockToken);
                throw;
            }
            finally
            {
                _startupSemaphore.Release();
            }
        }

        private async Task<bool> TryBindWithWaitAsync(
            int maxWaitMs,
            int stepMs,
            CancellationToken cancellationToken,
            bool clearServerStartingLockWhenReady = true)
        {
            int remainingMs = maxWaitMs;
            while (true)
            {
                VibeLogger.LogInfo("binding_attempt", "transport=project_ipc");
                IUnityCliLoopServerInstance server = null;
                try
                {
                    // Defensive: dispose any non-running stale instance before creating a new one
                    if (_bridgeServer != null && !_bridgeServer.IsRunning)
                    {
                        try
                        {
                            _bridgeServer.Dispose();
                            VibeLogger.LogInfo("server_disposed_before_bind", "disposed stale instance");
                        }
                        catch (Exception ex)
                        {
                            VibeLogger.LogWarning("server_dispose_failed", ex.Message);
                        }
                        finally
                        {
                            _bridgeServer = null;
                        }
                    }

                    server = _serverInstanceFactory.Create();
                    server.StartServer(clearServerStartingLockWhenReady);
                    _bridgeServer = server;
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
            UnityCliLoopEditorSettings.SetIsServerRunning(true);
        }

        internal string CreateOptionalServerStartingLock(Func<string> createLockFile = null)
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

        internal IUnityCliLoopServerInstance CreateServerInstanceForRecovery()
        {
            return _serverInstanceFactory.Create();
        }

        public void AddServerStateChangedHandler(Action handler)
        {
            _serverLifecycleRegistry.ServerStateChanged += handler;
        }

        public void RemoveServerStateChangedHandler(Action handler)
        {
            _serverLifecycleRegistry.ServerStateChanged -= handler;
        }
    }

}
