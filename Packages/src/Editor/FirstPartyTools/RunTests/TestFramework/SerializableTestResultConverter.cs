#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using UnityEditor.TestTools.TestRunner.Api;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Converts Unity Test Runner result adapters into the run-tests response DTO.
    /// </summary>
    internal static class SerializableTestResultConverter
    {
        public static SerializableTestResult FromTestResult(ITestResultAdaptor result)
        {
            if (result == null)
            {
                System.Diagnostics.Debug.Assert(result != null, "ITestResultAdaptor must not be null");
                return new SerializableTestResult
                {
                    success = false,
                    message = "Test execution failed: no test result was produced",
                    completedAt = DateTime.UtcNow.ToString("o"),
                    testCount = 0,
                    passedCount = 0,
                    failedCount = 0,
                    skippedCount = 0,
                    xmlPath = null
                };
            }

            int totalTests = CountTotalTests(result);
            int passedTests = CountPassedTests(result);
            int failedTests = CountFailedTests(result);
            int skippedTests = CountSkippedTests(result);
            bool success = totalTests > 0 && result.TestStatus == TestStatus.Passed;
            string message = CreateMessage(result, totalTests);

            return new SerializableTestResult
            {
                success = success,
                message = message,
                completedAt = DateTime.UtcNow.ToString("o"),
                testCount = totalTests,
                passedCount = passedTests,
                failedCount = failedTests,
                skippedCount = skippedTests,
                xmlPath = null
            };
        }

        private static string CreateMessage(ITestResultAdaptor result, int totalTests)
        {
            if (totalTests == 0)
            {
                return RunTestsResponse.NoTestsFoundMessage;
            }

            return $"Test execution completed with status: {result.TestStatus}";
        }

        private static int CountTotalTests(ITestResultAdaptor result)
        {
            int count = 0;
            CountTestsByStatus(result, ref count, null);
            return count;
        }

        private static int CountPassedTests(ITestResultAdaptor result)
        {
            int count = 0;
            CountTestsByStatus(result, ref count, TestStatus.Passed);
            return count;
        }

        private static int CountFailedTests(ITestResultAdaptor result)
        {
            int count = 0;
            CountTestsByStatus(result, ref count, TestStatus.Failed);
            return count;
        }

        private static int CountSkippedTests(ITestResultAdaptor result)
        {
            int count = 0;
            CountTestsByStatus(result, ref count, TestStatus.Skipped);
            return count;
        }

        private static void CountTestsByStatus(ITestResultAdaptor result, ref int count, TestStatus? targetStatus)
        {
            if (!result.Test.IsSuite)
            {
                if (targetStatus == null || result.TestStatus == targetStatus)
                {
                    count++;
                }
                return;
            }

            if (result.Children == null)
            {
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                CountTestsByStatus(child, ref count, targetStatus);
            }
        }
    }
}
#endif
