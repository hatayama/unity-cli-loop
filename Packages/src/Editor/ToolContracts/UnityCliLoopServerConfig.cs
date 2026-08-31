namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Shared settings for project IPC and JSON-RPC bridge responses.
    /// </summary>
    public static class UnityCliLoopServerConfig
    {
        public const int SHUTDOWN_TIMEOUT_SECONDS = 5;

        public const int READINESS_PROBE_TIMEOUT_MS = 30000;

        // Why: a single failed recovery (e.g. readiness timeout during a heavy import right after
        // a domain reload) must not leave the server down until the next reload. The total backoff
        // stays well inside the CLI's 180s readiness polling window so a late success is still picked up.
        public static readonly int[] RECOVERY_RETRY_DELAYS_MS = { 5000, 15000, 30000 };

        // Heartbeats prove to the CLI that the server process is alive during long-running
        // commands; each frame also reports how long the editor main thread has gone without
        // an update tick so the CLI can distinguish "busy" from "frozen".
        public const int HEARTBEAT_INTERVAL_SECONDS = 10;

        public const string JSONRPC_VERSION = "2.0";

        public const int INTERNAL_ERROR_CODE = -32603;

        // JSON payloads are already bounded by the command lifecycle; the serializer depth limit is disabled to avoid false failures on nested Unity data.
        public const int DEFAULT_JSON_MAX_DEPTH = int.MaxValue;
    }
}
