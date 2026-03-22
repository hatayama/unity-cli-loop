namespace io.github.hatayama.uLoopMCP
{
    internal static class RecordInputConstants
    {
        public const string INPUT_RECORDINGS_DIR = "InputRecordings";
        public static readonly string DEFAULT_OUTPUT_DIR = System.IO.Path.Combine(McpConstants.OUTPUT_ROOT_DIR, INPUT_RECORDINGS_DIR);
        public const float MAX_RECORDING_DURATION_SECONDS = 300f;
        public const string RECORDING_FILE_PREFIX = "";
        public const int MIN_DELAY_SECONDS = 0;
        public const int MAX_DELAY_SECONDS = 10;
    }
}
