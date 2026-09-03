using System.Diagnostics;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the pure run-tests response fields from a SerializableTestResult.
    /// </summary>
    internal static class RunTestsResponseFactory
    {
        internal static RunTestsResponse FromResult(SerializableTestResult result)
        {
            Debug.Assert(result != null, "result must not be null");

            RunTestsResponse response = new(
                success: result.success,
                message: result.message,
                completedAt: result.completedAt,
                testCount: result.testCount,
                passedCount: result.passedCount,
                failedCount: result.failedCount,
                skippedCount: result.skippedCount,
                xmlPath: result.xmlPath,
                status: result.status,
                hasFailures: result.hasFailures,
                noTestsFound: result.noTestsFound,
                noTestsFoundExplanation: result.noTestsFoundExplanation);
            CopyTestDetails(result, response);
            if (result.failedCount > RunTestsConstants.FailedTestDetailsLimit)
            {
                response.Message = response.Message
                    + " "
                    + string.Format(
                        CultureInfo.InvariantCulture,
                        RunTestsConstants.FailedTestDetailsTruncatedMessageFormat,
                        RunTestsConstants.FailedTestDetailsLimit,
                        result.failedCount);
            }

            return response;
        }

        private static void CopyTestDetails(SerializableTestResult result, RunTestsResponse response)
        {
            if (result.failedTests != null && result.failedTests.Length > 0)
            {
                response.FailedTests = result.failedTests;
            }

            if (result.skippedTests != null && result.skippedTests.Length > 0)
            {
                response.SkippedTests = result.skippedTests;
            }
        }
    }
}
