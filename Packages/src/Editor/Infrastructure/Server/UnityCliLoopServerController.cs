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
        IUnityCliLoopServerRecoveryCoordinator
    {
        private enum ServerStopIntent
        {
            ManualStop,
            RestartCleanup,
        }

        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly UnityCliLoopServerLifecycleRegistryService _serverLifecycleRegistry;
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly UnityCliLoopServerInitializationUseCase _initializationUseCase;
        private readonly UnityCliLoopServerShutdownUseCase _shutdownUseCase;
        private readonly SessionRecoveryService _sessionRecoveryService;
        private readonly DomainReloadRecoveryUseCase _domainReloadRecoveryUseCase;
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;
        private readonly UnityCliLoopServerReadinessService _readinessService;
        private readonly UnityCliLoopServerStartupProtectionService _startupProtectionService;
        private readonly UnityCliLoopServerRecoveryTrackingService _recoveryTrackingService;
        private readonly IUnityCliLoopServerDomainReloadLifecycle _domainReloadLifecycle;
        private IUnityCliLoopServerInstance _bridgeServer;
        private readonly SemaphoreSlim _startupSemaphore = new SemaphoreSlim(1, 1);

        internal UnityCliLoopServerControllerService(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            UnityCliLoopServerLifecycleRegistryService serverLifecycleRegistry,
            IDomainReloadDetectionService domainReloadDetectionService,
            ISessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopServerInitializationUseCase initializationUseCase,
            UnityCliLoopServerShutdownUseCase shutdownUseCase,
            SessionRecoveryService sessionRecoveryService,
            DomainReloadRecoveryUseCase domainReloadRecoveryUseCase,
            UnityCliLoopToolRegistrarService toolRegistrarService,
            UnityCliLoopServerReadinessService readinessService,
            UnityCliLoopServerStartupProtectionService startupProtectionService,
            UnityCliLoopServerRecoveryTrackingService recoveryTrackingService,
            IUnityCliLoopServerDomainReloadLifecycle domainReloadLifecycle)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(serverLifecycleRegistry != null, "serverLifecycleRegistry must not be null");
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            System.Diagnostics.Debug.Assert(initializationUseCase != null, "initializationUseCase must not be null");
            System.Diagnostics.Debug.Assert(shutdownUseCase != null, "shutdownUseCase must not be null");
            System.Diagnostics.Debug.Assert(sessionRecoveryService != null, "sessionRecoveryService must not be null");
            System.Diagnostics.Debug.Assert(domainReloadRecoveryUseCase != null, "domainReloadRecoveryUseCase must not be null");
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");
            System.Diagnostics.Debug.Assert(readinessService != null, "readinessService must not be null");
            System.Diagnostics.Debug.Assert(startupProtectionService != null, "startupProtectionService must not be null");
            System.Diagnostics.Debug.Assert(recoveryTrackingService != null, "recoveryTrackingService must not be null");
            System.Diagnostics.Debug.Assert(domainReloadLifecycle != null, "domainReloadLifecycle must not be null");

            _serverInstanceFactory = serverInstanceFactory ?? throw new ArgumentNullException(nameof(serverInstanceFactory));
            _serverLifecycleRegistry = serverLifecycleRegistry ?? throw new ArgumentNullException(nameof(serverLifecycleRegistry));
            _domainReloadDetectionService = domainReloadDetectionService ?? throw new ArgumentNullException(nameof(domainReloadDetectionService));
            _sessionFlagsRepository = sessionFlagsRepository ?? throw new ArgumentNullException(nameof(sessionFlagsRepository));
            _initializationUseCase = initializationUseCase ?? throw new ArgumentNullException(nameof(initializationUseCase));
            _shutdownUseCase = shutdownUseCase ?? throw new ArgumentNullException(nameof(shutdownUseCase));
            _sessionRecoveryService = sessionRecoveryService ?? throw new ArgumentNullException(nameof(sessionRecoveryService));
            _domainReloadRecoveryUseCase = domainReloadRecoveryUseCase ?? throw new ArgumentNullException(nameof(domainReloadRecoveryUseCase));
            _toolRegistrarService = toolRegistrarService ?? throw new ArgumentNullException(nameof(toolRegistrarService));
            _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
            _startupProtectionService = startupProtectionService ?? throw new ArgumentNullException(nameof(startupProtectionService));
            _recoveryTrackingService = recoveryTrackingService ?? throw new ArgumentNullException(nameof(recoveryTrackingService));
            _domainReloadLifecycle = domainReloadLifecycle ?? throw new ArgumentNullException(nameof(domainReloadLifecycle));
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
        public Task RecoveryTask => _recoveryTrackingService.RecoveryTask;

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
            _recoveryTrackingService.ScheduleStartupRecovery(
                action => EditorApplication.delayCall += () => action(),
                RestoreServerStateIfNeeded);
        }

        public void StartServer()
        {
            // Why: an async void body lets exceptions (e.g. a readiness probe failure thrown by
            // MarkServerReadyAsync) escape to the Unity synchronization context as unhandled.
            // Forget() observes the task and logs the exception instead.
            StartServerWithUseCaseAsync().Forget();
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

            // Always stop the existing server first so the project IPC endpoint is released.
            if (_bridgeServer != null)
            {
                bool cleanupSucceeded = await StopServerForRestartAsync(CancellationToken.None);
                if (!cleanupSucceeded)
                {
                    return;
                }
            }

            System.Threading.CancellationToken cancellationToken = System.Threading.CancellationToken.None;

            ServerInitializationResult<IUnityCliLoopServerInstance> result =
                await _initializationUseCase.ExecuteAsync(cancellationToken);

            if (!result.Success)
            {
                // Error message already handled by UseCase
                UnityEngine.Debug.LogError($"Server startup failed: {result.Message}");
                return;
            }

            // UseCase creates a new server instance, so we keep a reference here
            // for compatibility with existing code
            _bridgeServer = result.ServerInstance;

            _toolRegistrarService.WarmupRegistry();
            await _readinessService.MarkServerReadyAsync("manual-start", cancellationToken);
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void StopServer()
        {
            // Why: same as StartServer — Forget() keeps shutdown failures observed and logged
            // instead of crashing through an async void boundary.
            StopServerWithUseCaseAsync().Forget();
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

            _serverLifecycleRegistry.PublishServerStopping();
            _startupProtectionService.ClearStartupProtection();

            ServerShutdownResult result = await _shutdownUseCase.ExecuteAsync(_bridgeServer, ct);

            if (result.Success)
            {
                // Server stopped by UseCase, so clear the reference
                _bridgeServer = null;

                if (stopIntent == ServerStopIntent.ManualStop)
                {
                    _sessionFlagsRepository.MarkServerManuallyStopped();
                }
                else
                {
                    _sessionFlagsRepository.ClearServerSession();
                }

                return true;
            }
            else
            {
                // Error message already handled by UseCase
                UnityEngine.Debug.LogError($"Server shutdown failed: {result.Message}");
                return false;
            }
        }

        /// <summary>
        /// Processing before assembly reload.
        /// </summary>
        internal void OnBeforeAssemblyReload()
        {
            _startupProtectionService.ClearStartupProtection();
            _domainReloadLifecycle.PrepareForDomainReload();

            ServiceResult<string> result = _domainReloadRecoveryUseCase.ExecuteBeforeDomainReload(_bridgeServer);
            
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
            _recoveryTrackingService.ScheduleTrackedRecovery(() => ExecuteAfterDomainReloadRecoveryAsync(CancellationToken.None));
        }

        private async Task ExecuteAfterDomainReloadRecoveryAsync(CancellationToken cancellationToken)
        {
            ServiceResult<string> result =
                await _domainReloadRecoveryUseCase.ExecuteAfterDomainReloadAsync(this, cancellationToken);
            if (!result.Success)
            {
                string message = $"Domain reload recovery failed after Unity finished reloading assemblies. {result.ErrorMessage}";
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

            ValidationResult restoreResult =
                await _sessionRecoveryService.RestoreServerStateIfNeededAsync(
                    this,
                    CancellationToken.None);
            if (!restoreResult.IsValid)
            {
                throw new InvalidOperationException(restoreResult.ErrorMessage);
            }
        }

        /// <summary>
        /// Cleanup on Unity exit.
        /// Disposes the bridge listener and clears the in-editor server session before the editor closes.
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
            _sessionFlagsRepository.ClearServerSession();
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
                _startupProtectionService.ClearStartupProtection();

                VibeLogger.LogWarning(
                    "server_loop_exit_detected",
                    "Detected unexpected server loop exit. Initiating automatic recovery.",
                    new { transport = "project_ipc" }
                );

                // Resources already cleaned up by CleanupAfterUnexpectedLoopExit — just clear the reference
                _bridgeServer = null;

                // The server just crashed — startup protection blocks recovery if the crash happens
                // within the 5-second protection window after a successful start
                _startupProtectionService.ClearStartupProtection();

                _recoveryTrackingService.ScheduleTrackedRecovery(() => StartRecoveryIfNeededAsync(false, CancellationToken.None));
            };
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

            if (_startupProtectionService.IsStartupProtectionActive())
            {
                VibeLogger.LogInfo("server_start_ignored", "startup_protection_active");
                if (_bridgeServer?.IsRunning == true)
                {
                    await _readinessService.MarkServerReadyAsync("startup-protection-active", cancellationToken);
                    return;
                }

                return;
            }

            VibeLogger.LogInfo("startup_request", "transport=project_ipc");

            await _startupSemaphore.WaitAsync(cancellationToken);
            try
            {
                // If any server is already running, ignore this request to prevent double-binding
                if (_bridgeServer != null && _bridgeServer.IsRunning)
                {
                    VibeLogger.LogInfo("server_start_ignored", $"already_running endpoint={_bridgeServer.Endpoint}");
                    await _readinessService.MarkServerReadyAsync("already-running", cancellationToken);
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
                    _sessionFlagsRepository.ClearServerSession();
                    _sessionFlagsRepository.ClearReconnectingFlags();
                    string message = "Unity CLI Loop server recovery failed because the project IPC endpoint could not be bound within 5000ms.";
                    Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] {message}");
                    throw new InvalidOperationException(message);
                }

                // Mark running and update settings
                SaveRunningServerState();

                // Clear reconnection-related flags on successful recovery
                _sessionFlagsRepository.ClearReconnectingFlags();
                _sessionFlagsRepository.ClearPostCompileReconnectingUI();
                _toolRegistrarService.WarmupRegistry();
                await _readinessService.MarkServerReadyAsync("server-recovery", cancellationToken);

                _startupProtectionService.ActivateStartupProtection(5000);
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
                    VibeLogger.LogInfo(
                        "binding_success",
                        "Unity CLI Loop server bound the project IPC endpoint.",
                        new { endpoint = server.Endpoint });
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
            _sessionFlagsRepository.MarkServerStarted();
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
