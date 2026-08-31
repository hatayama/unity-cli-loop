using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Represents a parsed JSON-RPC request
    /// </summary>
    internal class JsonRpcRequest
    {
        public string Method { get; set; }
        /// <summary>
        /// JSON-RPC 2.0 spec allows params to be object, array, or null.
        /// We use JToken to accept any format, then convert to strongly-typed
        /// schema classes (e.g. PingSchema) in AbstractUnityCommand.ConvertToSchema.
        /// This provides flexibility at the protocol layer and type safety at the command layer.
        /// </summary>
        public JToken Params { get; set; }

        public string ClientProjectRunnerVersion { get; set; }

        /// <summary>
        /// IPC protocol generation the client speaks. Null when the client predates the
        /// protocol handshake or sent a malformed value; both must fail the compatibility gate.
        /// </summary>
        public int? ClientProtocolVersion { get; set; }

        public bool AcceptsDispatchAck { get; set; }

        public bool AcceptsHeartbeat { get; set; }

        /// <summary>
        /// JSON-RPC 2.0 spec requires id type to match the request.
        /// Must be string, number, or null - same as received.
        /// </summary>
        public object Id { get; set; }

        /// <summary>
        /// JSON-RPC 2.0 notification flag. True when id is null/missing.
        /// Notifications are fire-and-forget messages that don't expect a response.
        /// Regular requests (with id) expect a response, notifications do not.
        /// </summary>
        public bool IsNotification => Id == null;
    }
}
