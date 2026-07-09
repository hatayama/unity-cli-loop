namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
}
