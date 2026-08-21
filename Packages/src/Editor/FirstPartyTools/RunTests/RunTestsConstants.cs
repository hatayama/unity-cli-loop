namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared run-tests response limits and user-facing message formats.
    /// </summary>
    internal static class RunTestsConstants
    {
        public const int FailedTestDetailsLimit = 10;

        public const int UnfilteredTestNamesLimit = 20;

        // Why a listing timeout instead of --timeout-seconds: RetrieveTestList is a catalog
        // callback, not a test run. Waiting the full run timeout would stall a no-tests
        // response for minutes when the callback never arrives.
        public const int UnfilteredTestListRetrieveTimeoutMilliseconds = 10000;

        // Format: listed count, total failed count. Emitted when FailedCount exceeds the
        // listed cap so the truncation is never silent.
        public const string FailedTestDetailsTruncatedMessageFormat =
            "first {0} of {1} failures listed; see XmlPath for full results.";

        // Format: FilterType, FilterValue, UnfilteredTestCount. Appended to the existing
        // NoTestsFound message so the original sentence stays unchanged.
        // Why a leading period: NoTestsFoundMessage has no terminator, so a leading space
        // would fuse the two sentences into one.
        public const string NoTestsFoundWithFilterMessageFormat =
            ". No tests matched FilterType '{0}' with FilterValue '{1}'. {2} test(s) exist in this TestMode without the filter; compare UnfilteredTestNames against the filter value.";

        // Format: active hot-reload change count at test-run start. Policy-form because
        // response construction was measured to run before any deferred domain reload.
        public const string HotReloadDiscardWarningFormat =
            "{0} active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards active patches - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in.";
    }
}
