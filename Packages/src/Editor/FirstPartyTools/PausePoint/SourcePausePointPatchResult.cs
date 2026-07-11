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
        public string Warning { get; }

        private SourcePausePointPatchResult(
            bool success, SourcePausePointPatchFailureReason failureReason, string errorMessage, string hint, string warning)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Hint = hint;
            Warning = warning;
        }

        public static SourcePausePointPatchResult SuccessResult(string warning = "")
        {
            return new SourcePausePointPatchResult(true, SourcePausePointPatchFailureReason.None, string.Empty, string.Empty, warning);
        }

        public static SourcePausePointPatchResult Failure(SourcePausePointPatchFailureReason reason, string errorMessage, string hint)
        {
            return new SourcePausePointPatchResult(false, reason, errorMessage, hint, string.Empty);
        }
    }
}
