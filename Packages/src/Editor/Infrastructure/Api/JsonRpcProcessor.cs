using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using ApplicationRegistrar = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Compatibility entrypoint for JSON-RPC callers that have not received JsonRpcRequestProcessor through DI yet.
    /// </summary>
    public static class JsonRpcProcessor
    {
        /// <summary>
        /// Process JSON-RPC request and generate response.
        /// </summary>
        public static Task<string> ProcessRequest(string jsonRequest, CancellationToken ct)
        {
            JsonRpcRequestProcessor processor = CreateProcessor();
            return processor.ProcessRequest(jsonRequest, ct);
        }

        internal static Task<string> ProcessRequestWithEarlyResponseAsync(
            string jsonRequest,
            CancellationToken ct,
            JsonRpcRequestProcessor.JsonRpcEarlyResponseWriter earlyResponseWriter)
        {
            JsonRpcRequestProcessor processor = CreateProcessor();
            return processor.ProcessRequestWithEarlyResponseAsync(jsonRequest, ct, earlyResponseWriter);
        }

        internal static string CreateDispatchAcceptedResponse(object id, int heartbeatIntervalSeconds)
        {
            return JsonRpcRequestProcessor.CreateDispatchAcceptedResponse(id, heartbeatIntervalSeconds);
        }

        internal static string CreateHeartbeatResponse(object id, double mainThreadStallSeconds)
        {
            return JsonRpcRequestProcessor.CreateHeartbeatResponse(id, mainThreadStallSeconds);
        }

        private static JsonRpcRequestProcessor CreateProcessor()
        {
            UnityCliLoopToolRegistrarService toolRegistrarService = ApplicationRegistrar.Service;
            UnityCliLoopExecutionRouter executionRouter = new(toolRegistrarService);
            return new JsonRpcRequestProcessor(executionRouter);
        }
    }
}
