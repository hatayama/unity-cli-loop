namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
}
