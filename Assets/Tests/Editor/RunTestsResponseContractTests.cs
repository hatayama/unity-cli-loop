using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies run-tests JSON omits null FailedTests and null File/Line keys.
    /// </summary>
    public sealed class RunTestsResponseContractTests
    {
        /// <summary>
        /// What: zero failures omit the FailedTests key, and a populated detail with
        /// null File/Line omits those keys from the serialized object.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenSerialized_OmitsNullFailedTestsAndNullFileLineKeys()
        {
            RunTestsResponse zeroFailures = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);

            JObject zeroFailuresJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    zeroFailures,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(zeroFailuresJson.Property("FailedTests"), Is.Null);

            RunTestsResponse populated = new RunTestsResponse(
                success: false,
                message: "Test execution completed with status: Failed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 0,
                failedCount: 1,
                skippedCount: 0,
                xmlPath: "TestResults/example.xml",
                status: RunTestsExecutionStatus.Failed,
                hasFailures: true,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty)
            {
                FailedTests = new[]
                {
                    new SerializableTestResult.FailedTestDetail
                    {
                        FullName = "Example.Tests.FailingTest",
                        Message = "Expected 2 But was: 1",
                        File = null,
                        Line = null
                    }
                }
            };

            JObject populatedJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    populated,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            JArray failedTests = (JArray)populatedJson["FailedTests"];
            Assert.That(failedTests, Is.Not.Null);
            Assert.That(failedTests.Count, Is.EqualTo(1));
            JObject first = (JObject)failedTests[0];
            Assert.That(first["FullName"]?.Value<string>(), Is.EqualTo("Example.Tests.FailingTest"));
            Assert.That(first["Message"]?.Value<string>(), Is.EqualTo("Expected 2 But was: 1"));
            Assert.That(first.Property("File"), Is.Null);
            Assert.That(first.Property("Line"), Is.Null);
        }
    }
}
