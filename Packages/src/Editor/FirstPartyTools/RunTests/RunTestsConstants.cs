namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared run-tests response limits and user-facing message formats.
    /// </summary>
    internal static class RunTestsConstants
    {
        public const int FailedTestDetailsLimit = 10;

        public const int UnfilteredTestNamesLimit = 20;

        public const int PredefinedAssemblyTestSampleLimit = 5;

        // Why these four names: Unity compiles scripts with no .asmdef into these
        // predefined assemblies. Tests that land there are the subject of the
        // zero-discovery notice.
        internal const string PredefinedAssemblyCSharpName = "Assembly-CSharp";
        internal const string PredefinedAssemblyCSharpEditorName = "Assembly-CSharp-Editor";
        internal const string PredefinedAssemblyCSharpFirstpassName = "Assembly-CSharp-firstpass";
        internal const string PredefinedAssemblyCSharpEditorFirstpassName = "Assembly-CSharp-Editor-firstpass";

        // Why a leading space: appended after the existing no-tests Message and
        // after any asmdef hints. AppendIfNeeded inserts a period when the current
        // message has no terminator, so this space stays as-is.
        // Why "could not discover them" is always true here: this notice is emitted
        // only on zero-discovery. A PlayMode run with legacy playModeTestRunnerEnabled=1
        // can discover predefined-assembly tests; that path is not zero-discovery, so
        // the notice would not appear. The claim is scoped to this run, not a
        // universal "never discovers".
        internal const string PredefinedAssemblyTestNoticeFormat =
            " Additionally, {0} NUnit test method(s) are compiled into predefined assemblies rather than any test assembly, so this run could not discover them: {1}. Move these scripts into a folder whose .asmdef has Test Assemblies enabled (EditMode tests target the Editor platform only), reference the assemblies under test, then run 'uloop compile' and rerun the tests.";

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
