using System;
using System.Globalization;
using System.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Tells the caller how to locate a failure that has no source location: a failure raised only by a
    /// logged error or exception loses its location when log stack traces are off.
    /// </summary>
    internal static class RunTestsLogStackTraceNoticeBuilder
    {
        private const string PlayModeArgument = "--test-mode PlayMode ";

        internal static string AppendIfLocationMayBeMissing(
            string message,
            RunTestsSchema parameters,
            int failedCount,
            SerializableTestResult.FailedTestDetail[] failedTests,
            SerializableTestResult.FailedTestDetail[] failedSuites)
        {
            // Why count the total: the list stops at the cap, so a dropped failure may have no location even
            // when every listed one does.
            bool listDropsAFailure = failedCount > RunTestsConstants.FailedTestDetailsLimit;
            if (parameters.LogStackTraces
                || !(listDropsAFailure || HasMissingLocation(failedTests) || HasMissingLocation(failedSuites)))
            {
                return message;
            }

            string testModeArgument = parameters.TestMode == UnityCliLoopTestMode.PlayMode
                ? PlayModeArgument
                : string.Empty;
            // Why check the ending: the status message has no terminator, while the truncation note has one.
            string separator = message.EndsWith(".", StringComparison.Ordinal) ? " " : ". ";
            return message
                + separator
                + string.Format(
                    CultureInfo.InvariantCulture,
                    RunTestsConstants.MissingFailureLocationNoticeFormat,
                    testModeArgument);
        }

        private static bool HasMissingLocation(SerializableTestResult.FailedTestDetail[] failures)
        {
            return failures != null && failures.Any(failure => string.IsNullOrEmpty(failure.File));
        }
    }
}
