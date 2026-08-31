#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Converts Unity Test Runner result adapters into the run-tests response DTO.
    /// </summary>
    internal static class SerializableTestResultConverter
    {
        private enum RunTestsResultClassification
        {
            NoTestsFound,
            HasFailures,
            FullyPassed,
            RootStatus
        }

        public static SerializableTestResult FromTestResult(ITestResultAdaptor result)
        {
            if (result == null)
            {
                System.Diagnostics.Debug.Assert(result != null, "ITestResultAdaptor must not be null");
                return new SerializableTestResult
                {
                    success = false,
                    status = RunTestsExecutionStatus.ExecutionFailed,
                    hasFailures = false,
                    noTestsFound = false,
                    noTestsFoundExplanation = string.Empty,
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
            bool noTestsFound = totalTests == 0;
            bool hasFailures = failedTests > 0;
            RunTestsResultClassification classification = Classify(
                result,
                totalTests,
                passedTests,
                failedTests,
                skippedTests,
                noTestsFound,
                hasFailures);
            bool success = classification == RunTestsResultClassification.FullyPassed;
            string status = CreateStatus(result, classification);
            string message = CreateMessage(status, classification);
            string noTestsFoundExplanation = noTestsFound
                ? RunTestsResponse.NoTestsFoundExplanationText
                : string.Empty;

            return new SerializableTestResult
            {
                success = success,
                status = status,
                hasFailures = hasFailures,
                noTestsFound = noTestsFound,
                noTestsFoundExplanation = noTestsFoundExplanation,
                message = message,
                completedAt = DateTime.UtcNow.ToString("o"),
                testCount = totalTests,
                passedCount = passedTests,
                failedCount = failedTests,
                skippedCount = skippedTests,
                xmlPath = null,
                failedTests = CollectFailedTestDetails(result),
                skippedTests = CollectSkippedTestFullNames(result)
            };
        }

        private static RunTestsResultClassification Classify(
            ITestResultAdaptor result,
            int totalTests,
            int passedTests,
            int failedTests,
            int skippedTests,
            bool noTestsFound,
            bool hasFailures)
        {
            if (noTestsFound)
            {
                return RunTestsResultClassification.NoTestsFound;
            }

            if (hasFailures)
            {
                return RunTestsResultClassification.HasFailures;
            }

            if (totalTests > 0
                && failedTests == 0
                && (result.TestStatus == TestStatus.Passed
                    || (passedTests > 0 && passedTests + skippedTests == totalTests)))
            {
                return RunTestsResultClassification.FullyPassed;
            }

            return RunTestsResultClassification.RootStatus;
        }

        private static string CreateStatus(
            ITestResultAdaptor result,
            RunTestsResultClassification classification)
        {
            if (classification == RunTestsResultClassification.NoTestsFound)
            {
                return RunTestsExecutionStatus.NoTestsFound;
            }

            if (classification == RunTestsResultClassification.HasFailures)
            {
                return RunTestsExecutionStatus.Failed;
            }

            if (classification == RunTestsResultClassification.FullyPassed)
            {
                return RunTestsExecutionStatus.Passed;
            }

            return result.TestStatus.ToString();
        }

        private static string CreateMessage(
            string status,
            RunTestsResultClassification classification)
        {
            if (classification == RunTestsResultClassification.NoTestsFound)
            {
                return RunTestsResponse.NoTestsFoundMessage;
            }

            return $"Test execution completed with status: {status}";
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

        private static SerializableTestResult.FailedTestDetail[] CollectFailedTestDetails(ITestResultAdaptor result)
        {
            List<SerializableTestResult.FailedTestDetail> details =
                new List<SerializableTestResult.FailedTestDetail>();
            AppendFailedTestDetails(result, details);
            if (details.Count == 0)
            {
                return null;
            }

            return details.ToArray();
        }

        private static void AppendFailedTestDetails(
            ITestResultAdaptor result,
            List<SerializableTestResult.FailedTestDetail> details)
        {
            if (details.Count >= RunTestsConstants.FailedTestDetailsLimit)
            {
                return;
            }

            if (!result.Test.IsSuite)
            {
                if (result.TestStatus == TestStatus.Failed)
                {
                    details.Add(CreateFailedTestDetail(result));
                }

                return;
            }

            if (result.Children == null)
            {
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                AppendFailedTestDetails(child, details);
                if (details.Count >= RunTestsConstants.FailedTestDetailsLimit)
                {
                    return;
                }
            }
        }

        private static SerializableTestResult.FailedTestDetail CreateFailedTestDetail(ITestResultAdaptor result)
        {
            (string file, int? line) = FailedTestStackLocationParser.TryParse(result.StackTrace);
            return new SerializableTestResult.FailedTestDetail
            {
                FullName = result.Test.FullName,
                Message = result.Message ?? string.Empty,
                File = file,
                Line = line
            };
        }

        private static string[] CollectSkippedTestFullNames(ITestResultAdaptor result)
        {
            List<string> fullNames = new List<string>();
            AppendSkippedTestFullNames(result, fullNames);
            if (fullNames.Count == 0)
            {
                return null;
            }

            return fullNames.ToArray();
        }

        private static void AppendSkippedTestFullNames(
            ITestResultAdaptor result,
            List<string> fullNames)
        {
            if (fullNames.Count >= RunTestsConstants.FailedTestDetailsLimit)
            {
                return;
            }

            if (!result.Test.IsSuite)
            {
                if (result.TestStatus == TestStatus.Skipped)
                {
                    fullNames.Add(result.Test.FullName);
                }

                return;
            }

            if (result.Children == null)
            {
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                AppendSkippedTestFullNames(child, fullNames);
                if (fullNames.Count >= RunTestsConstants.FailedTestDetailsLimit)
                {
                    return;
                }
            }
        }
    }
}
#endif
