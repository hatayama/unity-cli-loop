namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries one hot-reload argument validation failure before it becomes a tool response.
    /// </summary>
    internal sealed class HotReloadValidationFailure
    {
        public HotReloadValidationFailure(string message, string errorCode, string[] nextActions)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(message), "message must not be empty.");
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(errorCode), "errorCode must not be empty.");
            System.Diagnostics.Debug.Assert(nextActions != null, "nextActions must not be null.");
            Message = message;
            ErrorCode = errorCode;
            NextActions = nextActions;
        }

        public string Message { get; }

        public string ErrorCode { get; }

        public string[] NextActions { get; }
    }
}
