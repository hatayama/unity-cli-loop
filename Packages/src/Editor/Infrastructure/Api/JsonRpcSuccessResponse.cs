namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
}
