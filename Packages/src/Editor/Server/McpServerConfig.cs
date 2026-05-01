namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Settings related to the Unity CLI bridge server.
    /// </summary>
    public static class McpServerConfig
    {
        /// <summary>
        /// Default port number.
        /// </summary>
        public const int DEFAULT_PORT = 8700;

        /// <summary>
        /// Buffer size for TCP/IP communication.
        /// </summary>
        public const int BUFFER_SIZE = 4096;

        /// <summary>
        /// Wait timeout in seconds when stopping the server.
        /// </summary>
        public const int SHUTDOWN_TIMEOUT_SECONDS = 5;

        /// <summary>
        /// Version string for JSON-RPC 2.0.
        /// </summary>
        public const string JSONRPC_VERSION = "2.0";

        /// <summary>
        /// Internal error code.
        /// </summary>
        public const int INTERNAL_ERROR_CODE = -32603;

        /// <summary>
        /// Minimum port number.
        /// </summary>
        public const int MIN_PORT_NUMBER = 1024;

        /// <summary>
        /// Maximum port number.
        /// </summary>
        public const int MAX_PORT_NUMBER = 65535;

        /// <summary>
        /// Unknown client endpoint.
        /// </summary>
        public const string UNKNOWN_CLIENT_ENDPOINT = "Unknown";

        /// <summary>
        /// Default log type.
        /// </summary>
        public const string DEFAULT_LOG_TYPE = "All";

        /// <summary>
        /// Default maximum number of logs.
        /// </summary>
        public const int DEFAULT_MAX_LOG_COUNT = 100;

        /// <summary>
        /// Default search text.
        /// </summary>
        public const string DEFAULT_SEARCH_TEXT = "";

        /// <summary>
        /// Whether to include stack traces by default.
        /// </summary>
        public const bool DEFAULT_INCLUDE_STACK_TRACE = true;

        /// <summary>
        /// Default line number.
        /// </summary>
        public const int DEFAULT_LINE_NUMBER = 0;

        /// <summary>
        /// Date and time format string.
        /// </summary>
        public const string TIMESTAMP_FORMAT = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// ISO format date and time string.
        /// </summary>
        public const string ISO_DATETIME_FORMAT = "yyyy-MM-ddTHH:mm:ss.fffZ";

        /// <summary>
        /// Default maximum depth for JSON serialization.
        /// Set to int.MaxValue to effectively disable depth limiting while preventing infinite loops.
        /// </summary>
        public const int DEFAULT_JSON_MAX_DEPTH = int.MaxValue;
    }
}
