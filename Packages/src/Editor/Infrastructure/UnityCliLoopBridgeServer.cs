using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Creates Unity CLI Loop Bridge Server Instance instances with the dependencies required by this module.
    /// </summary>
    public sealed class UnityCliLoopBridgeServerInstanceFactory :
        IUnityCliLoopServerInstanceFactory,
        IUnityCliLoopServerLifecycleSource
    {
        public event Action ServerLoopExited;
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;

        internal UnityCliLoopBridgeServerInstanceFactory(
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            _domainReloadDetectionService = domainReloadDetectionService
                ?? throw new ArgumentNullException(nameof(domainReloadDetectionService));
            _toolRegistrarService = toolRegistrarService
                ?? throw new ArgumentNullException(nameof(toolRegistrarService));
        }

        public IUnityCliLoopServerInstance Create()
        {
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();
            UnityCliLoopBridgeClientDisconnectMonitor clientDisconnectMonitor = new();
            UnityCliLoopExecutionRouter executionRouter = new(_toolRegistrarService);
            JsonRpcRequestProcessor jsonRpcRequestProcessor = new(executionRouter);
            UnityCliLoopBridgeServer server = new(
                _domainReloadDetectionService,
                jsonRpcRequestProcessor,
                heartbeatService,
                clientDisconnectMonitor);
            server.ServerLoopExited += NotifyServerLoopExited;

            return server;
        }

        private void NotifyServerLoopExited()
        {
            ServerLoopExited?.Invoke();
        }
    }

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
        private readonly JsonRpcRequestProcessor _jsonRpcRequestProcessor;
        private readonly UnityCliLoopBridgeHeartbeatService _heartbeatService;
        private readonly UnityCliLoopBridgeClientDisconnectMonitor _clientDisconnectMonitor;
        
        // HResult error codes for normal disconnection detection
        private static readonly HashSet<int> NormalDisconnectionHResults = new()
        {
            unchecked((int)0x800703E3), // ERROR_OPERATION_ABORTED
            unchecked((int)0x80070040), // ERROR_NETNAME_DELETED
            unchecked((int)0x80072745), // ERROR_CONNECTION_ABORTED
            unchecked((int)0x80072746)  // ERROR_CONNECTION_RESET
        };
        
        private IBridgeTransportListener _transportListener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _serverTask;
        // Read from thread pool (ServerLoopAsync), written from main thread (StopServer)
        private volatile bool _isRunning = false;

        // Guard against concurrent cleanup from ServerLoopAsync finally + external disposal
        private int _unexpectedExitCleanupStarted = 0;
        
        private readonly ConcurrentDictionary<string, Stream> _clientStreams = new();
        private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
        private int _nextClientTaskId;

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
            _jsonRpcRequestProcessor = jsonRpcRequestProcessor
                ?? throw new ArgumentNullException(nameof(jsonRpcRequestProcessor));
            _heartbeatService = heartbeatService
                ?? throw new ArgumentNullException(nameof(heartbeatService));
            _clientDisconnectMonitor = clientDisconnectMonitor
                ?? throw new ArgumentNullException(nameof(clientDisconnectMonitor));
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
            DisconnectAllClients();
            
            try
            {
                _transportListener?.Stop();
            }
            finally
            {
                _transportListener = null;
            }

            Task serverTask = _serverTask;
            Task[] clientTasks = GetActiveClientTasks();
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
        /// Explicitly disconnect all connected clients
        /// This ensures CLI clients receive proper close events
        /// </summary>
        private void DisconnectAllClients()
        {
            if (_clientStreams.IsEmpty)
            {
                return;
            }

            List<string> clientsToRemove = new();

            foreach (KeyValuePair<string, Stream> client in _clientStreams)
            {
                try
                {
                    if (client.Value != null && client.Value.CanWrite)
                    {
                        client.Value.Close();
                    }
                    clientsToRemove.Add(client.Key);
                }
                catch (Exception ex)
                {
                    VibeLogger.LogWarning(
                        "client_disconnect_failed",
                        $"Error disconnecting client {client.Key}: {ex.Message}");
                    clientsToRemove.Add(client.Key); // Remove even if disconnect failed
                }
            }

            // Remove all clients from the connected clients list
            foreach (string clientKey in clientsToRemove)
            {
                _clientStreams.TryRemove(clientKey, out _);
            }
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

            DisconnectAllClients();

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
                            StartClientHandler(client, cancellationToken);
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

        private void StartClientHandler(BridgeClientConnection client, CancellationToken cancellationToken)
        {
            int taskId = Interlocked.Increment(ref _nextClientTaskId);
            Task clientTask = Task.Run(() => HandleClientAsync(client, cancellationToken));
            _clientTasks.TryAdd(taskId, clientTask);
            clientTask.ContinueWith(
                task =>
                {
                    _clientTasks.TryRemove(taskId, out _);
                    if (task.IsFaulted && task.Exception != null)
                    {
                        // HandleClientAsync catches expected failures itself, so a faulted task is
                        // an anomaly worth surfacing in the console for non-debug installs.
                        Debug.LogError(
                            $"[{UnityCliLoopConstants.PROJECT_NAME}] Client handler task faulted: {task.Exception.GetBaseException()}");
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private Task[] GetActiveClientTasks()
        {
            return _clientTasks.Values.ToArray();
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
        /// Handles communication with the client using Content-Length framing.
        /// </summary>
        private async Task HandleClientAsync(BridgeClientConnection client, CancellationToken cancellationToken)
        {
            string clientKey = client.Endpoint;
            
            // Initialize new components for Content-Length framing
            DynamicBufferManager bufferManager = null;
            MessageReassembler messageReassembler = null;
            
            try
            {
                using (client)
                using (Stream stream = client.Stream)
                {
                    
                    // Check for existing connection from same endpoint and close it
                    if (_clientStreams.TryRemove(clientKey, out Stream existingStream))
                    {
                        existingStream?.Close();
                    }
                    
                    _clientStreams.TryAdd(clientKey, stream);
                    
                    // Initialize new framing components
                    bufferManager = new DynamicBufferManager();
                    messageReassembler = new MessageReassembler(bufferManager);

                    // Not disposed deliberately: a heartbeat writer could still be releasing it
                    // during teardown, and an undisposed SemaphoreSlim holds no unmanaged state.
                    SemaphoreSlim streamWriteLock = new(1, 1);
                    
                    // Start with initial buffer size
                    byte[] buffer = bufferManager.GetBuffer(BufferConfig.INITIAL_BUFFER_SIZE);
                    
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        
                        if (bytesRead == 0)
                        {
                            break; // Client disconnected.
                        }
                        
                        // Add received data to message reassembler
                        messageReassembler.AddData(buffer, bytesRead);
                        
                        // Extract any complete messages
                        string[] completeJsonMessages = messageReassembler.ExtractCompleteMessages();
                        
                        foreach (string requestJson in completeJsonMessages)
                        {
                            if (string.IsNullOrWhiteSpace(requestJson)) continue;

                            await ProcessRequestFrameAsync(client, stream, streamWriteLock, requestJson, cancellationToken);
                        }
                        
                        // Why: false means the reassembler was disposed and can no longer make
                        // progress; closing the connection lets the CLI observe the disconnect
                        // immediately instead of waiting silently for its own timeout.
                        // (Corrupted framing state throws from ValidateState and is handled below.)
                        if (!messageReassembler.ValidateState())
                        {
                            break;
                        }
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // Treat as normal behavior if a domain reload is in progress.
                // No need to log thread aborts during domain reload
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation during server shutdown or domain reload
                // No logging needed as this is expected behavior during Unity Editor operations
            }
            catch (IOException ex)
            {
                // I/O errors are usually normal disconnections - only log as info instead of warning
                if (IsNormalDisconnectionException(ex))
                {
                    // Log normal disconnections as info level
                }
            }
            catch (Exception ex)
            {
                // Expected failures (cancellation, reload aborts, normal disconnects) are filtered
                // above, so anything reaching here means a CLI session died abnormally and the
                // console must say so even on installs where VibeLogger is compiled out.
                Debug.LogError(
                    $"[{UnityCliLoopConstants.PROJECT_NAME}] Client session for {clientKey} failed: {ex}");
            }
            finally
            {
                // Dispose of framing components
                try
                {
                    messageReassembler?.Dispose();
                    bufferManager?.Dispose();
                }
                catch (Exception ex)
                {
                    VibeLogger.LogWarning(
                        "client_dispose_failed",
                        $"Error during client disposal: {ex.Message}");
                }
                
                _clientStreams.TryRemove(clientKey, out _);
                
                client.Dispose();
            }
        }

        /// <summary>
        /// Creates a Content-Length framed message for JSON-RPC 2.0 communication.
        /// </summary>
        /// <param name="jsonContent">The JSON content to frame</param>
        /// <returns>The framed message with Content-Length header</returns>
        private string CreateContentLengthFrame(string jsonContent)
        {
            if (string.IsNullOrEmpty(jsonContent))
            {
                return string.Empty;
            }
            
            // Calculate content length in bytes (UTF-8 encoding)
            int contentLength = Encoding.UTF8.GetByteCount(jsonContent);
            
            // Create the framed message: Content-Length: <n>\r\n\r\n<json_content>
            return $"Content-Length: {contentLength}\r\n\r\n{jsonContent}";
        }

        private async Task WriteJsonResponseAsync(
            Stream stream,
            string responseJson,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(responseJson))
            {
                return;
            }

            if (!stream.CanWrite || ct.IsCancellationRequested)
            {
                return;
            }

            string framedResponse = CreateContentLengthFrame(responseJson);
            byte[] responseData = Encoding.UTF8.GetBytes(framedResponse);
            await stream.WriteAsync(responseData, 0, responseData.Length, ct);
        }

        private async Task ProcessRequestFrameAsync(
            BridgeClientConnection client,
            Stream stream,
            SemaphoreSlim streamWriteLock,
            string requestJson,
            CancellationToken serverCancellationToken)
        {
            using (CancellationTokenSource requestCancellationTokenSource =
                   CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken))
            {
                Task clientDisconnectMonitorTask = null;
                Task heartbeatTask = null;
                CancellationTokenSource heartbeatCancellationSource = null;
                try
                {
                    string responseJson = await _jsonRpcRequestProcessor.ProcessRequestWithEarlyResponseAsync(
                        requestJson,
                        requestCancellationTokenSource.Token,
                        async (responseJsonValue, cancelOnClientDisconnect, createHeartbeatJson) =>
                        {
                            await WriteJsonResponseLockedAsync(
                                stream, streamWriteLock, responseJsonValue, serverCancellationToken);

                            if (createHeartbeatJson != null)
                            {
                                heartbeatCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                                    requestCancellationTokenSource.Token);
                                // Why: the heartbeat token must govern the write as well; with the
                                // server token an in-flight write could ignore StopHeartbeatsAsync
                                // and stall the final response behind a slow client.
                                CancellationToken heartbeatToken = heartbeatCancellationSource.Token;
                                heartbeatTask = _heartbeatService.SendHeartbeatsAsync(
                                    createHeartbeatJson,
                                    heartbeatJson => WriteJsonResponseLockedAsync(
                                        stream, streamWriteLock, heartbeatJson, heartbeatToken),
                                    TimeSpan.FromSeconds(UnityCliLoopServerConfig.HEARTBEAT_INTERVAL_SECONDS),
                                    heartbeatToken);
                            }

                            if (!cancelOnClientDisconnect)
                            {
                                return;
                            }

                            clientDisconnectMonitorTask =
                                _clientDisconnectMonitor.MonitorClientDisconnectAsync(
                                    client,
                                    requestCancellationTokenSource);
                        });

                    // Stop heartbeats before the final response so no heartbeat frame can be
                    // queued after the response the CLI stops reading at.
                    await _heartbeatService.StopHeartbeatsAsync(heartbeatTask, heartbeatCancellationSource);
                    heartbeatTask = null;

                    await WriteJsonResponseLockedAsync(stream, streamWriteLock, responseJson, serverCancellationToken);
                }
                finally
                {
                    await _heartbeatService.StopHeartbeatsAsync(heartbeatTask, heartbeatCancellationSource);
                    heartbeatCancellationSource?.Dispose();
                    await _clientDisconnectMonitor.StopClientDisconnectMonitorAsync(
                        clientDisconnectMonitorTask,
                        requestCancellationTokenSource);
                }
            }
        }

        private async Task WriteJsonResponseLockedAsync(
            Stream stream,
            SemaphoreSlim streamWriteLock,
            string responseJson,
            CancellationToken ct)
        {
            // Why: heartbeat frames are written from a background timer while the final
            // response is written by the request task; interleaved writes would corrupt
            // Content-Length framing, so all frame writes share one lock per connection.
            await streamWriteLock.WaitAsync(ct);
            try
            {
                await WriteJsonResponseAsync(stream, responseJson, ct);
            }
            finally
            {
                streamWriteLock.Release();
            }
        }

        /// <summary>
        /// Determines if the given exception represents a normal client disconnection.
        /// </summary>
        /// <param name="ex">The exception to evaluate</param>
        /// <returns>True if the exception represents a normal disconnection, false otherwise</returns>
        private static bool IsNormalDisconnectionException(Exception ex)
        {
            switch (ex)
            {
                case SocketException sockEx:
                    return sockEx.SocketErrorCode is SocketError.ConnectionReset or
                                                     SocketError.ConnectionAborted or
                                                     SocketError.OperationAborted or
                                                     SocketError.Shutdown or
                                                     SocketError.NotConnected;
                    
                case ObjectDisposedException:
                    return true;
                    
                case IOException ioEx when ioEx.InnerException is SocketException innerSockEx:
                    return innerSockEx.SocketErrorCode is SocketError.ConnectionReset or
                                                          SocketError.ConnectionAborted or
                                                          SocketError.OperationAborted or
                                                          SocketError.Shutdown or
                                                          SocketError.NotConnected;
                
                case IOException ioEx:
                    // Check HResult codes for common disconnection scenarios
                    return NormalDisconnectionHResults.Contains(ioEx.HResult) ||
                           IsNormalDisconnectionByInnerException(ioEx);
                    
                default:
                    return false;
            }
        }

        /// <summary>
        /// Recursively checks inner exceptions for normal disconnection scenarios
        /// </summary>
        /// <param name="ex">The exception to check</param>
        /// <returns>True if any inner exception indicates a normal disconnection</returns>
        private static bool IsNormalDisconnectionByInnerException(Exception ex)
        {
            Exception innerEx = ex.InnerException;
            while (innerEx != null)
            {
                if (IsNormalDisconnectionException(innerEx))
                {
                    return true;
                }
                innerEx = innerEx.InnerException;
            }
            return false;
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
