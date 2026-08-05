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

        // True when the patched method is small (or marked [AggressiveInlining]) and the
        // Mono JIT may already have inlined it into existing callers; the orchestrator
        // aggregates these flags into one response warning.
        public bool InlineRiskDetected { get; }

        private HotReloadPatchResult(
            bool success,
            HotReloadPatchFailureReason failureReason,
            string errorMessage,
            bool inlineRiskDetected)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            InlineRiskDetected = inlineRiskDetected;
        }

        public static HotReloadPatchResult SuccessResult(bool inlineRiskDetected = false)
        {
            return new HotReloadPatchResult(true, HotReloadPatchFailureReason.None, string.Empty, inlineRiskDetected);
        }

        public static HotReloadPatchResult Failure(HotReloadPatchFailureReason reason, string errorMessage)
        {
            return new HotReloadPatchResult(false, reason, errorMessage, false);
        }
    }
}
