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

        internal static string AppendIfLocationMissing(
            string message,
            RunTestsSchema parameters,
            SerializableTestResult.FailedTestDetail[] failedTests,
            SerializableTestResult.FailedTestDetail[] failedSuites)
        {
            if (parameters.LogStackTraces || !(HasMissingLocation(failedTests) || HasMissingLocation(failedSuites)))
            {
                return message;
            }

            string testModeArgument = parameters.TestMode == UnityCliLoopTestMode.PlayMode
                ? PlayModeArgument
                : string.Empty;
            return message
                + " "
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
