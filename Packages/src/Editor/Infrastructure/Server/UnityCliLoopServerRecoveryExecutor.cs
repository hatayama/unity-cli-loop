using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Executes the coalesced, bind-retry recovery flow that (re)binds the project IPC endpoint.
    /// </summary>
    internal sealed class UnityCliLoopServerRecoveryExecutor
    {
        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly UnityCliLoopServerReadinessService _readinessService;
        private readonly UnityCliLoopServerStartupProtectionService _startupProtectionService;
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;
        private readonly Func<IUnityCliLoopServerInstance> _getBridgeServer;
        private readonly Action<IUnityCliLoopServerInstance> _setBridgeServer;
        private readonly SemaphoreSlim _startupSemaphore = new SemaphoreSlim(1, 1);

        internal UnityCliLoopServerRecoveryExecutor(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            UnityCliLoopServerReadinessService readinessService,
            UnityCliLoopServerStartupProtectionService startupProtectionService,
            ISessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopToolRegistrarService toolRegistrarService,
            Func<IUnityCliLoopServerInstance> getBridgeServer,
            Action<IUnityCliLoopServerInstance> setBridgeServer)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(readinessService != null, "readinessService must not be null");
            System.Diagnostics.Debug.Assert(startupProtectionService != null, "startupProtectionService must not be null");
            System.Diagnostics.Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");
            System.Diagnostics.Debug.Assert(getBridgeServer != null, "getBridgeServer must not be null");
            System.Diagnostics.Debug.Assert(setBridgeServer != null, "setBridgeServer must not be null");

            _serverInstanceFactory = serverInstanceFactory;
            _readinessService = readinessService;
            _startupProtectionService = startupProtectionService;
            _sessionFlagsRepository = sessionFlagsRepository;
            _toolRegistrarService = toolRegistrarService;
            _getBridgeServer = getBridgeServer;
            _setBridgeServer = setBridgeServer;
        }

        /// <summary>
        /// Centralized, coalesced recovery start.
        /// Attempts recovery on the project IPC endpoint for up to 5 seconds.
        /// </summary>
        internal async Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken ct)
        {
            if (UnityCliLoopServerControllerService.IsBackgroundUnityProcess())
            {
                VibeLogger.LogInfo("server_start_ignored", "background_process");
                return;
            }

            if (_startupProtectionService.IsStartupProtectionActive())
            {
                VibeLogger.LogInfo("server_start_ignored", "startup_protection_active");
                if (_getBridgeServer()?.IsRunning == true)
                {
                    await _readinessService.MarkServerReadyAsync("startup-protection-active", ct);
                    return;
                }

                return;
            }

            VibeLogger.LogInfo("startup_request", "transport=project_ipc");

            await _startupSemaphore.WaitAsync(ct);
            try
            {
                // If any server is already running, ignore this request to prevent double-binding
                if (_getBridgeServer() != null && _getBridgeServer().IsRunning)
                {
                    VibeLogger.LogInfo("server_start_ignored", $"already_running endpoint={_getBridgeServer().Endpoint}");
                    await _readinessService.MarkServerReadyAsync("already-running", ct);
                    return;
                }

                // Ensure previous instance is fully disposed before trying to bind a new one
                if (_getBridgeServer() != null)
                {
                    try
                    {
                        _getBridgeServer().Dispose();
                        VibeLogger.LogInfo("server_disposed_before_bind", "disposed previous server instance");
                    }
                    catch (Exception ex)
                    {
                        VibeLogger.LogWarning("server_dispose_failed", ex.Message);
                    }
                    finally
                    {
                        _setBridgeServer(null);
                    }
                }

                bool started = await TryBindWithWaitAsync(
                    5000,
                    250,
                    ct);

                if (!started)
                {
                    // Ensure session reflects stopped state on failure
                    _sessionFlagsRepository.ClearServerSession();
                    _sessionFlagsRepository.ClearReconnectingFlags();
                    string message = "Unity CLI Loop server recovery failed because the project IPC endpoint could not be bound within 5000ms.";
                    UnityEngine.Debug.LogError($"[{UnityCliLoopConstants.PROJECT_NAME}] {message}");
                    throw new InvalidOperationException(message);
                }

                // Mark running and update settings
                _sessionFlagsRepository.MarkServerStarted();

                // Clear reconnection-related flags on successful recovery
                _sessionFlagsRepository.ClearReconnectingFlags();
                _sessionFlagsRepository.ClearPostCompileReconnectingUI();
                _toolRegistrarService.WarmupRegistry();
                await _readinessService.MarkServerReadyAsync("server-recovery", ct);

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
            CancellationToken ct)
        {
            int remainingMs = maxWaitMs;
            while (true)
            {
                VibeLogger.LogInfo("binding_attempt", "transport=project_ipc");
                IUnityCliLoopServerInstance server = null;
                try
                {
                    // Defensive: dispose any non-running stale instance before creating a new one
                    if (_getBridgeServer() != null && !_getBridgeServer().IsRunning)
                    {
                        try
                        {
                            _getBridgeServer().Dispose();
                            VibeLogger.LogInfo("server_disposed_before_bind", "disposed stale instance");
                        }
                        catch (Exception ex)
                        {
                            VibeLogger.LogWarning("server_dispose_failed", ex.Message);
                        }
                        finally
                        {
                            _setBridgeServer(null);
                        }
                    }

                    server = _serverInstanceFactory.Create();
                    server.StartServer();
                    _setBridgeServer(server);
                    VibeLogger.LogInfo(
                        "binding_success",
                        "Unity CLI Loop server bound the project IPC endpoint.",
                        new { endpoint = server.Endpoint });
                    return true;
                }
                catch (Exception ex)
                {
                    // Ensure partially created server is cleaned up on failure
                    server?.Dispose();
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
                    await TimerDelay.Wait(delay, ct);
                    remainingMs -= delay;
                }
            }
        }
    }
}
