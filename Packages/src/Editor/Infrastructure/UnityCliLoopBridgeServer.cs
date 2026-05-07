using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
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
        public event Action ServerStarted;
        public event Action ServerStopping;
        public event Action ServerLoopExited;

        public IUnityCliLoopServerInstance Create()
        {
            UnityCliLoopBridgeServer server = new();
            server.ServerStarted += NotifyServerStarted;
            server.ServerStopping += NotifyServerStopping;
            server.ServerLoopExited += NotifyServerLoopExited;

            return server;
        }

        private void NotifyServerStarted()
        {
            ServerStarted?.Invoke();
        }

        private void NotifyServerStopping()
        {
            ServerStopping?.Invoke();
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
        public event Action ServerStopping;
        public event Action ServerStarted;

        // Fired from thread pool when ServerLoopAsync exits while _isRunning is still true.
        // Subscribers must marshal to main thread before accessing Unity APIs.
        public event Action ServerLoopExited;
        
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
        
        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public bool IsRunning => _isRunning;
        
        public string Endpoint => _transportListener?.Endpoint.DisplayName() ?? string.Empty;
        
        /// <summary>
        /// Event on error.
        /// </summary>
        public event Action<string> OnError;

        private string GenerateClientKey(string endpoint)
        {
            return endpoint;
        }

        public void StartServer(bool clearServerStartingLockWhenReady = true)
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

                // Server is now ready to accept connections - clean up compilation/reload locks.
                CompilationLockService.DeleteLockFile();
                DomainReloadDetectionService.DeleteLockFile();
                if (clearServerStartingLockWhenReady)
                {
                    ServerStartingLockService.DeleteLockFile();
                }

                ServerStarted?.Invoke();
                
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                _isRunning = false;
                string errorMessage = $"Project IPC endpoint is already in use: {endpoint.DisplayName()}";
                OnError?.Invoke(errorMessage);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                string errorMessage = $"Failed to start Unity CLI bridge: {ex.Message}";
                OnError?.Invoke(errorMessage);
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

            // Notify that server is stopping
            ServerStopping?.Invoke();

            _isRunning = false;

            // Explicitly disconnect all connected clients before stopping the server
            DisconnectAllClients();
            
            // Request cancellation.
            _cancellationTokenSource?.Cancel();
            
            try
            {
                _transportListener?.Stop();
            }
            finally
            {
                _transportListener = null;
            }
            
            // Wait for the server task to complete.
            try
            {
                _serverTask?.Wait(TimeSpan.FromSeconds(UnityCliLoopServerConfig.SHUTDOWN_TIMEOUT_SECONDS));
            }
            finally
            {
                // Set the server task to null regardless of success/failure
                _serverTask = null;
            }
            
            // Dispose of the cancellation token source.
            try
            {
                _cancellationTokenSource?.Dispose();
            }
            finally
            {
                // Set the cancellation token source to null regardless of success/failure
                _cancellationTokenSource = null;
            }
            
            
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
                    OnError?.Invoke($"Error disconnecting client {client.Key}: {ex.Message}");
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

            try
            {
                _cancellationTokenSource?.Cancel();
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }

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
                            // Execute client handling in a separate task (fire-and-forget).
                            Task.Run(() => HandleClientAsync(client, cancellationToken)).Forget();
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
                        if (!UnityCliLoopEditorSettings.GetIsDomainReloadInProgress())
                        {
                            OnError?.Invoke($"Unexpected thread abort: {ex.Message}");
                        }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            string errorMessage = $"Server loop error: {ex.Message}";
                            OnError?.Invoke(errorMessage);
                        }
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
                if (!UnityCliLoopEditorSettings.GetIsDomainReloadInProgress())
                {
                    OnError?.Invoke($"Unexpected thread abort: {ex.Message}");
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
            string clientEndpoint = client.Endpoint;
            string clientKey = GenerateClientKey(clientEndpoint);
            
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
                            
                            // JSON-RPC processing and response sending with client context
                            string responseJson = await JsonRpcProcessor.ProcessRequest(requestJson, clientEndpoint);
                            
                            if (!string.IsNullOrEmpty(responseJson))
                            {
                                // Check stream and client state before attempting write
                                if (!stream.CanWrite || cancellationToken.IsCancellationRequested)
                                {
                                    return; // Skip the write operation
                                }
                                
                                // Send response with Content-Length framing
                                string framedResponse = CreateContentLengthFrame(responseJson);
                                byte[] responseData = Encoding.UTF8.GetBytes(framedResponse);
                                
                                await stream.WriteAsync(responseData, 0, responseData.Length, cancellationToken);
                            }
                        }
                        
                        // Validate reassembler state and clear if needed
                        if (!messageReassembler.ValidateState())
                        {
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
                OnError?.Invoke(ex.Message);
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
                    OnError?.Invoke($"Error during client disposal: {ex.Message}");
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
            _cancellationTokenSource?.Dispose();
            _transportListener = null;
            _serverTask = null;
        }
    }
} 
