namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Constants for JSON-RPC error types
    /// </summary>
    public static class JsonRpcErrorTypes
    {
        public const string SecurityBlocked = "security_blocked";
        public const string InternalError = "internal_error";
        public const string CliUpdateRequired = "cli_update_required";
        public const string ServerBusy = "server_busy";
    }
}
