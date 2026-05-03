using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Current client context for JSON-RPC processing
    /// </summary>
    public class ClientExecutionContext
    {
        public string Endpoint { get; }
        
        public ClientExecutionContext(string endpoint)
        {
            Endpoint = endpoint;
        }
    }

    /// <summary>
    /// Class specialized in handling JSON-RPC 2.0 processing
    /// 
    /// Design document reference: Packages/src/Editor/ARCHITECTURE.md
    /// 
    /// Related classes:
    /// - UnityApiHandler: Executes Unity commands based on JSON-RPC requests
    /// - McpBridgeServer: Project IPC bridge that receives JSON-RPC messages from CLI clients
    /// - MainThreadSwitcher: Ensures Unity API calls run on the main thread
    /// - JsonRpcRequest: Request model for JSON-RPC 2.0 protocol
    /// - ClientExecutionContext: Thread-local context for tracking current client
    /// 
    /// Processing flow:
    /// 1. Receives JSON message from McpBridgeServer
    /// 2. Parses and validates JSON-RPC 2.0 format
    /// 3. Sets client context for the current thread
    /// 4. Delegates to UnityApiHandler for command execution
    /// 5. Formats response according to JSON-RPC 2.0 specification
    /// 6. Returns JSON response to be sent back to client
    /// </summary>
    public static class JsonRpcProcessor
    {
        /// <summary>
        /// Current client context for async operations
        /// </summary>
        private static readonly AsyncLocal<ClientExecutionContext> _currentClientContext = new();
        
        /// <summary>
        /// Get current client context (ProcessID and Endpoint)
        /// </summary>
        public static ClientExecutionContext CurrentClientContext => _currentClientContext.Value;
        
        /// <summary>
        /// Process JSON-RPC request and generate response with client context
        /// </summary>
        public static async Task<string> ProcessRequest(string jsonRequest, string clientEndpoint)
        {
            var context = new ClientExecutionContext(clientEndpoint);
            _currentClientContext.Value = context;
            
            try
            {
                return await ProcessRequest(jsonRequest);
            }
            finally
            {
                _currentClientContext.Value = null;
            }
        }
        
        /// <summary>
        /// Process JSON-RPC request and generate response
        /// </summary>
        public static async Task<string> ProcessRequest(string jsonRequest)
        {
            try
            {
                JsonRpcRequest request = ParseRequest(jsonRequest);
                
                if (request.IsNotification)
                {
                    ProcessNotification(request);
                    return null;
                }
                
                return await ProcessRpcRequest(request, jsonRequest);
            }
            catch (JsonReaderException ex)
            {
                return CreateErrorResponse(null, ex);
            }
            catch (Exception ex)
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
                Id = request["id"]?.ToObject<object>(),
                UloopMetadata = ParseUloopMetadata(request["x-uloop"])
            };
        }

        private static JsonRpcRequestUloopMetadata ParseUloopMetadata(JToken metadataToken)
        {
            if (metadataToken == null || metadataToken.Type == JTokenType.Null)
            {
                return null;
            }

            return new JsonRpcRequestUloopMetadata
            {
                ExpectedProjectRoot = metadataToken["expectedProjectRoot"]?.ToString()
            };
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
        private static async Task<string> ProcessRpcRequest(JsonRpcRequest request, string originalJson)
        {
            Stopwatch requestStopwatch = Stopwatch.StartNew();
            try
            {
                ValidateClientIdentityIfNeeded(request);

                Stopwatch mainThreadWaitStopwatch = Stopwatch.StartNew();
                await MainThreadSwitcher.SwitchToMainThread();
                mainThreadWaitStopwatch.Stop();

                Stopwatch toolStopwatch = Stopwatch.StartNew();
                BaseToolResponse result = await ExecuteMethod(request.Method, request.Params);
                toolStopwatch.Stop();

                AppendExecuteDynamicCodeTimingsIfSupported(
                    result,
                    mainThreadWaitStopwatch.Elapsed.TotalMilliseconds,
                    toolStopwatch.Elapsed.TotalMilliseconds,
                    requestStopwatch.Elapsed.TotalMilliseconds);
                string response = CreateSuccessResponse(request.Id, result);
                return response;
            }
            catch (JsonSerializationException ex)
            {
                UnityEngine.Debug.LogError($"[JsonRpcProcessor] JSON serialization error: {ex.Message}\nStack trace: {ex.StackTrace}");
                return CreateErrorResponse(request.Id, ex);
            }
            catch (ParameterValidationException ex)
            {
                LogParameterValidationException(ex);
                return CreateErrorResponse(request.Id, ex);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[JsonRpcProcessor] Error: {ex.Message}\nStack trace: {ex.StackTrace}");
                return CreateErrorResponse(request.Id, ex);
            }
        }

        private static void ValidateClientIdentityIfNeeded(JsonRpcRequest request)
        {
            JsonRpcRequestIdentityValidator.Validate(
                request?.UloopMetadata,
                GetCurrentProjectRoot());
        }

        private static string GetCurrentProjectRoot()
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            return BridgeTransportEndpoint.CanonicalizeProjectRoot(projectRoot);
        }

        private static void LogParameterValidationException(ParameterValidationException exception)
        {
            UnityEngine.Debug.LogError(
                $"[JsonRpcProcessor] Parameter validation error: {exception.Message}\nStack trace: {exception.StackTrace}");
        }

        private static void AppendExecuteDynamicCodeTimingsIfSupported(
            BaseToolResponse result,
            double mainThreadWaitMilliseconds,
            double toolTotalMilliseconds,
            double requestTotalMilliseconds)
        {
            if (result is not ExecuteDynamicCodeResponse executeDynamicCodeResponse)
            {
                return;
            }

            ExecuteDynamicCodeResponseTimingAugmenter.AppendTimingEntries(
                executeDynamicCodeResponse,
                mainThreadWaitMilliseconds,
                toolTotalMilliseconds,
                requestTotalMilliseconds);
        }

        /// <summary>
        /// Create JSON-RPC success response
        /// </summary>
        /// <param name="id">Request ID - must be same type as received (string/number/null per JSON-RPC spec)</param>
        /// <param name="result">Command execution result</param>
        private static string CreateSuccessResponse(object id, BaseToolResponse result)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = McpServerConfig.DEFAULT_JSON_MAX_DEPTH
            };
            
            try
            {
                JsonRpcSuccessResponse response = new JsonRpcSuccessResponse(
                    McpServerConfig.JSONRPC_VERSION,
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
                
                JsonRpcSuccessResponse fallbackResponse = new JsonRpcSuccessResponse(
                    McpServerConfig.JSONRPC_VERSION,
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
            UserFriendlyErrorConverter handler = new UserFriendlyErrorConverter();
            UserFriendlyErrorDto exceptionResponse = handler.ProcessException(ex);
            
            // Map UserFriendlyErrorDto to JsonRpcError
            string errorMessage = exceptionResponse.FriendlyMessage;

            JsonRpcErrorData errorData;
            if (ex is McpSecurityException secEx)
            {
                errorData = new SecurityBlockedErrorData(secEx.ToolName, secEx.SecurityReason, exceptionResponse.Explanation ?? ex.Message);
            }
            else
            {
                errorData = new InternalErrorData(exceptionResponse.Explanation ?? ex.Message);
            }

            JsonRpcErrorResponse errorResponse = new JsonRpcErrorResponse(
                McpServerConfig.JSONRPC_VERSION,
                id,
                new JsonRpcError(McpServerConfig.INTERNAL_ERROR_CODE, errorMessage, errorData)
            );
            
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = McpServerConfig.DEFAULT_JSON_MAX_DEPTH
            };
            
            return JsonConvert.SerializeObject(errorResponse, Formatting.None, settings);
        }

        /// <summary>
        /// Execute appropriate handler according to method name
        /// Use new command-based structure
        /// </summary>
        private static async Task<BaseToolResponse> ExecuteMethod(string method, JToken paramsToken)
        {
            return await UnityApiHandler.ExecuteCommandAsync(method, paramsToken);
        }
    }

    /// <summary>
    /// Constants for JSON-RPC error types
    /// </summary>
    public static class JsonRpcErrorTypes
    {
        public const string SecurityBlocked = "security_blocked";
        public const string InternalError = "internal_error";
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
