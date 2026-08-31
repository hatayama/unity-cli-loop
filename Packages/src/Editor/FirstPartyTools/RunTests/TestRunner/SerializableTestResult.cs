using System;
using Newtonsoft.Json;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Serializable test result for domain reload survival
    /// </summary>
    [Serializable]
    public class SerializableTestResult
    {
        [SerializeField] public bool success;
        [SerializeField] public string status;
        [SerializeField] public bool hasFailures;
        [SerializeField] public bool noTestsFound;
        [SerializeField] public string noTestsFoundExplanation;
        [SerializeField] public string message;
        [SerializeField] public string completedAt;
        [SerializeField] public int testCount;
        [SerializeField] public int passedCount;
        [SerializeField] public int failedCount;
        [SerializeField] public int skippedCount;
        [SerializeField] public string xmlPath;

        /// <summary>
        /// Failed leaf tests, capped for the JSON response. Null when none failed.
        /// </summary>
        public FailedTestDetail[] failedTests;

        /// <summary>
        /// Skipped leaf test full names, capped for the JSON response. Null when none skipped.
        /// </summary>
        public string[] skippedTests;

        /// <summary>
        /// One failed test leaf included in a run-tests response.
        /// </summary>
        [Serializable]
        public class FailedTestDetail
        {
            public string FullName { get; set; }

            public string Message { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string File { get; set; }

            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? Line { get; set; }
        }

        public static SerializableTestResult CreateTestFrameworkUnavailable()
        {
            return new SerializableTestResult
            {
                success = false,
                status = RunTestsExecutionStatus.ExecutionFailed,
                hasFailures = false,
                noTestsFound = false,
                noTestsFoundExplanation = string.Empty,
                message = RunTestsResponse.TestFrameworkUnavailableMessage,
                completedAt = DateTime.UtcNow.ToString("o"),
                testCount = 0,
                passedCount = 0,
                failedCount = 0,
                skippedCount = 0,
                xmlPath = null
            };
        }
    }
}
