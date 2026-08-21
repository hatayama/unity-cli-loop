namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared run-tests response limits and user-facing message formats.
    /// </summary>
    internal static class RunTestsConstants
    {
        public const int FailedTestDetailsLimit = 10;

        // Format: listed count, total failed count. Emitted when FailedCount exceeds the
        // listed cap so the truncation is never silent.
        public const string FailedTestDetailsTruncatedMessageFormat =
            "first {0} of {1} failures listed; see XmlPath for full results.";

        // Format: active hot-reload change count at test-run start. Policy-form because
        // response construction was measured to run before any deferred domain reload.
        public const string HotReloadDiscardWarningFormat =
            "{0} active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards active patches - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in.";
    }
}
