namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Shared settings for project IPC and JSON-RPC bridge responses.
    /// </summary>
    public static class UnityCliLoopServerConfig
    {
        public const int SHUTDOWN_TIMEOUT_SECONDS = 5;

        public const string JSONRPC_VERSION = "2.0";

        public const int INTERNAL_ERROR_CODE = -32603;

        // JSON payloads are already bounded by the command lifecycle; the serializer depth limit is disabled to avoid false failures on nested Unity data.
        public const int DEFAULT_JSON_MAX_DEPTH = int.MaxValue;
    }
}
