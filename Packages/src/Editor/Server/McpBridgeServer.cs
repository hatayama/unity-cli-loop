using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    // Related classes:
    // - McpServerController: Manages the lifecycle of this server.
    // - UnityCommandExecutor: Executes commands received from clients.
    // - JsonRpcProcessor: Handles JSON-RPC 2.0 message processing.
    public class ConnectedClient
    {
        public readonly string Endpoint;
        public readonly string ClientName; 
        public readonly DateTime ConnectedAt;
        public readonly Stream Stream;

        public ConnectedClient(string endpoint, Stream stream, string clientName = McpConstants.UNKNOWN_CLIENT_NAME)
        {
            Endpoint = endpoint;
            Stream = stream; // Allow null stream for UI display purposes
            ClientName = clientName;
            ConnectedAt = DateTime.Now;
        }
        
        // Private constructor for WithClientName to preserve ConnectedAt
        private ConnectedClient(string endpoint, Stream stream, string clientName, DateTime connectedAt)
        {
            Endpoint = endpoint;
            Stream = stream; // Allow null stream for UI display purposes
            ClientName = clientName;
            ConnectedAt = connectedAt;
        }
        
        public ConnectedClient WithClientName(string clientName)
        {
            return new ConnectedClient(Endpoint, Stream, clientName, ConnectedAt);
        }

    }

    /// <summary>
    /// Unity CLI bridge server.
    /// Accepts project-local CLI connections and handles JSON-RPC 2.0 communication.
    /// </summary>
    public class McpBridgeServer : IDisposable
    {
        // Note: Domain reload progress is now tracked via McpSessionManager
        
        // Events for server lifecycle notifications
        public static event System.Action OnServerStopping;
        public static event System.Action OnServerStarted;
        
        // Events for individual tool management
        public static event System.Action<ConnectedClient> OnToolConnected;
        public static event System.Action<string> OnToolDisconnected;
        public static event System.Action OnAllToolsCleared;

        // Fired from thread pool when ServerLoopAsync exits while _isRunning is still true.
        // Subscribers must marshal to main thread before accessing Unity APIs.
        public static event System.Action OnServerLoopExited;
        
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
        
        // Client management for broadcasting notifications
        private readonly ConcurrentDictionary<string, ConnectedClient> _connectedClients = new();
        
        /// <summary>
        /// Whether the server is running.
        /// </summary>
        public bool IsRunning => _isRunning;
        
        public string Endpoint => _transportListener?.Endpoint.DisplayName() ?? string.Empty;
        
        /// <summary>
        /// Event on client connection.
        /// </summary>
        public event Action<string> OnClientConnected;
        
        /// <summary>
        /// Event on client disconnection.
        /// </summary>
        public event Action<string> OnClientDisconnected;
        
        /// <summary>
        /// Event on error.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Generate unique client key using Endpoint
        /// </summary>
        private string GenerateClientKey(string endpoint)
        {
            // Use endpoint as unique identifier
            return endpoint;
        }

        /// <summary>
        /// Get list of connected clients sorted by name
        /// </summary>
        public IReadOnlyCollection<ConnectedClient> GetConnectedClients()
        {
            return _connectedClients.Values.OrderBy(client => client.ClientName).ToArray();
        }

        /// <summary>
        /// Update client name for a connected client
        /// </summary>
        public void UpdateClientName(string clientEndpoint, string clientName)
        {
            // Find client by endpoint (backward compatibility)
            ConnectedClient targetClient = _connectedClients.Values
                .FirstOrDefault(c => c.Endpoint == clientEndpoint);
                
            if (targetClient != null)
            {
                string clientKey = GenerateClientKey(targetClient.Endpoint);
                ConnectedClient updatedClient = targetClient.WithClientName(clientName);
                bool updateResult = _connectedClients.TryUpdate(clientKey, updatedClient, targetClient);
                
                // Clear reconnecting flags when client name is successfully set (client is now fully connected)
                if (updateResult && clientName != McpConstants.UNKNOWN_CLIENT_NAME)
                {
                    McpServerController.ClearReconnectingFlag();
                    
                    // Notify tool connected
                    OnToolConnected?.Invoke(updatedClient);
                }
            }
        }

        public void StartServer(bool clearServerStartingLockWhenReady = true)
        {
            if (_isRunning)
            {
                return;
            }

            BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(Application.dataPath + "/..");
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

                // Notify that server has started
                OnServerStarted?.Invoke();
                
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

            // Determine shutdown reason based on domain reload state
            ServerShutdownReason shutdownReason = McpEditorSettings.GetIsDomainReloadInProgress()
                ? ServerShutdownReason.DomainReload
                : ServerShutdownReason.EditorQuit;

            // Send shutdown notification to all connected clients BEFORE disconnecting
            // This allows TypeScript side to differentiate between temporary and permanent shutdown
            SendShutdownNotification(shutdownReason);

            // Notify that server is stopping
            OnServerStopping?.Invoke();

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
                _serverTask?.Wait(TimeSpan.FromSeconds(McpServerConfig.SHUTDOWN_TIMEOUT_SECONDS));
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
        /// This ensures TypeScript clients receive proper close events
        /// </summary>
        private void DisconnectAllClients()
        {
            DisconnectAllClientsCore(notifyLifecycleEvents: true);
        }

        /// <summary>
        /// OnAllToolsCleared subscribers (UI components) require the main thread.
        /// Background-thread callers (CleanupAfterUnexpectedLoopExit) must suppress
        /// lifecycle events to avoid cross-thread Unity API violations.
        /// </summary>
        private void DisconnectAllClientsCore(bool notifyLifecycleEvents)
        {
            if (_connectedClients.IsEmpty)
            {
                return;
            }

            List<string> clientsToRemove = new List<string>();

            foreach (KeyValuePair<string, ConnectedClient> client in _connectedClients)
            {
                try
                {
                    // Close the NetworkStream to send proper close event to TypeScript client
                    if (client.Value.Stream != null && client.Value.Stream.CanWrite)
                    {
                        client.Value.Stream.Close();
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
                _connectedClients.TryRemove(clientKey, out _);
            }

            // Lifecycle events require main thread — only fire when explicitly requested
            if (notifyLifecycleEvents && !McpEditorSettings.GetIsDomainReloadInProgress())
            {
                OnAllToolsCleared?.Invoke();
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

            DisconnectAllClientsCore(notifyLifecycleEvents: false);

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
                            string clientEndpoint = client.Endpoint;
                            OnClientConnected?.Invoke(clientEndpoint);

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
                        if (!McpEditorSettings.GetIsDomainReloadInProgress())
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
                    OnServerLoopExited?.Invoke();
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
                if (!McpEditorSettings.GetIsDomainReloadInProgress())
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
            
            // Initialize new components for Content-Length framing
            DynamicBufferManager bufferManager = null;
            MessageReassembler messageReassembler = null;
            
            try
            {
                using (client)
                using (Stream stream = client.Stream)
                {
                    
                    // Check for existing connection from same endpoint and close it
                    string clientKey = GenerateClientKey(clientEndpoint);
                    if (_connectedClients.TryGetValue(clientKey, out ConnectedClient existingClient))
                    {
                        existingClient.Stream?.Close();
                        _connectedClients.TryRemove(clientKey, out _);
                        
                        // Notify tool disconnected
                        OnToolDisconnected?.Invoke(existingClient.ClientName);
                    }
                    
                    // Add new client to connected clients for notification broadcasting
                    ConnectedClient connectedClient = new ConnectedClient(clientEndpoint, stream);
                    _connectedClients.TryAdd(clientKey, connectedClient);
                    
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
                            
                            // Only send response if it's not null (notifications return null)
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
                
                // Remove client from connected clients list
                // Find client by endpoint to get the correct key
                ConnectedClient clientToRemove = _connectedClients.Values
                    .FirstOrDefault(c => c.Endpoint == clientEndpoint);
                    
                if (clientToRemove != null)
                {
                    string clientKey = GenerateClientKey(clientToRemove.Endpoint);
                    _connectedClients.TryRemove(clientKey, out _);
                    
                    // Notify tool disconnected
                    OnToolDisconnected?.Invoke(clientToRemove.ClientName);
                }
                
                client.Dispose();
                OnClientDisconnected?.Invoke(clientEndpoint);
            }
        }

        /// <summary>
        /// Sends a pre-formatted JSON-RPC notification to all connected clients using Content-Length framing.
        /// </summary>
        /// <param name="notificationJson">The complete JSON-RPC notification string</param>
        public void SendNotificationToClients(string notificationJson)
        {
            if (_connectedClients.IsEmpty)
            {
                return;
            }

            // Frame the notification with Content-Length header
            string framedNotification = CreateContentLengthFrame(notificationJson);
            byte[] notificationData = Encoding.UTF8.GetBytes(framedNotification);

            SendNotificationDataAsync(notificationData).Forget();
        }

        /// <summary>
        /// Sends a shutdown notification to all connected clients.
        /// This should be called before disconnecting clients so TypeScript side can
        /// differentiate between domain reload (temporary) and editor quit (permanent).
        /// </summary>
        /// <param name="reason">The reason for server shutdown</param>
        public void SendShutdownNotification(ServerShutdownReason reason)
        {
            if (_connectedClients.IsEmpty)
            {
                return;
            }

            // Create JSON-RPC 2.0 notification with shutdown reason
            string notificationJson = $"{{\"jsonrpc\":\"2.0\",\"method\":\"notifications/server/shutdown\",\"params\":{{\"reason\":\"{reason}\"}}}}";

            // Frame the notification with Content-Length header
            string framedNotification = CreateContentLengthFrame(notificationJson);
            byte[] notificationData = Encoding.UTF8.GetBytes(framedNotification);

            // Send synchronously to ensure it's sent before connection closes
            SendNotificationDataSync(notificationData);
        }

        /// <summary>
        /// Send notification data to all connected clients synchronously.
        /// Used for shutdown notifications to ensure delivery before connection closes.
        /// </summary>
        private void SendNotificationDataSync(byte[] notificationData)
        {
            foreach (KeyValuePair<string, ConnectedClient> client in _connectedClients)
            {
                try
                {
                    if (client.Value.Stream?.CanWrite == true)
                    {
                        client.Value.Stream.Write(notificationData, 0, notificationData.Length);
                        client.Value.Stream.Flush();
                    }
                }
                catch (Exception)
                {
                    // Ignore errors during shutdown notification - client may already be disconnected
                }
            }
        }

        /// <summary>
        /// Send notification data to all connected clients
        /// </summary>
        private async Task SendNotificationDataAsync(byte[] notificationData)
        {
            List<string> clientsToRemove = new List<string>();
            
            foreach (KeyValuePair<string, ConnectedClient> client in _connectedClients)
            {
                try
                {
                    if (client.Value.Stream?.CanWrite == true)
                    {
                        await client.Value.Stream.WriteAsync(notificationData, 0, notificationData.Length);
                    }
                    else
                    {
                        clientsToRemove.Add(client.Key);
                    }
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Error writing notification to client {client.Key}: {ex.Message}");
                    clientsToRemove.Add(client.Key);
                }
            }
            
            // Remove disconnected clients
            foreach (string clientKey in clientsToRemove)
            {
                if (_connectedClients.TryRemove(clientKey, out ConnectedClient removedClient))
                {
                    // Notify tool disconnected
                    OnToolDisconnected?.Invoke(removedClient.ClientName);
                }
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
