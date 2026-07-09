namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
