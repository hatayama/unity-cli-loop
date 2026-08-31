namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
}
