namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
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
}
