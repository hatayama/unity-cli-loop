namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of a <see cref="SourcePausePointPatcher"/> patch attempt.
    /// </summary>
    internal sealed class SourcePausePointPatchResult
    {
        public bool Success { get; }
        public SourcePausePointPatchFailureReason FailureReason { get; }
        public string ErrorMessage { get; }
        public string Hint { get; }

        private SourcePausePointPatchResult(
            bool success, SourcePausePointPatchFailureReason failureReason, string errorMessage, string hint)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Hint = hint;
        }

        public static SourcePausePointPatchResult SuccessResult()
        {
            return new SourcePausePointPatchResult(true, SourcePausePointPatchFailureReason.None, string.Empty, string.Empty);
        }

        public static SourcePausePointPatchResult Failure(SourcePausePointPatchFailureReason reason, string errorMessage, string hint)
        {
            return new SourcePausePointPatchResult(false, reason, errorMessage, hint);
        }
    }
}
