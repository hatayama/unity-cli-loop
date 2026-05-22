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
        private const string MANUAL_STOP_REASON = "manual-stop";
        private const string MANUAL_START_CLEANUP_REASON = "manual-start-cleanup";

        private enum ServerStopIntent
        {
            ManualStop,
            RestartCleanup,
        }

        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly UnityCliLoopServerLifecycleRegistryService _serverLifecycleRegistry;
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;
        private readonly SessionRecoveryService _sessionRecoveryService;
        private readonly ServerReadinessStateStore _stateStore;
        private readonly IUnityCliLoopServerReadinessProbe _readinessProbe;
        private readonly IUnityCliLoopServerDomainReloadLifecycle _domainReloadLifecycle;
        private IUnityCliLoopServerInstance _bridgeServer;
        private readonly SemaphoreSlim _startupSemaphore = new SemaphoreSlim(1, 1);
        private long _startupProtectionUntilTicks = 0;
        private Task _currentRecoveryTask;

        internal UnityCliLoopServerControllerService(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            UnityCliLoopServerLifecycleRegistryService serverLifecycleRegistry,
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopEditorSessionStateService sessionStateService,
            ServerReadinessStateStore stateStore,
            IUnityCliLoopServerReadinessProbe readinessProbe,
            IUnityCliLoopServerDomainReloadLifecycle domainReloadLifecycle)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(serverLifecycleRegistry != null, "serverLifecycleRegistry must not be null");
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(sessionStateService != null, "sessionStateService must not be null");
            System.Diagnostics.Debug.Assert(stateStore != null, "stateStore must not be null");
            System.Diagnostics.Debug.Assert(readinessProbe != null, "readinessProbe must not be null");
            System.Diagnostics.Debug.Assert(domainReloadLifecycle != null, "domainReloadLifecycle must not be null");

            _serverInstanceFactory = serverInstanceFactory ?? throw new ArgumentNullException(nameof(serverInstanceFactory));
            _serverLifecycleRegistry = serverLifecycleRegistry ?? throw new ArgumentNullException(nameof(serverLifecycleRegistry));
            _domainReloadDetectionService = domainReloadDetectionService ?? throw new ArgumentNullException(nameof(domainReloadDetectionService));
            _sessionStateService = sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _readinessProbe = readinessProbe ?? throw new ArgumentNullException(nameof(readinessProbe));
            _domainReloadLifecycle = domainReloadLifecycle ?? throw new ArgumentNullException(nameof(domainReloadLifecycle));
            _sessionRecoveryService = new SessionRecoveryService(
                this,
                _domainReloadDetectionService,
                _sessionStateService);
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
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;

            // Processing before assembly reload.
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Processing after assembly reload.
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
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
        internal async Task StartServerWithUseCaseAsync()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_start_ignored", "background_process");
                return;
            }

            string generationId = ServerReadinessStateStore.CreateGenerationId();
            WriteServerState(ServerReadinessPhase.Starting, generationId, "manual-start", null);

            // Always stop the existing server first so the project IPC endpoint is released.
            if (_bridgeServer != null)
            {
                bool cleanupSucceeded = await StopServerForRestartAsync(CancellationToken.None);
                if (!cleanupSucceeded)
                {
                    return;
                }
            }

            UnityCliLoopServerStartupService startupService =
                new UnityCliLoopServerStartupService(_serverInstanceFactory, _sessionStateService);
            UnityCliLoopServerInitializationUseCase useCase =
                new UnityCliLoopServerInitializationUseCase(
                    new EditorSecurityValidationService(),
                    startupService);
            System.Threading.CancellationToken cancellationToken = System.Threading.CancellationToken.None;

            ServerInitializationResult<IUnityCliLoopServerInstance> result =
                await useCase.ExecuteAsync(cancellationToken);

            if (!result.Success)
            {
                // Error message already handled by UseCase
                WriteServerState(ServerReadinessPhase.Failed, generationId, "manual-start", result.Message);
                UnityEngine.Debug.LogError($"Server startup failed: {result.Message}");
                return;
            }

            // UseCase creates a new server instance, so we keep a reference here
            // for compatibility with existing code
            _bridgeServer = result.ServerInstance;

            UnityCliLoopToolRegistrar.WarmupRegistry();
            await MarkServerReadyAsync(generationId, "manual-start", cancellationToken);
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
            await StopServerForIntentAsync(ServerStopIntent.ManualStop, CancellationToken.None);
        }

        private async Task<bool> StopServerForRestartAsync(CancellationToken ct)
        {
            return await StopServerForIntentAsync(ServerStopIntent.RestartCleanup, ct);
        }

        private async Task<bool> StopServerForIntentAsync(ServerStopIntent stopIntent, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_stop_ignored", "background_process");
                return true;
            }

            string generationId = ServerReadinessStateStore.CreateGenerationId();
            string reason = stopIntent == ServerStopIntent.ManualStop ? MANUAL_STOP_REASON : MANUAL_START_CLEANUP_REASON;
            WriteServerState(ServerReadinessPhase.Stopping, generationId, reason, null);
            _serverLifecycleRegistry.PublishServerStopping();
            PrepareForServerShutdown();

            UnityCliLoopServerStartupService startupService =
                new UnityCliLoopServerStartupService(_serverInstanceFactory, _sessionStateService);
            UnityCliLoopServerShutdownUseCase useCase =
                new UnityCliLoopServerShutdownUseCase(startupService, this);

            ServerShutdownResult result = await useCase.ExecuteAsync(ct);

            if (result.Success)
            {
                // Server stopped by UseCase, so clear the reference
                _bridgeServer = null;

                if (stopIntent == ServerStopIntent.ManualStop)
                {
                    _sessionStateService.MarkServerManuallyStopped();
                }
                else
                {
                    _sessionStateService.ClearServerSession();
                }

                WriteServerState(ServerReadinessPhase.Stopped, generationId, reason, null);
                return true;
            }
            else
            {
                // Error message already handled by UseCase
                WriteServerState(ServerReadinessPhase.Failed, generationId, reason, result.Message);
                UnityEngine.Debug.LogError($"Server shutdown failed: {result.Message}");
                return false;
            }
        }

        /// <summary>
        /// Processing before assembly reload.
        /// </summary>
        internal void OnBeforeAssemblyReload()
        {
            ClearStartupProtection();
            _domainReloadLifecycle.PrepareForDomainReload();
            string generationId = ServerReadinessStateStore.CreateGenerationId();
            WriteServerState(ServerReadinessPhase.Reloading, generationId, "domain-reload-before", null);

            DomainReloadRecoveryUseCase useCase =
                new DomainReloadRecoveryUseCase(
                    _sessionRecoveryService,
                    _domainReloadDetectionService,
                    _sessionStateService);
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
            ScheduleTrackedRecovery(() => ExecuteAfterDomainReloadRecoveryAsync(CancellationToken.None));
        }

        private async Task ExecuteAfterDomainReloadRecoveryAsync(CancellationToken cancellationToken)
        {
            DomainReloadRecoveryUseCase useCase =
                new DomainReloadRecoveryUseCase(
                    _sessionRecoveryService,
                    _domainReloadDetectionService,
                    _sessionStateService);
            ServiceResult<string> result =
                await useCase.ExecuteAfterDomainReloadAsync(cancellationToken);
            if (!result.Success)
            {
                string generationId = ServerReadinessStateStore.CreateGenerationId();
                string message = $"Domain reload recovery failed after Unity finished reloading assemblies. {result.ErrorMessage}";
                WriteServerState(ServerReadinessPhase.Failed, generationId, "domain-reload-after", message);
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Restores the server state if necessary.
        /// </summary>
        internal async Task RestoreServerStateIfNeeded()
        {
            if (IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_restore_skipped", "background_process");
                return;
            }

            bool isAfterCompile = _sessionStateService.GetIsAfterCompile();

            if (_bridgeServer?.IsRunning == true)
            {
                if (isAfterCompile)
                {
                    _sessionStateService.ClearAfterCompileFlag();
                }

                return;
            }

            if (isAfterCompile)
            {
                _sessionStateService.ClearAfterCompileFlag();
            }

            if (_sessionStateService.GetIsServerManuallyStopped())
            {
                return;
            }

            await StartRecoveryIfNeededAsync(isAfterCompile, CancellationToken.None);
        }

        /// <summary>
        /// Cleanup on Unity exit.
        /// Disposes the bridge listener and marks the server as stopped so the CLI
        /// does not attempt to connect to a stale IPC endpoint after the editor closes.
        /// </summary>
        private void OnEditorQuitting()
        {
            string generationId = ServerReadinessStateStore.CreateGenerationId();
            WriteServerState(ServerReadinessPhase.Stopping, generationId, "editor-quitting", null);

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
            _sessionStateService.ClearServerSession();
            WriteServerState(ServerReadinessPhase.Stopped, generationId, "editor-quitting", null);
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

                ScheduleTrackedRecovery(() => StartRecoveryIfNeededAsync(false, CancellationToken.None));
            };
        }

        private Task ScheduleTrackedRecovery(Func<Task> recoveryAction)
        {
            Debug.Assert(recoveryAction != null, "recoveryAction must not be null");

            Task recoveryTask = ExecuteTrackedRecoveryAsync(recoveryAction);
            _currentRecoveryTask = recoveryTask;
            _ = ClearTrackedRecoveryWhenCompleteAsync(recoveryTask);
            return recoveryTask;
        }

        private async Task ExecuteTrackedRecoveryAsync(Func<Task> recoveryAction)
        {
            Debug.Assert(recoveryAction != null, "recoveryAction must not be null");

            try
            {
                await recoveryAction();
            }
            catch (Exception ex)
            {
                string generationId = ServerReadinessStateStore.CreateGenerationId();
                string message = $"Unity CLI Loop server recovery failed before the bridge became ready. {ex.GetBaseException().Message}";
                WriteServerState(ServerReadinessPhase.Failed, generationId, "tracked-recovery", message);
                VibeLogger.LogError(
                    "server_recovery_failed",
                    message);
                _sessionStateService.ClearServerSession();
                throw new InvalidOperationException(message, ex);
            }
        }

        private async Task ClearTrackedRecoveryWhenCompleteAsync(Task recoveryTask)
        {
            Debug.Assert(recoveryTask != null, "recoveryTask must not be null");

            try
            {
                await recoveryTask;
            }
            finally
            {
                if (ReferenceEquals(_currentRecoveryTask, recoveryTask))
                {
                    _currentRecoveryTask = null;
                }
            }
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

            string generationId = ServerReadinessStateStore.CreateGenerationId();
            if (IsStartupProtectionActive())
            {
                VibeLogger.LogInfo("server_start_ignored", "startup_protection_active");
                if (_bridgeServer?.IsRunning == true)
                {
                    await MarkServerReadyAsync(generationId, "startup-protection-active", cancellationToken);
                    return;
                }

                string blockedMessage = "Unity CLI Loop server recovery was skipped because startup protection is active and no running bridge instance is available.";
                WriteServerState(ServerReadinessPhase.Failed, generationId, "startup-protection-active", blockedMessage);
                return;
            }

            WriteServerState(
                isAfterCompile ? ServerReadinessPhase.Recovering : ServerReadinessPhase.Starting,
                generationId,
                isAfterCompile ? "post-compile-recovery" : "server-recovery",
                null);

            VibeLogger.LogInfo("startup_request", "transport=project_ipc");

            await _startupSemaphore.WaitAsync(cancellationToken);
            try
            {
                // If any server is already running, ignore this request to prevent double-binding
                if (_bridgeServer != null && _bridgeServer.IsRunning)
                {
                    VibeLogger.LogInfo("server_start_ignored", $"already_running endpoint={_bridgeServer.Endpoint}");
                    await MarkServerReadyAsync(generationId, "already-running", cancellationToken);
                    return;
                }

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
                    cancellationToken);

                if (!started)
                {
                    // Ensure session reflects stopped state on failure
                    _sessionStateService.ClearServerSession();
                    _sessionStateService.ClearReconnectingFlags();
                    string message = "Unity CLI Loop server recovery failed because the project IPC endpoint could not be bound within 5000ms.";
                    WriteServerState(ServerReadinessPhase.Failed, generationId, "server-recovery", message);
                    Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] {message}");
                    throw new InvalidOperationException(message);
                }

                // Mark running and update settings
                SaveRunningServerState();

                // Clear reconnection-related flags on successful recovery
                _sessionStateService.ClearReconnectingFlags();
                _sessionStateService.ClearPostCompileReconnectingUI();
                UnityCliLoopToolRegistrar.WarmupRegistry();
                await MarkServerReadyAsync(generationId, "server-recovery", cancellationToken);

                ActivateStartupProtection(5000);
            }
            finally
            {
                _startupSemaphore.Release();
            }
        }

        private async Task<bool> TryBindWithWaitAsync(
            int maxWaitMs,
            int stepMs,
            CancellationToken cancellationToken)
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
                    server.StartServer();
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

        private void SaveRunningServerState()
        {
            _sessionStateService.MarkServerStarted();
        }

        private async Task MarkServerReadyAsync(
            string generationId,
            string reason,
            CancellationToken cancellationToken)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(generationId), "generationId must not be null or empty");

            try
            {
                await ProbeReadinessWithTimeoutAsync(cancellationToken, UnityCliLoopServerConfig.READINESS_PROBE_TIMEOUT_MS);
            }
            catch (Exception ex)
            {
                string message = $"Unity CLI Loop server bound its project IPC endpoint, but readiness probe failed during {reason}. {ex.GetBaseException().Message}";
                WriteServerState(ServerReadinessPhase.Failed, generationId, reason, message);
                throw new InvalidOperationException(message, ex);
            }

            WriteServerState(ServerReadinessPhase.Ready, generationId, reason, null);
            _serverLifecycleRegistry.PublishServerStarted();
        }

        internal async Task ProbeReadinessWithTimeoutAsync(
            CancellationToken cancellationToken,
            int timeoutMilliseconds)
        {
            Debug.Assert(timeoutMilliseconds > 0, "timeoutMilliseconds must be positive");

            using (CancellationTokenSource probeCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task probeTask = _readinessProbe.ProbeAsync(probeCancellation.Token);
                Task timeoutTask = TimerDelay.Wait(timeoutMilliseconds, cancellationToken);
                Task completedTask = await Task.WhenAny(probeTask, timeoutTask).ConfigureAwait(false);
                if (completedTask == probeTask)
                {
                    await probeTask.ConfigureAwait(false);
                    return;
                }

                probeCancellation.Cancel();
                ObserveTimedOutReadinessProbe(probeTask);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"Readiness probe timed out after {timeoutMilliseconds}ms while waiting for project IPC warmup.");
        }

        private static void ObserveTimedOutReadinessProbe(Task probeTask)
        {
            _ = probeTask.ContinueWith(
                completedTask => _ = completedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private void WriteServerState(
            ServerReadinessPhase phase,
            string generationId,
            string reason,
            string lastError)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(generationId), "generationId must not be null or empty");

            string endpoint = _bridgeServer?.Endpoint;
            _stateStore.Write(phase, generationId, reason, endpoint, lastError);
        }

        public void AddServerStateChangedHandler(Action handler)
        {
            _serverLifecycleRegistry.ServerStateChanged += handler;
        }

        public void RemoveServerStateChangedHandler(Action handler)
        {
            _serverLifecycleRegistry.ServerStateChanged -= handler;
        }

        public void AddServerStartedHandler(Action handler)
        {
            _serverLifecycleRegistry.ServerStarted += handler;
        }

        public void RemoveServerStartedHandler(Action handler)
        {
            _serverLifecycleRegistry.ServerStarted -= handler;
        }
    }

}
