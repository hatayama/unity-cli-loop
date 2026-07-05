using System;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Response schema for RunTests command
    /// Provides type-safe response structure
    /// </summary>
    public class RunTestsResponse : UnityCliLoopToolResponse
    {
        internal const string NoTestsFoundMessage = "No tests found matching the specified filter criteria";
        internal const string NoTestsFoundExplanationText =
            "No tests were discovered for this run. This is not a test failure; check TestMode, FilterType, FilterValue, or test assembly configuration.";

        public static readonly string TestFrameworkUnavailableMessage =
            $"run-tests requires the Unity Test Framework package ({UnityCliLoopConstants.PACKAGE_NAME_TEST_FRAMEWORK}). Install it via Package Manager to use test execution.";

        /// <summary>
        /// Whether test execution was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Machine-readable execution status
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Whether any discovered test failed
        /// </summary>
        public bool HasFailures { get; set; }

        /// <summary>
        /// Whether Unity Test Runner discovered zero tests
        /// </summary>
        public bool NoTestsFound { get; set; }

        /// <summary>
        /// Explanation for agents when zero tests are discovered
        /// </summary>
        public string NoTestsFoundExplanation { get; set; }

        /// <summary>
        /// Test execution message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Test execution completion timestamp
        /// </summary>
        public string CompletedAt { get; set; }

        /// <summary>
        /// Total number of tests executed
        /// </summary>
        public int TestCount { get; set; }

        /// <summary>
        /// Number of passed tests
        /// </summary>
        public int PassedCount { get; set; }

        /// <summary>
        /// Number of failed tests
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Number of skipped tests
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// Path to XML result file (if saved)
        /// </summary>
        public string XmlPath { get; set; }

        /// <summary>
        /// Create a new RunTestsResponse. Every field is required so classification decisions
        /// stay at the call site (the Unity Test Runner adapter and use-case), not in this DTO.
        /// </summary>
        public RunTestsResponse(
            bool success,
            string message,
            string completedAt,
            int testCount,
            int passedCount,
            int failedCount,
            int skippedCount,
            string xmlPath,
            string status,
            bool hasFailures,
            bool noTestsFound,
            string noTestsFoundExplanation)
        {
            Debug.Assert(!string.IsNullOrEmpty(status), "status must be a RunTestsExecutionStatus value");
            Debug.Assert(noTestsFoundExplanation != null, "noTestsFoundExplanation must not be null; pass string.Empty when not applicable");

            Success = success;
            Message = message;
            CompletedAt = completedAt;
            TestCount = testCount;
            PassedCount = passedCount;
            FailedCount = failedCount;
            SkippedCount = skippedCount;
            XmlPath = xmlPath;
            Status = status;
            HasFailures = hasFailures;
            NoTestsFound = noTestsFound;
            NoTestsFoundExplanation = noTestsFoundExplanation;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public RunTestsResponse()
        {
            Message = string.Empty;
            Status = string.Empty;
            NoTestsFoundExplanation = string.Empty;
            CompletedAt = string.Empty;
            XmlPath = string.Empty;
        }

        public static RunTestsResponse CreateTestFrameworkUnavailable()
        {
            return new RunTestsResponse(
                success: false,
                message: TestFrameworkUnavailableMessage,
                completedAt: DateTime.UtcNow.ToString("o"),
                testCount: 0,
                passedCount: 0,
                failedCount: 0,
                skippedCount: 0,
                xmlPath: null,
                status: RunTestsExecutionStatus.ExecutionFailed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);
        }
    }
}
