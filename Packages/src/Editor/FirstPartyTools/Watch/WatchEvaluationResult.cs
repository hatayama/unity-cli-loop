namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries either a watch expression value or the user-code error that produced it.
    /// </summary>
    public sealed class WatchEvaluationResult
    {
        private WatchEvaluationResult(bool success, object value, string errorTypeName, string errorMessage)
        {
            Success = success;
            Value = value;
            ErrorTypeName = errorTypeName;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public object Value { get; }
        public string ErrorTypeName { get; }
        public string ErrorMessage { get; }

        public static WatchEvaluationResult SuccessResult(object value)
        {
            return new WatchEvaluationResult(true, value, string.Empty, string.Empty);
        }

        public static WatchEvaluationResult FailureResult(string errorTypeName, string errorMessage)
        {
            return new WatchEvaluationResult(false, null, errorTypeName, errorMessage);
        }
    }
}
