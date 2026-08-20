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
    }
}
