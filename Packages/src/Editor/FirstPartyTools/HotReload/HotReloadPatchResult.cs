namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of applying or rejecting a hot-reload transplant patch.
    /// </summary>
    internal sealed class HotReloadPatchResult
    {
        public bool Success { get; }
        public HotReloadPatchFailureReason FailureReason { get; }
        public string ErrorMessage { get; }
        public string Warning { get; }

        private HotReloadPatchResult(
            bool success,
            HotReloadPatchFailureReason failureReason,
            string errorMessage,
            string warning)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Warning = warning;
        }

        public static HotReloadPatchResult SuccessResult(string warning = "")
        {
            return new HotReloadPatchResult(true, HotReloadPatchFailureReason.None, string.Empty, warning ?? string.Empty);
        }

        public static HotReloadPatchResult Failure(HotReloadPatchFailureReason reason, string errorMessage)
        {
            return new HotReloadPatchResult(false, reason, errorMessage, string.Empty);
        }
    }
}