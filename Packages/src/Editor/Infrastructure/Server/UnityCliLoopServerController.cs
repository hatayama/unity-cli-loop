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
        private enum ServerStopIntent
        {
            ManualStop,
            RestartCleanup,
        }

        private const int READINESS_IDLE_POLL_INTERVAL_MS = 250;

        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly UnityCliLoopServerLifecycleRegistryService _serverLifecycleRegistry;
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;
        private readonly SessionRecoveryService _sessionRecoveryService;
        private readonly IUnityCliLoopServerReadinessProbe _readinessProbe;
        private readonly IUnityCliLoopServerDomainReloadLifecycle _domainReloadLifecycle;
        private readonly Func<bool> _isReadinessProbeBlocked;
        private readonly Func<int, CancellationToken, Task> _waitBeforeReadinessRetryAsync;
        private readonly Func<int, CancellationToken, Task> _waitBeforeRecoveryRetryAsync;
        private readonly int _readinessIdleTimeoutMilliseconds;
        private IUnityCliLoopServerInstance _bridgeServer;
        private readonly SemaphoreSlim _startupSemaphore = new SemaphoreSlim(1, 1);
        private long _startupProtectionUntilTicks = 0;
        private Task _currentRecoveryTask;

        internal UnityCliLoopServerControllerService(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            UnityCliLoopServerLifecycleRegistryService serverLifecycleRegistry,
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopEditorSessionStateService sessionStateService,
            IUnityCliLoopServerReadinessProbe readinessProbe,
            IUnityCliLoopServerDomainReloadLifecycle domainReloadLifecycle,
            Func<bool> isReadinessProbeBlocked = null,
            Func<int, CancellationToken, Task> waitBeforeReadinessRetryAsync = null,
            Func<int, CancellationToken, Task> waitBeforeRecoveryRetryAsync = null,
            int readinessIdleTimeoutMilliseconds = UnityCliLoopServerConfig.READINESS_PROBE_TIMEOUT_MS)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(serverLifecycleRegistry != null, "serverLifecycleRegistry must not be null");
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(sessionStateService != null, "sessionStateService must not be null");
            System.Diagnostics.Debug.Assert(readinessProbe != null, "readinessProbe must not be null");
            System.Diagnostics.Debug.Assert(domainReloadLifecycle != null, "domainReloadLifecycle must not be null");
            System.Diagnostics.Debug.Assert(readinessIdleTimeoutMilliseconds > 0, "readinessIdleTimeoutMilliseconds must be positive");

            _serverInstanceFactory = serverInstanceFactory ?? throw new ArgumentNullException(nameof(serverInstanceFactory));
            _serverLifecycleRegistry = serverLifecycleRegistry ?? throw new ArgumentNullException(nameof(serverLifecycleRegistry));
            _domainReloadDetectionService = domainReloadDetectionService ?? throw new ArgumentNullException(nameof(domainReloadDetectionService));
            _sessionStateService = sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));
            _readinessProbe = readinessProbe ?? throw new ArgumentNullException(nameof(readinessProbe));
            _domainReloadLifecycle = domainReloadLifecycle ?? throw new ArgumentNullException(nameof(domainReloadLifecycle));
            _isReadinessProbeBlocked = isReadinessProbeBlocked ?? IsEditorBusyForReadinessProbe;
            _waitBeforeReadinessRetryAsync = waitBeforeReadinessRetryAsync ?? TimerDelay.Wait;
            _waitBeforeRecoveryRetryAsync = waitBeforeRecoveryRetryAsync ?? TimerDelay.Wait;
            _readinessIdleTimeoutMilliseconds = readinessIdleTimeoutMilliseconds;
            _sessionRecoveryService = new SessionRecoveryService(
                this,
                _domainReloadDetectionService,
                _sessionStateService);
        }

        private static bool IsEditorBusyForReadinessProbe()
        {
            return EditorApplication.isCompiling ||
                   EditorApplication.isUpdating ||
                   DomainReloadStateRegistry.IsDomainReloadInProgress();
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
                UnityEngine.Debug.LogError($"Server startup failed: {result.Message}");
                return;
            }

            // UseCase creates a new server instance, so we keep a reference here
            // for compatibility with existing code
            _bridgeServer = result.ServerInstance;

            UnityCliLoopToolRegistrar.WarmupRegistry();
            await MarkServerReadyAsync("manual-start", cancellationToken);
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
            ClearStartupProtection();
            _domainReloadLifecycle.PrepareForDomainReload();

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
            _sessionStateService.ClearServerSession();
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

        /// <summary>
        /// Runs a recovery action, retrying with backoff so one transient failure
        /// (e.g. readiness timeout during a heavy import) does not leave the server
        /// down until the next domain reload.
        /// </summary>
        internal async Task ExecuteTrackedRecoveryAsync(Func<Task> recoveryAction)
        {
            Debug.Assert(recoveryAction != null, "recoveryAction must not be null");

            int failedAttemptCount = 0;
            while (true)
            {
                try
                {
                    await recoveryAction();
                    return;
                }
                catch (Exception ex)
                {
                    if (failedAttemptCount >= UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS.Length)
                    {
                        string message = $"Unity CLI Loop server recovery failed before the bridge became ready. {ex.GetBaseException().Message}";
                        VibeLogger.LogError(
                            "server_recovery_failed",
                            message);
                        _sessionStateService.ClearServerSession();
                        throw new InvalidOperationException(message, ex);
                    }

                    int delayMilliseconds = UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS[failedAttemptCount];
                    failedAttemptCount++;
                    VibeLogger.LogWarning(
                        "server_recovery_retry_scheduled",
                        $"Recovery attempt {failedAttemptCount} failed; retrying in {delayMilliseconds}ms. {ex.GetBaseException().Message}");
                    await _waitBeforeRecoveryRetryAsync(delayMilliseconds, CancellationToken.None);

                    // Why: an explicit Stop Server issued during the backoff must win over
                    // automatic recovery, otherwise the retry would silently restart the server.
                    if (_sessionStateService.GetIsServerManuallyStopped())
                    {
                        VibeLogger.LogInfo(
                            "server_recovery_retry_abandoned",
                            "Recovery retry abandoned because the server was manually stopped.");
                        return;
                    }
                }
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

            if (IsStartupProtectionActive())
            {
                VibeLogger.LogInfo("server_start_ignored", "startup_protection_active");
                if (_bridgeServer?.IsRunning == true)
                {
                    await MarkServerReadyAsync("startup-protection-active", cancellationToken);
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
                    await MarkServerReadyAsync("already-running", cancellationToken);
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
                    Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] {message}");
                    throw new InvalidOperationException(message);
                }

                // Mark running and update settings
                SaveRunningServerState();

                // Clear reconnection-related flags on successful recovery
                _sessionStateService.ClearReconnectingFlags();
                _sessionStateService.ClearPostCompileReconnectingUI();
                UnityCliLoopToolRegistrar.WarmupRegistry();
                await MarkServerReadyAsync("server-recovery", cancellationToken);

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
            _sessionStateService.MarkServerStarted();
        }

        private async Task MarkServerReadyAsync(
            string reason,
            CancellationToken cancellationToken)
        {
            try
            {
                await WaitForEditorIdleBeforeReadinessProbeAsync(
                    cancellationToken,
                    _readinessIdleTimeoutMilliseconds);
                await ProbeReadinessWithTimeoutAsync(cancellationToken, UnityCliLoopServerConfig.READINESS_PROBE_TIMEOUT_MS);
            }
            catch (Exception ex)
            {
                string message = $"Unity CLI Loop server bound its project IPC endpoint, but readiness probe failed during {reason}. {ex.GetBaseException().Message}";
                throw new InvalidOperationException(message, ex);
            }

            _serverLifecycleRegistry.PublishServerStarted();
        }

        /// <summary>
        /// Waits until Unity is ready for a main-thread IPC readiness probe.
        /// </summary>
        private async Task WaitForEditorIdleBeforeReadinessProbeAsync(
            CancellationToken cancellationToken,
            int timeoutMilliseconds)
        {
            Debug.Assert(timeoutMilliseconds > 0, "timeoutMilliseconds must be positive");

            cancellationToken.ThrowIfCancellationRequested();

            int remainingMilliseconds = timeoutMilliseconds;
            while (_isReadinessProbeBlocked())
            {
                if (remainingMilliseconds <= 0)
                {
                    throw new TimeoutException(
                        $"Readiness probe timed out after {timeoutMilliseconds}ms while waiting for Unity editor idle.");
                }

                int delayMilliseconds = Math.Min(READINESS_IDLE_POLL_INTERVAL_MS, remainingMilliseconds);
                // Why: compile, import, and domain reload work can hold the editor thread after the
                // endpoint is bound, so readiness timeout must start only after Unity can answer IPC.
                await _waitBeforeReadinessRetryAsync(
                    delayMilliseconds,
                    cancellationToken);
                remainingMilliseconds -= delayMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();
            }
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
