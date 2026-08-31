namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports whether a watch expression was accepted by the in-memory registry.
    /// </summary>
    public sealed class WatchRegistrationResult
    {
        private WatchRegistrationResult(bool success, string errorMessage)
        {
            Success = success;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public string ErrorMessage { get; }

        public static WatchRegistrationResult SuccessResult()
        {
            return new WatchRegistrationResult(true, string.Empty);
        }

        public static WatchRegistrationResult FailureResult(string errorMessage)
        {
            return new WatchRegistrationResult(false, errorMessage);
        }
    }
}
