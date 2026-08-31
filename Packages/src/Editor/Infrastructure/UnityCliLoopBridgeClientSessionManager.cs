using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Owns connected client streams, handler tasks, and per-request project IPC session processing.
    /// </summary>
    internal sealed class UnityCliLoopBridgeClientSessionManager
    {
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

        private readonly ConcurrentDictionary<string, Stream> _clientStreams = new();
        private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
        private int _nextClientTaskId;

        internal UnityCliLoopBridgeClientSessionManager(
            JsonRpcRequestProcessor jsonRpcRequestProcessor,
            UnityCliLoopBridgeHeartbeatService heartbeatService,
            UnityCliLoopBridgeClientDisconnectMonitor clientDisconnectMonitor)
        {
            System.Diagnostics.Debug.Assert(jsonRpcRequestProcessor != null, "jsonRpcRequestProcessor must not be null");
            System.Diagnostics.Debug.Assert(heartbeatService != null, "heartbeatService must not be null");
            System.Diagnostics.Debug.Assert(clientDisconnectMonitor != null, "clientDisconnectMonitor must not be null");

            _jsonRpcRequestProcessor = jsonRpcRequestProcessor;
            _heartbeatService = heartbeatService;
            _clientDisconnectMonitor = clientDisconnectMonitor;
        }

        /// <summary>
        /// Explicitly disconnect all connected clients
        /// This ensures CLI clients receive proper close events
        /// </summary>
        internal void DisconnectAllClients()
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

            // Why: bridge teardown must not leave Play Mode paused after clients are forced off.
            UloopPausePointRegistry.ResumeEditorPauseForClientDisconnect();
        }

        internal void StartClientHandler(BridgeClientConnection client, CancellationToken cancellationToken)
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

        internal Task[] GetActiveClientTasks()
        {
            return _clientTasks.Values.ToArray();
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
                            await UnityCliLoopBridgeResponseWriter.WriteJsonResponseLockedAsync(
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
                                    heartbeatJson => UnityCliLoopBridgeResponseWriter.WriteJsonResponseLockedAsync(
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

                    await UnityCliLoopBridgeResponseWriter.WriteJsonResponseLockedAsync(
                        stream, streamWriteLock, responseJson, serverCancellationToken);
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
    }
}
