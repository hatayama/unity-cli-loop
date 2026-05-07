using System;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Response schema for RunTests command
    /// Provides type-safe response structure
    /// </summary>
    public class RunTestsResponse : BaseToolResponse
    {
        public const string TestFrameworkUnavailableMessage =
            "run-tests requires the Unity Test Framework package (com.unity.test-framework). Install it via Package Manager to use test execution.";

        /// <summary>
        /// Whether test execution was successful
        /// </summary>
        public bool Success { get; set; }

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
        /// Create a new RunTestsResponse
        /// </summary>
        public RunTestsResponse(bool success, string message, string completedAt, int testCount, 
                               int passedCount, int failedCount, int skippedCount, string xmlPath = null)
        {
            Success = success;
            Message = message;
            CompletedAt = completedAt;
            TestCount = testCount;
            PassedCount = passedCount;
            FailedCount = failedCount;
            SkippedCount = skippedCount;
            XmlPath = xmlPath;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public RunTestsResponse()
        {
            Message = string.Empty;
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
                xmlPath: null
            );
        }
    }
}
