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
    /// Design document reference: Packages/src/Editor/ARCHITECTURE.md
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

        // Shared by every response path; JsonConvert only reads the settings, so a single
        // instance avoids allocating identical settings per response.
        private static readonly JsonSerializerSettings ResponseSerializerSettings =
            JsonRpcResponseSerializer.Settings;

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
                return CreateErrorResponse(null, ex);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return CreateErrorResponse(null, ex);
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
            try
            {
                ct.ThrowIfCancellationRequested();
                if (IsCliProtocolMismatch(request.ClientProtocolVersion))
                {
                    return CreateCliProtocolMismatchResponse(
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
                        ? () => CreateHeartbeatResponse(
                            request.Id,
                            EditorMainThreadLivenessTracker.SecondsSinceLastMainThreadTick())
                        : null;
                    await earlyResponseWriter(
                        CreateDispatchAcceptedResponse(request.Id, heartbeatIntervalSeconds),
                        ShouldCancelAcceptedRequestOnClientDisconnect(request),
                        createHeartbeatJson);
                }

                Stopwatch requestStopwatch = Stopwatch.StartNew();

                Stopwatch executeMethodStopwatch = Stopwatch.StartNew();
                UnityCliLoopToolResponse result = await ExecuteMethod(request.Method, request.Params, ct);
                executeMethodStopwatch.Stop();

                AppendTimingIfRequested(
                    result,
                    $"[Perf] RpcExecuteMethod: {executeMethodStopwatch.Elapsed.TotalMilliseconds:F1}ms");
                AppendTimingIfRequested(
                    result,
                    $"[Perf] RpcBeforeSerializeTotal: {requestStopwatch.Elapsed.TotalMilliseconds:F1}ms");

                string response = CreateSuccessResponse(request.Id, result);
                return response;
            }
            catch (JsonSerializationException ex)
            {
                UnityEngine.Debug.LogError($"[JsonRpcRequestProcessor] JSON serialization error: {ex.Message}\nStack trace: {ex.StackTrace}");
                return CreateErrorResponse(request.Id, ex);
            }
            catch (UnityCliLoopToolParameterValidationException ex)
            {
                LogUnityCliLoopToolParameterValidationException(ex);
                return CreateErrorResponse(request.Id, ex);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                LogRpcExceptionIfNeeded(ex);
                return CreateErrorResponse(request.Id, ex);
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

        private static string CreateCliProtocolMismatchResponse(
            object id,
            string currentCliVersion,
            int? currentProtocolVersion)
        {
            JsonRpcErrorResponse errorResponse = new(
                UnityCliLoopServerConfig.JSONRPC_VERSION,
                id,
                new JsonRpcError(
                    UnityCliLoopServerConfig.INTERNAL_ERROR_CODE,
                    "The installed uloop CLI uses an IPC protocol that does not match this Unity package.",
                    new CliUpdateRequiredErrorData(
                        currentCliVersion,
                        currentProtocolVersion,
                        CliConstants.REQUIRED_CLI_PROTOCOL_VERSION,
                        GetCliUpdateCommandForProtocolMismatch(currentProtocolVersion))));

            return JsonConvert.SerializeObject(errorResponse, Formatting.None, ResponseSerializerSettings);
        }

        private static string GetCliUpdateCommandForProtocolMismatch(int? currentProtocolVersion)
        {
            if (currentProtocolVersion == null)
            {
                return CreateCliUpdateCommand();
            }

            if (currentProtocolVersion.Value < CliConstants.REQUIRED_CLI_PROTOCOL_VERSION)
            {
                return CreateCliUpdateCommand();
            }

            return null;
        }

        private static string CreateCliUpdateCommand()
        {
            return $"{CliConstants.EXECUTABLE_NAME} update";
        }

        internal static string CreateDispatchAcceptedResponse(object id, int heartbeatIntervalSeconds)
        {
            // heartbeatIntervalSeconds is only advertised when the client negotiated
            // heartbeats; older CLIs would treat unexpected extra frames as the final
            // response, so the field doubles as the negotiation answer.
            object uloopMetadata = heartbeatIntervalSeconds > 0
                ? new
                {
                    phase = JsonRpcResponsePhases.Accepted,
                    heartbeatIntervalSeconds
                }
                : (object)new
                {
                    phase = JsonRpcResponsePhases.Accepted
                };

            object response = new
            {
                jsonrpc = UnityCliLoopServerConfig.JSONRPC_VERSION,
                id,
                result = new
                {
                    accepted = true
                },
                uloop = uloopMetadata
            };

            return JsonConvert.SerializeObject(response, Formatting.None, ResponseSerializerSettings);
        }

        internal static string CreateHeartbeatResponse(object id, double mainThreadStallSeconds)
        {
            object response = new
            {
                jsonrpc = UnityCliLoopServerConfig.JSONRPC_VERSION,
                id,
                result = new
                {
                    alive = true
                },
                uloop = new
                {
                    phase = JsonRpcResponsePhases.Heartbeat,
                    mainThreadStallSeconds
                }
            };

            return JsonConvert.SerializeObject(response, Formatting.None, ResponseSerializerSettings);
        }

        private static void AppendTimingIfRequested(UnityCliLoopToolResponse result, string timing)
        {
            if (result is not IUnityCliLoopTimingResponse timingResponse)
            {
                return;
            }

            if (!timingResponse.EmitsTimingsInJsonResponse)
            {
                return;
            }

            timingResponse.AddTiming(timing);
        }

        private static void LogUnityCliLoopToolParameterValidationException(UnityCliLoopToolParameterValidationException exception)
        {
            UnityEngine.Debug.LogError(
                $"[JsonRpcRequestProcessor] Parameter validation error: {exception.Message}\nStack trace: {exception.StackTrace}");
        }

        /// <summary>
        /// Create JSON-RPC success response
        /// </summary>
        /// <param name="id">Request ID - must be same type as received (string/number/null per JSON-RPC spec)</param>
        /// <param name="result">Command execution result</param>
        private static string CreateSuccessResponse(object id, UnityCliLoopToolResponse result)
        {
            try
            {
                JsonRpcSuccessResponse response = new(
                    UnityCliLoopServerConfig.JSONRPC_VERSION,
                    id,
                    result
                );
                return JsonConvert.SerializeObject(response, Formatting.None, ResponseSerializerSettings);
            }
            catch (Exception)
            {
                // Return safe fallback response for any serialization errors
                object fallbackResult = new
                {
                    error = "Serialization failed - returning safe fallback",
                    commandType = result != null ? result.GetType().Name : "unknown"
                };
                
                JsonRpcSuccessResponse fallbackResponse = new(
                    UnityCliLoopServerConfig.JSONRPC_VERSION,
                    id,
                    fallbackResult
                );
                return JsonConvert.SerializeObject(fallbackResponse, Formatting.None, ResponseSerializerSettings);
            }
        }

        /// <summary>
        /// Create JSON-RPC error response
        /// </summary>
        /// <param name="id">Request ID - must be same type as received (string/number/null per JSON-RPC spec)</param>
        /// <param name="ex">Exception to convert to error response</param>
        private static string CreateErrorResponse(object id, Exception ex)
        {
            // Centralize exception -> user-facing message via UserFriendlyErrorConverter
            UserFriendlyErrorConverter handler = new();
            UserFriendlyErrorDto exceptionResponse = handler.ProcessException(ex);
            
            // Map UserFriendlyErrorDto to JsonRpcError
            string errorMessage = exceptionResponse.FriendlyMessage;

            JsonRpcErrorData errorData;
            if (ex is UnityCliLoopSecurityException secEx)
            {
                errorData = new SecurityBlockedErrorData(secEx.ToolName, secEx.SecurityReason, exceptionResponse.Explanation ?? ex.Message);
            }
            else if (ex is UnityCliLoopToolBusyException busyEx)
            {
                errorData = new ServerBusyErrorData(
                    busyEx.RunningToolName,
                    busyEx.RequestedToolName,
                    busyEx.IsPlaying,
                    busyEx.IsPaused,
                    exceptionResponse.Explanation ?? ex.Message);
            }
            else
            {
                errorData = new InternalErrorData(exceptionResponse.Explanation ?? ex.Message);
            }

            JsonRpcErrorResponse errorResponse = new(
                UnityCliLoopServerConfig.JSONRPC_VERSION,
                id,
                new JsonRpcError(UnityCliLoopServerConfig.INTERNAL_ERROR_CODE, errorMessage, errorData)
            );

            return JsonConvert.SerializeObject(errorResponse, Formatting.None, ResponseSerializerSettings);
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

    /// <summary>
    /// Constants for JSON-RPC error types
    /// </summary>
    public static class JsonRpcErrorTypes
    {
        public const string SecurityBlocked = "security_blocked";
        public const string InternalError = "internal_error";
        public const string CliUpdateRequired = "cli_update_required";
        public const string ServerBusy = "server_busy";
    }

    public static class JsonRpcResponsePhases
    {
        public const string Accepted = "accepted";
        public const string Heartbeat = "heartbeat";
    }

    /// <summary>
    /// Base class for JSON-RPC error data
    /// </summary>
    public abstract class JsonRpcErrorData
    {
        public abstract string type { get; }
        
        public string message { get; protected set; }
        
        protected JsonRpcErrorData(string message)
        {
            this.message = message;
        }
    }

    /// <summary>
    /// Error data for security blocked commands
    /// </summary>
    public class SecurityBlockedErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.SecurityBlocked;
        
        public string command { get; }
        
        public string reason { get; }
        
        public SecurityBlockedErrorData(string command, string reason, string message) : base(message)
        {
            this.command = command;
            this.reason = reason;
        }
    }

    /// <summary>
    /// Error data for internal errors
    /// </summary>
    public class InternalErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.InternalError;
        
        public InternalErrorData(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Keeps busy responses machine-readable so CLI clients can classify them as retryable.
    /// </summary>
    public class ServerBusyErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.ServerBusy;

        public string runningToolName { get; }

        public string requestedToolName { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isPlaying { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? isPaused { get; }

        public ServerBusyErrorData(
            string runningToolName,
            string requestedToolName,
            bool? isPlaying,
            bool? isPaused,
            string message)
            : base(message)
        {
            this.runningToolName = runningToolName;
            this.requestedToolName = requestedToolName;
            this.isPlaying = isPlaying;
            this.isPaused = isPaused;
        }
    }

    /// <summary>
    /// Carries protocol mismatch details plus optional CLI update instructions.
    /// </summary>
    public class CliUpdateRequiredErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.CliUpdateRequired;

        public string currentCliVersion { get; }

        public int? currentProtocolVersion { get; }

        public int requiredProtocolVersion { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string updateCommand { get; }

        public bool retryableAfterUpdate { get; }

        public CliUpdateRequiredErrorData(
            string currentCliVersion,
            int? currentProtocolVersion,
            int requiredProtocolVersion,
            string updateCommand) : base("Install matching uloop CLI and Unity package versions, then retry the original command.")
        {
            this.currentCliVersion = string.IsNullOrWhiteSpace(currentCliVersion) ? null : currentCliVersion;
            this.currentProtocolVersion = currentProtocolVersion;
            this.requiredProtocolVersion = requiredProtocolVersion;
            this.updateCommand = updateCommand;
            retryableAfterUpdate = true;
        }
    }

    /// <summary>
    /// JSON-RPC error object
    /// </summary>
    public class JsonRpcError
    {
        public int code { get; }
        
        public string message { get; }
        
        public JsonRpcErrorData data { get; }
        
        public JsonRpcError(int code, string message, JsonRpcErrorData data)
        {
            this.code = code;
            this.message = message;
            this.data = data;
        }
    }

    /// <summary>
    /// JSON-RPC success response
    /// </summary>
    public class JsonRpcSuccessResponse
    {
        public string jsonrpc { get; }
        
        public object id { get; }
        
        public object result { get; }
        
        public JsonRpcSuccessResponse(string jsonRpc, object id, object result)
        {
            this.jsonrpc = jsonRpc;
            this.id = id;
            this.result = result;
        }
    }

    /// <summary>
    /// JSON-RPC error response
    /// </summary>
    public class JsonRpcErrorResponse
    {
        public string jsonrpc { get; }
        
        public object id { get; }
        
        public JsonRpcError error { get; }
        
        public JsonRpcErrorResponse(string jsonRpc, object id, JsonRpcError error)
        {
            this.jsonrpc = jsonRpc;
            this.id = id;
            this.error = error;
        }
    }
} 
