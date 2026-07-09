using System;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Owns the wire shape of every JSON-RPC response frame: success, error,
    /// dispatch-accepted, heartbeat, and CLI protocol mismatch.
    /// </summary>
    internal static class JsonRpcResponseFactory
    {
        // Shared by every response path; JsonConvert only reads the settings, so a single
        // instance avoids allocating identical settings per response.
        private static readonly JsonSerializerSettings ResponseSerializerSettings =
            JsonRpcResponseSerializer.Settings;

        /// <summary>
        /// Create JSON-RPC success response
        /// </summary>
        /// <param name="id">Request ID - must be same type as received (string/number/null per JSON-RPC spec)</param>
        /// <param name="result">Command execution result</param>
        internal static string CreateSuccessResponse(object id, UnityCliLoopToolResponse result)
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
        internal static string CreateErrorResponse(object id, Exception ex)
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

        internal static string CreateCliProtocolMismatchResponse(
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

        internal static void AppendTimingIfRequested(UnityCliLoopToolResponse result, string timing)
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
    }
}
