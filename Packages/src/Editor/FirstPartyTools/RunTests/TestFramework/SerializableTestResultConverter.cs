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
            HasInconclusive,
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
                    inconclusiveCount = 0,
                    xmlPath = null
                };
            }

            int totalTests = CountTotalTests(result);
            int passedTests = CountPassedTests(result);
            int failedTests = CountFailedTests(result);
            int skippedTests = CountSkippedTests(result);
            int inconclusiveTests = CountInconclusiveTests(result);
            SerializableTestResult.FailedTestDetail[] failedSuites = CollectFailedSuiteDetails(result);
            bool noTestsFound = totalTests == 0;
            bool hasFailures = failedTests > 0 || failedSuites != null;
            RunTestsResultClassification classification = Classify(
                result,
                totalTests,
                passedTests,
                failedTests,
                skippedTests,
                inconclusiveTests,
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
                inconclusiveCount = inconclusiveTests,
                xmlPath = null,
                failedTests = CollectFailedTestDetails(result),
                skippedTests = CollectSkippedTestFullNames(result),
                inconclusiveTests = CollectInconclusiveTestDetails(result),
                failedSuites = failedSuites
            };
        }

        /// <summary>
        /// Whether a finished run leaves anything to read in the NUnit XML: a failed or an
        /// inconclusive leaf, or a suite that failed outside its tests.
        /// </summary>
        internal static bool ShouldSaveResultXml(SerializableTestResult result)
        {
            return result.failedCount > 0
                || result.inconclusiveCount > 0
                || (result.failedSuites != null && result.failedSuites.Length > 0);
        }

        private static RunTestsResultClassification Classify(
            ITestResultAdaptor result,
            int totalTests,
            int passedTests,
            int failedTests,
            int skippedTests,
            int inconclusiveTests,
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

            // Why before the root status: NUnit can roll an inconclusive leaf up into a Passed suite,
            // while Unity's batchmode run exits with a failure for it, so the leaf decides. A Failed
            // root still outranks it: a OneTimeTearDown exception fails the fixture but no leaf.
            if (inconclusiveTests > 0 && result.TestStatus != TestStatus.Failed)
            {
                return RunTestsResultClassification.HasInconclusive;
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

            if (classification == RunTestsResultClassification.HasInconclusive)
            {
                return RunTestsExecutionStatus.Inconclusive;
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

        private static int CountInconclusiveTests(ITestResultAdaptor result)
        {
            int count = 0;
            CountTestsByStatus(result, ref count, TestStatus.Inconclusive);
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

        private static SerializableTestResult.FailedTestDetail[] CollectFailedSuiteDetails(ITestResultAdaptor result)
        {
            List<SerializableTestResult.FailedTestDetail> details =
                new List<SerializableTestResult.FailedTestDetail>();
            AppendFailedSuiteDetails(result, details);
            if (details.Count == 0)
            {
                return null;
            }

            return details.ToArray();
        }

        private static void AppendFailedSuiteDetails(
            ITestResultAdaptor result,
            List<SerializableTestResult.FailedTestDetail> details)
        {
            if (details.Count >= RunTestsConstants.FailedTestDetailsLimit)
            {
                return;
            }

            if (!result.Test.IsSuite || result.TestStatus != TestStatus.Failed)
            {
                return;
            }

            if (FailedOutsideItsTests(result))
            {
                details.Add(CreateFailedTestDetail(result));
            }

            if (result.Children == null)
            {
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                AppendFailedSuiteDetails(child, details);
            }
        }

        // Why two rules: NUnit records a OneTimeSetUp or OneTimeTearDown error on the suite's own
        // result state at the SetUp or TearDown site and rolls it into every ancestor at the Child
        // site, so that site marks where the failure started even when some tests failed too. A
        // Failed suite with neither site and nothing Failed beneath it, such as a cancelled one,
        // would otherwise leave a Failed run with nothing that explains it.
        private static bool FailedOutsideItsTests(ITestResultAdaptor suite)
        {
            if (HasSetUpOrTearDownSite(suite.ResultState))
            {
                return true;
            }

            return !HasFailedChildSuite(suite) && CountFailedTests(suite) == 0;
        }

        // Why the text: ITestResultAdaptor exposes the site only through NUnit's ResultState
        // string, which renders as Status[:Label][(Site)].
        private static bool HasSetUpOrTearDownSite(string resultState)
        {
            return resultState.EndsWith("(SetUp)", StringComparison.Ordinal)
                || resultState.EndsWith("(TearDown)", StringComparison.Ordinal);
        }

        private static bool HasFailedChildSuite(ITestResultAdaptor suite)
        {
            if (suite.Children == null)
            {
                return false;
            }

            foreach (ITestResultAdaptor child in suite.Children)
            {
                if (child.Test.IsSuite && child.TestStatus == TestStatus.Failed)
                {
                    return true;
                }
            }

            return false;
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

        private static SerializableTestResult.InconclusiveTestDetail[] CollectInconclusiveTestDetails(
            ITestResultAdaptor result)
        {
            List<SerializableTestResult.InconclusiveTestDetail> details =
                new List<SerializableTestResult.InconclusiveTestDetail>();
            AppendInconclusiveTestDetails(result, details);
            if (details.Count == 0)
            {
                return null;
            }

            return details.ToArray();
        }

        private static void AppendInconclusiveTestDetails(
            ITestResultAdaptor result,
            List<SerializableTestResult.InconclusiveTestDetail> details)
        {
            if (details.Count >= RunTestsConstants.FailedTestDetailsLimit)
            {
                return;
            }

            if (!result.Test.IsSuite)
            {
                if (result.TestStatus == TestStatus.Inconclusive)
                {
                    details.Add(
                        new SerializableTestResult.InconclusiveTestDetail
                        {
                            FullName = result.Test.FullName,
                            Message = result.Message ?? string.Empty
                        });
                }

                return;
            }

            if (result.Children == null)
            {
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                AppendInconclusiveTestDetails(child, details);
                if (details.Count >= RunTestsConstants.FailedTestDetailsLimit)
                {
                    return;
                }
            }
        }
    }
}
#endif
