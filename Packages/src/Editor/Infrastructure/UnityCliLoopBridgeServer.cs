using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Unity CLI bridge server.
    /// Accepts project-local CLI connections and handles JSON-RPC 2.0 communication.
    /// </summary>
    public class UnityCliLoopBridgeServer : IUnityCliLoopServerInstance
    {
        // Fired from thread pool when ServerLoopAsync exits while _isRunning is still true.
        // Subscribers must marshal to main thread before accessing Unity APIs.
        public event Action ServerLoopExited;
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly UnityCliLoopBridgeClientSessionManager _clientSessionManager;
        
        private IBridgeTransportListener _transportListener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _serverTask;
        // Read from thread pool (ServerLoopAsync), written from main thread (StopServer)
        private volatile bool _isRunning = false;

        // Guard against concurrent cleanup from ServerLoopAsync finally + external disposal
        private int _unexpectedExitCleanupStarted = 0;
        
        internal UnityCliLoopBridgeServer(
            IDomainReloadDetectionService domainReloadDetectionService,
            JsonRpcRequestProcessor jsonRpcRequestProcessor,
            UnityCliLoopBridgeHeartbeatService heartbeatService,
            UnityCliLoopBridgeClientDisconnectMonitor clientDisconnectMonitor)
        {
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(jsonRpcRequestProcessor != null, "jsonRpcRequestProcessor must not be null");
            System.Diagnostics.Debug.Assert(heartbeatService != null, "heartbeatService must not be null");
            System.Diagnostics.Debug.Assert(clientDisconnectMonitor != null, "clientDisconnectMonitor must not be null");

            _domainReloadDetectionService = domainReloadDetectionService
                ?? throw new ArgumentNullException(nameof(domainReloadDetectionService));
            JsonRpcRequestProcessor validatedJsonRpcRequestProcessor = jsonRpcRequestProcessor
                ?? throw new ArgumentNullException(nameof(jsonRpcRequestProcessor));
            UnityCliLoopBridgeHeartbeatService validatedHeartbeatService = heartbeatService
                ?? throw new ArgumentNullException(nameof(heartbeatService));
            UnityCliLoopBridgeClientDisconnectMonitor validatedClientDisconnectMonitor = clientDisconnectMonitor
                ?? throw new ArgumentNullException(nameof(clientDisconnectMonitor));
            _clientSessionManager = new UnityCliLoopBridgeClientSessionManager(
                validatedJsonRpcRequestProcessor,
                validatedHeartbeatService,
                validatedClientDisconnectMonitor);
        }
        
        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public bool IsRunning => _isRunning;
        
        public string Endpoint => _transportListener?.Endpoint.DisplayName() ?? string.Empty;

        public void StartServer()
        {
            if (_isRunning)
            {
                return;
            }

            BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(UnityEngine.Application.dataPath + "/..");
            _cancellationTokenSource = new CancellationTokenSource();
            _unexpectedExitCleanupStarted = 0;
            
            try
            {
                _transportListener = BridgeTransportListenerFactory.Create(endpoint);
                _transportListener.Start();
                _isRunning = true;
                
                _serverTask = Task.Run(() => ServerLoopAsync(_cancellationTokenSource.Token));

                // Safety net: log if the server task faults unexpectedly.
                // Primary detection is in ServerLoopAsync's finally block; this catches unhandled exceptions in Task.Run itself.
                _serverTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        VibeLogger.LogError(
                            "server_task_faulted",
                            $"Server task faulted unexpectedly: {task.Exception?.GetBaseException().Message}",
                            new { exceptionType = task.Exception?.GetBaseException().GetType().Name }
                        );
                    }
                }, TaskScheduler.Default);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                _isRunning = false;
                throw new InvalidOperationException(
                    $"Project IPC endpoint is already in use: {endpoint.DisplayName()}", ex);
            }
            catch (Exception)
            {
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void StopServer()
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            CancellationTokenSource cancellationTokenSource = TakeCancellationTokenSource();
            cancellationTokenSource?.Cancel();

            // Explicitly disconnect all connected clients before stopping the server
            _clientSessionManager.DisconnectAllClients();
            
            try
            {
                _transportListener?.Stop();
            }
            finally
            {
                _transportListener = null;
            }

            Task serverTask = _serverTask;
            Task[] clientTasks = _clientSessionManager.GetActiveClientTasks();
            _serverTask = null;
            DisposeCancellationSourceAfterServerTaskAsync(
                serverTask,
                clientTasks,
                cancellationTokenSource,
                TimeSpan.FromSeconds(UnityCliLoopServerConfig.SHUTDOWN_TIMEOUT_SECONDS)).Forget();
        }

        private CancellationTokenSource TakeCancellationTokenSource()
        {
            return Interlocked.Exchange(ref _cancellationTokenSource, null);
        }

        internal static async Task DisposeCancellationSourceAfterServerTaskAsync(
            Task serverTask,
            Task[] clientTasks,
            CancellationTokenSource cancellationTokenSource,
            TimeSpan shutdownTimeout)
        {
            Task[] tasks = BuildShutdownWaitTasks(serverTask, clientTasks);
            if (tasks.Length > 0)
            {
                Task allTasksCompleted = Task.WhenAll(tasks);
                Task firstCompleted = await Task.WhenAny(
                    allTasksCompleted,
                    Task.Delay(shutdownTimeout));
                if (firstCompleted != allTasksCompleted)
                {
                    // Why: a straggling task may still observe the token after this timeout, and
                    // disposing the source under it surfaces ObjectDisposedException inside that
                    // task. Leaking one CancellationTokenSource is harmless; disposing early is not.
                    VibeLogger.LogWarning(
                        "server_shutdown_cts_leaked",
                        "Shutdown timed out waiting for server/client tasks; skipping CancellationTokenSource disposal.");
                    return;
                }
            }

            cancellationTokenSource?.Dispose();
        }

        private static Task[] BuildShutdownWaitTasks(Task serverTask, Task[] clientTasks)
        {
            List<Task> tasks = new();
            if (serverTask != null)
            {
                tasks.Add(serverTask);
            }

            if (clientTasks != null)
            {
                tasks.AddRange(clientTasks.Where(task => task != null));
            }

            return tasks.ToArray();
        }

        /// <summary>
        /// StopServer() guards on _isRunning==true, but by the time this runs _isRunning may already
        /// be false or the normal shutdown path may race with the finally block.
        /// A separate cleanup path that skips the _isRunning guard is needed.
        /// Lifecycle events are deferred to OnServerLoopExited → EditorApplication.delayCall
        /// because this runs on the thread pool where Unity APIs are unsafe.
        /// </summary>
        private void CleanupAfterUnexpectedLoopExit()
        {
            if (Interlocked.Exchange(ref _unexpectedExitCleanupStarted, 1) != 0)
            {
                return;
            }

            _clientSessionManager.DisconnectAllClients();

            CancellationTokenSource cancellationTokenSource = TakeCancellationTokenSource();
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();

            try
            {
                _transportListener?.Stop();
            }
            finally
            {
                _transportListener = null;
                _isRunning = false;
            }
        }

        /// <summary>
        /// The server's main loop.
        /// </summary>
        private async Task ServerLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        BridgeClientConnection client = await AcceptClientAsync(_transportListener, cancellationToken);
                        if (client != null)
                        {
                            _clientSessionManager.StartClientHandler(client, cancellationToken);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected when StopServer() disposes the listener while accept is pending.
                        // If _isRunning is still true here, this is an unexpected disposal — finally block handles state cleanup.
                        if (_isRunning)
                        {
                            VibeLogger.LogWarning(
                                "server_loop_disposed_while_running",
                                "Transport listener disposed while server was still marked as running. Exiting loop."
                            );
                        }
                        break;
                    }
                    catch (ThreadAbortException ex)
                    {
                        // Log and re-throw ThreadAbortException
                        if (!DomainReloadStateRegistry.IsDomainReloadInProgress())
                        {
                            VibeLogger.LogWarning(
                                "server_loop_thread_abort",
                                $"Unexpected thread abort: {ex.Message}");
                        }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // Why: continuing here would retry a failing accept in a tight loop
                        // (silently pinning a CPU core, as the Mono PipeSecurity regression showed).
                        // Exiting the loop hands the failure to the recovery path, which retries
                        // with bounded backoff. Debug.LogError is required for visibility because
                        // VibeLogger is compiled out unless ULOOP_DEBUG is defined.
                        Debug.LogError(
                            $"[{UnityCliLoopConstants.PROJECT_NAME}] Server accept loop failed; restarting the IPC server. {ex}");
                        VibeLogger.LogError(
                            "server_accept_loop_failed",
                            ex.Message,
                            new { exceptionType = ex.GetType().Name });
                        break;
                    }
                }
            }
            finally
            {
                // StopServer sets _isRunning=false before cancelling, so if it's still true here
                // the loop exited unexpectedly (e.g. ObjectDisposedException, listener disposed externally)
                bool wasUnexpectedExit = _isRunning;
                if (wasUnexpectedExit)
                {
                    VibeLogger.LogWarning(
                        "server_loop_unexpected_exit",
                        "ServerLoopAsync exited while _isRunning was still true. Cleaning up and triggering recovery.",
                        new { cancellationRequested = cancellationToken.IsCancellationRequested }
                    );

                    CleanupAfterUnexpectedLoopExit();
                    ServerLoopExited?.Invoke();
                }
            }
        }

        private async Task<BridgeClientConnection> AcceptClientAsync(IBridgeTransportListener listener, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() => listener.AcceptClient(cancellationToken), cancellationToken);
            }
            catch (ThreadAbortException ex)
            {
                // Log and re-throw ThreadAbortException
                if (!DomainReloadStateRegistry.IsDomainReloadInProgress())
                {
                    VibeLogger.LogWarning(
                        "accept_thread_abort",
                        $"Unexpected thread abort: {ex.Message}");
                }
                throw;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        public void Dispose()
        {
            StopServer();
            CancellationTokenSource cancellationTokenSource = TakeCancellationTokenSource();
            cancellationTokenSource?.Dispose();
            _transportListener = null;
            _serverTask = null;
        }
    }
}
