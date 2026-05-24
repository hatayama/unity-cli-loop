using System;
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
        [SerializeField] public string message;
        [SerializeField] public string completedAt;
        [SerializeField] public int testCount;
        [SerializeField] public int passedCount;
        [SerializeField] public int failedCount;
        [SerializeField] public int skippedCount;
        [SerializeField] public string xmlPath;

        public static SerializableTestResult CreateTestFrameworkUnavailable()
        {
            return new SerializableTestResult
            {
                success = false,
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
