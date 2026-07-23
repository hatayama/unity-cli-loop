using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Class specialized in handling JSON-RPC 2.0 processing
    /// 
    /// Related classes:
    /// - UnityCliLoopExecutionRouter: Executes Unity requests based on JSON-RPC requests
    /// - Project IPC server: Receives JSON-RPC messages from CLI clients
    /// - MainThreadSwitcher: Ensures Unity API calls run on the main thread
    /// - JsonRpcRequest: Request model for JSON-RPC 2.0 protocol
    /// 
    /// Processing flow:
    /// 1. Receives JSON message from the project IPC server
    /// 2. Parses and validates JSON-RPC 2.0 format
    /// 3. Delegates to UnityCliLoopExecutionRouter for execution
    /// 4. Formats response according to JSON-RPC 2.0 specification
    /// 5. Returns JSON response to be sent back to client
    /// </summary>
    internal sealed class JsonRpcRequestProcessor
    {
        private readonly UnityCliLoopExecutionRouter _executionRouter;

        internal delegate Task JsonRpcEarlyResponseWriter(
            string responseJson,
            bool cancelOnClientDisconnect,
            Func<string> createHeartbeatJson);

        internal JsonRpcRequestProcessor(UnityCliLoopExecutionRouter executionRouter)
        {
            System.Diagnostics.Debug.Assert(executionRouter != null, "executionRouter must not be null");

            _executionRouter = executionRouter
                ?? throw new ArgumentNullException(nameof(executionRouter));
        }

        /// <summary>
        /// Process JSON-RPC request and generate response
        /// </summary>
        public async Task<string> ProcessRequest(string jsonRequest, CancellationToken ct)
        {
            return await ProcessRequestWithEarlyResponseAsync(jsonRequest, ct, null);
        }

        internal async Task<string> ProcessRequestWithEarlyResponseAsync(
            string jsonRequest,
            CancellationToken ct,
            JsonRpcEarlyResponseWriter earlyResponseWriter)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                JsonRpcRequest request = ParseRequest(jsonRequest);
                
                if (request.IsNotification)
                {
                    ProcessNotification(request);
                    return null;
                }
                
                return await ProcessRpcRequest(request, jsonRequest, ct, earlyResponseWriter);
            }
            catch (JsonReaderException ex)
            {
                return JsonRpcResponseFactory.CreateErrorResponse(null, ex);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return JsonRpcResponseFactory.CreateErrorResponse(null, ex);
            }
        }

        /// <summary>
        /// Parse JSON-RPC request string into structured object
        /// </summary>
        private static JsonRpcRequest ParseRequest(string jsonRequest)
        {
            return UloopEnvelope.ParseJsonRpcRequest(jsonRequest);
        }

        /// <summary>
        /// Process notification (fire-and-forget)
        ///
        /// Note: Notifications are one-way messages without response.
        /// Currently handles:
        /// - focus-window: Brings Unity Editor window to foreground (used after request timeout)
        /// </summary>
        private static void ProcessNotification(JsonRpcRequest request)
        {
            if (string.IsNullOrEmpty(request.Method))
            {
                return;
            }

            switch (request.Method)
            {
                case "focus-window":
                    HandleFocusWindowNotification();
                    break;
                default:
                    // Unknown notification - ignore silently
                    break;
            }
        }

        /// <summary>
        /// Handle focus-window notification.
        /// Note: focus-window is handled at OS level by the CLI using native process control.
        /// This notification handler remains for protocol compatibility but does nothing.
        /// </summary>
        private static void HandleFocusWindowNotification()
        {
            // Intentionally empty because focus-window is handled by the CLI at OS level.
        }

        /// <summary>
        /// Process RPC request and return response JSON
        /// </summary>
        private async Task<string> ProcessRpcRequest(
            JsonRpcRequest request,
            string originalJson,
            CancellationToken ct,
            JsonRpcEarlyResponseWriter earlyResponseWriter)
        {
            // Why: keep an unfocused editor ticking for the request duration plus a trailing window
            // so compile/test work continues in the background without an OS focus kick.
            using (AutoTickPumpService.BeginScope())
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (IsCliProtocolMismatch(request.ClientProtocolVersion))
                    {
                        return JsonRpcResponseFactory.CreateCliProtocolMismatchResponse(
                            request.Id,
                            request.ClientProjectRunnerVersion,
                            request.ClientProtocolVersion);
                    }

                    if (request.AcceptsDispatchAck && earlyResponseWriter != null)
                    {
                        int heartbeatIntervalSeconds = request.AcceptsHeartbeat
                            ? UnityCliLoopServerConfig.HEARTBEAT_INTERVAL_SECONDS
                            : 0;
                        Func<string> createHeartbeatJson = request.AcceptsHeartbeat
                            ? () => JsonRpcResponseFactory.CreateHeartbeatResponse(
                                request.Id,
                                EditorMainThreadLivenessTracker.SecondsSinceLastMainThreadTick())
                            : null;
                        await earlyResponseWriter(
                            JsonRpcResponseFactory.CreateDispatchAcceptedResponse(request.Id, heartbeatIntervalSeconds),
                            ShouldCancelAcceptedRequestOnClientDisconnect(request),
                            createHeartbeatJson);
                    }

                    Stopwatch requestStopwatch = Stopwatch.StartNew();

                    Stopwatch executeMethodStopwatch = Stopwatch.StartNew();
                    UnityCliLoopToolResponse result = await ExecuteMethod(request.Method, request.Params, ct);
                    executeMethodStopwatch.Stop();

                    JsonRpcResponseFactory.AppendTimingIfRequested(
                        result,
                        $"[Perf] RpcExecuteMethod: {executeMethodStopwatch.Elapsed.TotalMilliseconds:F1}ms");
                    JsonRpcResponseFactory.AppendTimingIfRequested(
                        result,
                        $"[Perf] RpcBeforeSerializeTotal: {requestStopwatch.Elapsed.TotalMilliseconds:F1}ms");

                    string response = JsonRpcResponseFactory.CreateSuccessResponse(request.Id, result);
                    return response;
                }
                catch (JsonSerializationException ex)
                {
                    UnityEngine.Debug.LogError($"[JsonRpcRequestProcessor] JSON serialization error: {ex.Message}\nStack trace: {ex.StackTrace}");
                    return JsonRpcResponseFactory.CreateErrorResponse(request.Id, ex);
                }
                catch (UnityCliLoopToolParameterValidationException ex)
                {
                    LogUnityCliLoopToolParameterValidationException(ex);
                    return JsonRpcResponseFactory.CreateErrorResponse(request.Id, ex);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    LogRpcExceptionIfNeeded(ex);
                    return JsonRpcResponseFactory.CreateErrorResponse(request.Id, ex);
                }
            }
        }

        private static void LogRpcExceptionIfNeeded(Exception ex)
        {
            if (ex is UnityCliLoopToolBusyException)
            {
                return;
            }

            UnityEngine.Debug.LogError($"[JsonRpcRequestProcessor] Error: {ex.Message}\nStack trace: {ex.StackTrace}");
        }

        private static bool ShouldCancelAcceptedRequestOnClientDisconnect(JsonRpcRequest request)
        {
            System.Diagnostics.Debug.Assert(request != null, "request must not be null");

            bool? compileWaitsForDomainReload =
                JsonRpcCompileRequestMetadataReader.ReadWaitsForDomainReload(request.Params);
            return JsonRpcAcceptedRequestCancellationPolicy.ShouldCancelOnClientDisconnect(
                request.Method,
                compileWaitsForDomainReload);
        }

        private static bool IsCliProtocolMismatch(int? currentProtocolVersion)
        {
            if (currentProtocolVersion == null)
            {
                return true;
            }

            return currentProtocolVersion.Value != CliConstants.REQUIRED_CLI_PROTOCOL_VERSION;
        }

        private static void LogUnityCliLoopToolParameterValidationException(UnityCliLoopToolParameterValidationException exception)
        {
            UnityEngine.Debug.LogError(
                $"[JsonRpcRequestProcessor] Parameter validation error: {exception.Message}\nStack trace: {exception.StackTrace}");
        }

        /// <summary>
        /// Execute appropriate handler according to method name
        /// Use new command-based structure
        /// </summary>
        private async Task<UnityCliLoopToolResponse> ExecuteMethod(
            string method,
            JToken paramsToken,
            CancellationToken ct)
        {
            return await _executionRouter.ExecuteAsync(method, paramsToken, ct);
        }
    }
}

