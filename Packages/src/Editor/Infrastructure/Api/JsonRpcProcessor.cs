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
    /// - UnityApiHandler: Executes Unity commands based on JSON-RPC requests
    /// - Project IPC server: Receives JSON-RPC messages from CLI clients
    /// - MainThreadSwitcher: Ensures Unity API calls run on the main thread
    /// - JsonRpcRequest: Request model for JSON-RPC 2.0 protocol
    /// 
    /// Processing flow:
    /// 1. Receives JSON message from the project IPC server
    /// 2. Parses and validates JSON-RPC 2.0 format
    /// 3. Delegates to UnityApiHandler for command execution
    /// 4. Formats response according to JSON-RPC 2.0 specification
    /// 5. Returns JSON response to be sent back to client
    /// </summary>
    public static class JsonRpcProcessor
    {
        private const string WaitForDomainReloadParamName = "WaitForDomainReload";

        internal delegate Task JsonRpcEarlyResponseWriter(
            string responseJson,
            bool cancelOnClientDisconnect);

        /// <summary>
        /// Process JSON-RPC request and generate response
        /// </summary>
        public static async Task<string> ProcessRequest(string jsonRequest, CancellationToken ct)
        {
            return await ProcessRequestWithEarlyResponseAsync(jsonRequest, ct, null);
        }

        internal static async Task<string> ProcessRequestWithEarlyResponseAsync(
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
            JObject request = JObject.Parse(jsonRequest);
            return new JsonRpcRequest
            {
                Method = request["method"]?.ToString(),
                Params = request["params"],
                ClientCliVersion = ReadClientCliVersion(request),
                AcceptsDispatchAck = ReadAcceptsDispatchAck(request),
                Id = request["id"]?.ToObject<object>()
            };
        }

        private static string ReadClientCliVersion(JObject request)
        {
            JObject metadata = request["uloop"] as JObject;
            if (metadata == null)
            {
                return null;
            }

            string cliVersion = metadata["cliVersion"]?.ToString();
            return string.IsNullOrWhiteSpace(cliVersion) ? null : cliVersion;
        }

        private static bool ReadAcceptsDispatchAck(JObject request)
        {
            JObject metadata = request["uloop"] as JObject;
            if (metadata == null)
            {
                return false;
            }

            return metadata["acceptsDispatchAck"]?.Value<bool>() ?? false;
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
        private static async Task<string> ProcessRpcRequest(
            JsonRpcRequest request,
            string originalJson,
            CancellationToken ct,
            JsonRpcEarlyResponseWriter earlyResponseWriter)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (IsCliUpdateRequired(request.ClientCliVersion))
                {
                    return CreateCliUpdateRequiredResponse(request.Id, request.ClientCliVersion);
                }

                if (request.AcceptsDispatchAck && earlyResponseWriter != null)
                {
                    await earlyResponseWriter(
                        CreateDispatchAcceptedResponse(request.Id),
                        ShouldCancelAcceptedRequestOnClientDisconnect(request));
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
                UnityEngine.Debug.LogError($"[JsonRpcProcessor] JSON serialization error: {ex.Message}\nStack trace: {ex.StackTrace}");
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

            UnityEngine.Debug.LogError($"[JsonRpcProcessor] Error: {ex.Message}\nStack trace: {ex.StackTrace}");
        }

        private static bool ShouldCancelAcceptedRequestOnClientDisconnect(JsonRpcRequest request)
        {
            System.Diagnostics.Debug.Assert(request != null, "request must not be null");

            if (request.Method != UnityCliLoopConstants.TOOL_NAME_COMPILE)
            {
                return true;
            }

            return !CompileRequestWaitsForDomainReload(request.Params);
        }

        private static bool CompileRequestWaitsForDomainReload(JToken paramsToken)
        {
            if (paramsToken is not JObject paramsObject)
            {
                return true;
            }

            JToken waitForDomainReloadToken =
                paramsObject.GetValue(WaitForDomainReloadParamName, StringComparison.OrdinalIgnoreCase);
            if (waitForDomainReloadToken == null)
            {
                return true;
            }

            if (waitForDomainReloadToken.Type != JTokenType.Boolean)
            {
                return true;
            }

            return waitForDomainReloadToken.Value<bool>();
        }

        private static bool IsCliUpdateRequired(string currentCliVersion)
        {
            if (string.IsNullOrWhiteSpace(currentCliVersion))
            {
                return true;
            }

            return !CliVersionComparer.IsVersionGreaterThanOrEqual(
                currentCliVersion,
                CliConstants.MINIMUM_REQUIRED_CLI_VERSION);
        }

        private static string CreateCliUpdateRequiredResponse(object id, string currentCliVersion)
        {
            string requiredCliVersion = CliConstants.MINIMUM_REQUIRED_CLI_VERSION;
            JsonRpcErrorResponse errorResponse = new(
                UnityCliLoopServerConfig.JSONRPC_VERSION,
                id,
                new JsonRpcError(
                    UnityCliLoopServerConfig.INTERNAL_ERROR_CODE,
                    "The installed uloop CLI is too old for this Unity package.",
                    new CliUpdateRequiredErrorData(
                        currentCliVersion,
                        requiredCliVersion,
                        $"{CliConstants.EXECUTABLE_NAME} update",
                        $"{CliConstants.EXECUTABLE_NAME} update --to-version {requiredCliVersion}")));

            JsonSerializerSettings settings = new()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH
            };

            return JsonConvert.SerializeObject(errorResponse, Formatting.None, settings);
        }

        private static string CreateDispatchAcceptedResponse(object id)
        {
            object response = new
            {
                jsonrpc = UnityCliLoopServerConfig.JSONRPC_VERSION,
                id,
                result = new
                {
                    accepted = true
                },
                uloop = new
                {
                    phase = JsonRpcResponsePhases.Accepted
                }
            };

            JsonSerializerSettings settings = new()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH
            };

            return JsonConvert.SerializeObject(response, Formatting.None, settings);
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
                $"[JsonRpcProcessor] Parameter validation error: {exception.Message}\nStack trace: {exception.StackTrace}");
        }

        /// <summary>
        /// Create JSON-RPC success response
        /// </summary>
        /// <param name="id">Request ID - must be same type as received (string/number/null per JSON-RPC spec)</param>
        /// <param name="result">Command execution result</param>
        private static string CreateSuccessResponse(object id, UnityCliLoopToolResponse result)
        {
            JsonSerializerSettings settings = new()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH
            };
            
            try
            {
                JsonRpcSuccessResponse response = new(
                    UnityCliLoopServerConfig.JSONRPC_VERSION,
                    id,
                    result
                );
                return JsonConvert.SerializeObject(response, Formatting.None, settings);
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
                return JsonConvert.SerializeObject(fallbackResponse, Formatting.None);
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
            
            JsonSerializerSettings settings = new()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH
            };
            
            return JsonConvert.SerializeObject(errorResponse, Formatting.None, settings);
        }

        /// <summary>
        /// Execute appropriate handler according to method name
        /// Use new command-based structure
        /// </summary>
        private static async Task<UnityCliLoopToolResponse> ExecuteMethod(
            string method,
            JToken paramsToken,
            CancellationToken ct)
        {
            return await UnityApiHandler.ExecuteCommandAsync(method, paramsToken, ct);
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
    /// Carries exact CLI update instructions so clients do not infer release tags.
    /// </summary>
    public class CliUpdateRequiredErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.CliUpdateRequired;

        public string currentCliVersion { get; }

        public string requiredCliVersion { get; }

        public string updateCommand { get; }

        public string targetUpdateCommand { get; }

        public bool retryableAfterUpdate { get; }

        public CliUpdateRequiredErrorData(
            string currentCliVersion,
            string requiredCliVersion,
            string updateCommand,
            string targetUpdateCommand) : base("Update the uloop CLI and retry the original command.")
        {
            this.currentCliVersion = string.IsNullOrWhiteSpace(currentCliVersion) ? null : currentCliVersion;
            this.requiredCliVersion = requiredCliVersion;
            this.updateCommand = updateCommand;
            this.targetUpdateCommand = targetUpdateCommand;
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
