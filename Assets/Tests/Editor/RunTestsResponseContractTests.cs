using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies run-tests JSON optional test-detail fields and null File/Line keys.
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
                inconclusiveCount: 0,
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
                inconclusiveCount: 0,
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

        /// <summary>
        /// What: SkippedTests is omitted without skipped leaves and serializes each full name when present.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenSerialized_OmitsOrIncludesSkippedTestsByPresence()
        {
            RunTestsResponse zeroSkippedTests = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);

            JObject zeroSkippedTestsJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    zeroSkippedTests,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(zeroSkippedTestsJson.Property("SkippedTests"), Is.Null);

            RunTestsResponse populated = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 2,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 1,
                inconclusiveCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty)
            {
                SkippedTests = new[] { "Example.Tests.SkippedTest" }
            };

            JObject populatedJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    populated,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            JArray skippedTests = (JArray)populatedJson["SkippedTests"];

            Assert.That(skippedTests, Is.Not.Null);
            Assert.That(skippedTests.Count, Is.EqualTo(1));
            Assert.That(skippedTests[0].Value<string>(), Is.EqualTo("Example.Tests.SkippedTest"));
        }

        /// <summary>
        /// What: InconclusiveCount is always written, while InconclusiveTests is omitted without
        /// inconclusive leaves and serializes each name and message when present.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenSerialized_WritesInconclusiveCountAndOmitsOrIncludesInconclusiveTests()
        {
            RunTestsResponse zeroInconclusive = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);

            JObject zeroInconclusiveJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    zeroInconclusive,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(zeroInconclusiveJson.Value<int>("InconclusiveCount"), Is.EqualTo(0));
            Assert.That(zeroInconclusiveJson.Property("InconclusiveTests"), Is.Null);

            RunTestsResponse populated = new RunTestsResponse(
                success: false,
                message: "Test execution completed with status: Inconclusive",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 2,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 1,
                xmlPath: "TestResults/example.xml",
                status: RunTestsExecutionStatus.Inconclusive,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty)
            {
                InconclusiveTests = new[]
                {
                    new SerializableTestResult.InconclusiveTestDetail
                    {
                        FullName = "Example.Tests.InconclusiveTest",
                        Message = "Release is required."
                    }
                }
            };

            JObject populatedJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    populated,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            JArray inconclusiveTests = (JArray)populatedJson["InconclusiveTests"];

            Assert.That(populatedJson.Value<int>("InconclusiveCount"), Is.EqualTo(1));
            Assert.That(populatedJson.Value<string>("Status"), Is.EqualTo("Inconclusive"));
            Assert.That(inconclusiveTests, Is.Not.Null);
            Assert.That(inconclusiveTests.Count, Is.EqualTo(1));
            JObject first = (JObject)inconclusiveTests[0];
            Assert.That(first["FullName"]?.Value<string>(), Is.EqualTo("Example.Tests.InconclusiveTest"));
            Assert.That(first["Message"]?.Value<string>(), Is.EqualTo("Release is required."));
        }

        /// <summary>
        /// What: the response built from a stored result carries its inconclusive count and details,
        /// and leaves InconclusiveTests unset when the result lists none.
        /// </summary>
        [Test]
        public void FromResult_WhenResultHasInconclusiveLeaves_CopiesCountAndDetails()
        {
            SerializableTestResult withInconclusive = new SerializableTestResult
            {
                success = false,
                status = RunTestsExecutionStatus.Inconclusive,
                message = "Test execution completed with status: Inconclusive",
                noTestsFoundExplanation = string.Empty,
                completedAt = "2026-01-01T00:00:00.0000000Z",
                testCount = 2,
                passedCount = 1,
                inconclusiveCount = 1,
                inconclusiveTests = new[]
                {
                    new SerializableTestResult.InconclusiveTestDetail
                    {
                        FullName = "Example.Tests.InconclusiveTest",
                        Message = "Release is required."
                    }
                }
            };
            SerializableTestResult withoutInconclusive = new SerializableTestResult
            {
                success = true,
                status = RunTestsExecutionStatus.Passed,
                message = "Test execution completed with status: Passed",
                noTestsFoundExplanation = string.Empty,
                completedAt = "2026-01-01T00:00:00.0000000Z",
                testCount = 1,
                passedCount = 1,
                inconclusiveCount = 0,
                inconclusiveTests = new SerializableTestResult.InconclusiveTestDetail[0]
            };

            RunTestsResponse copied = RunTestsResponseFactory.FromResult(withInconclusive);
            RunTestsResponse empty = RunTestsResponseFactory.FromResult(withoutInconclusive);

            Assert.That(copied.InconclusiveCount, Is.EqualTo(1));
            Assert.That(copied.InconclusiveTests, Is.Not.Null);
            Assert.That(copied.InconclusiveTests.Length, Is.EqualTo(1));
            Assert.That(copied.InconclusiveTests[0].FullName, Is.EqualTo("Example.Tests.InconclusiveTest"));
            Assert.That(empty.InconclusiveCount, Is.EqualTo(0));
            Assert.That(empty.InconclusiveTests, Is.Null);
        }

        /// <summary>
        /// What: FailedSuites is omitted from JSON when no suite failed and serializes each suite's
        /// name and message when one did.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenSerialized_OmitsOrIncludesFailedSuites()
        {
            RunTestsResponse withoutFailedSuites = CreateFailedSuitesResponse(null);
            RunTestsResponse withFailedSuites = CreateFailedSuitesResponse(new[]
            {
                new SerializableTestResult.FailedTestDetail
                {
                    FullName = "Example.Tests.TearDownFixture",
                    Message = "TearDown : System.InvalidOperationException : teardown failed"
                }
            });

            JObject withoutJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    withoutFailedSuites,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            JObject withJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    withFailedSuites,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            JArray failedSuites = (JArray)withJson["FailedSuites"];

            Assert.That(withoutJson.Property("FailedSuites"), Is.Null);
            Assert.That(failedSuites, Is.Not.Null);
            Assert.That(failedSuites.Count, Is.EqualTo(1));
            JObject first = (JObject)failedSuites[0];
            Assert.That(first["FullName"]?.Value<string>(), Is.EqualTo("Example.Tests.TearDownFixture"));
            Assert.That(
                first["Message"]?.Value<string>(),
                Is.EqualTo("TearDown : System.InvalidOperationException : teardown failed"));
        }

        /// <summary>
        /// What: the response built from a stored result carries its failed suites, and leaves
        /// FailedSuites unset when the result lists none.
        /// </summary>
        [Test]
        public void FromResult_WhenResultHasFailedSuites_CopiesThem()
        {
            SerializableTestResult withFailedSuites = new SerializableTestResult
            {
                success = false,
                status = RunTestsExecutionStatus.Failed,
                hasFailures = true,
                message = "Test execution completed with status: Failed",
                noTestsFoundExplanation = string.Empty,
                completedAt = "2026-01-01T00:00:00.0000000Z",
                testCount = 1,
                passedCount = 1,
                failedSuites = new[]
                {
                    new SerializableTestResult.FailedTestDetail
                    {
                        FullName = "Example.Tests.TearDownFixture",
                        Message = "TearDown : System.InvalidOperationException : teardown failed"
                    }
                }
            };
            SerializableTestResult withoutFailedSuites = new SerializableTestResult
            {
                success = true,
                status = RunTestsExecutionStatus.Passed,
                message = "Test execution completed with status: Passed",
                noTestsFoundExplanation = string.Empty,
                completedAt = "2026-01-01T00:00:00.0000000Z",
                testCount = 1,
                passedCount = 1,
                failedSuites = new SerializableTestResult.FailedTestDetail[0]
            };

            RunTestsResponse copied = RunTestsResponseFactory.FromResult(withFailedSuites);
            RunTestsResponse empty = RunTestsResponseFactory.FromResult(withoutFailedSuites);

            Assert.That(copied.FailedSuites, Is.Not.Null);
            Assert.That(copied.FailedSuites.Length, Is.EqualTo(1));
            Assert.That(copied.FailedSuites[0].FullName, Is.EqualTo("Example.Tests.TearDownFixture"));
            Assert.That(empty.FailedSuites, Is.Null);
        }

        private static RunTestsResponse CreateFailedSuitesResponse(
            SerializableTestResult.FailedTestDetail[] failedSuites)
        {
            return new RunTestsResponse(
                success: failedSuites == null,
                message: "Test execution completed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 0,
                xmlPath: null,
                status: failedSuites == null ? RunTestsExecutionStatus.Passed : RunTestsExecutionStatus.Failed,
                hasFailures: failedSuites != null,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty)
            {
                FailedSuites = failedSuites
            };
        }

        /// <summary>
        /// What: an empty Warning is omitted from production JSON so the key cannot reappear unnoticed.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenWarningIsEmpty_OmitsWarningPropertyFromJson()
        {
            RunTestsResponse response = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);

            JObject parsed = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(parsed.Property("Warning"), Is.Null);
        }

        /// <summary>
        /// What: a set Warning serializes under Warning with the exact policy-form sentence.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenWarningIsSet_SerializesExactPolicyFormSentence()
        {
            RunTestsResponse response = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty)
            {
                Warning =
                    "2 active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards every active hot-reload change, including introduced types - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in."
            };

            JObject parsed = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(
                parsed.Value<string>("Warning"),
                Is.EqualTo(
                    "2 active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards every active hot-reload change, including introduced types - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in."));
        }

        /// <summary>
        /// What: a response that is not a --rerun-failed run omits both rerun fields from JSON.
        /// </summary>
        [Test]
        public void RunTestsResponse_WhenNotARerun_OmitsRerunFields()
        {
            RunTestsResponse response = new RunTestsResponse(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0,
                inconclusiveCount: 0,
                xmlPath: string.Empty,
                status: RunTestsExecutionStatus.Passed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);

            JObject parsed = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(parsed.Property("RerunTargetCount"), Is.Null);
            Assert.That(parsed.Property("RerunSourceCompletedAt"), Is.Null);
        }

        /// <summary>
        /// What: a rerun with no recorded failures serializes as a successful NothingToRerun response
        /// with zero targets and the source record's completion time.
        /// </summary>
        [Test]
        public void CreateNothingToRerun_WhenSerialized_ReportsSuccessWithZeroTargets()
        {
            const string sourceCompletedAt = "2026-01-02T03:04:05.0000000Z";
            RunTestsResponse response = RunTestsResponse.CreateNothingToRerun(
                UnityCliLoopTestMode.EditMode,
                sourceCompletedAt);

            JObject parsed = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(parsed.Value<string>("Status"), Is.EqualTo("NothingToRerun"));
            Assert.That(parsed.Value<bool>("Success"), Is.True);
            Assert.That(parsed.Value<bool>("HasFailures"), Is.False);
            Assert.That(parsed.Value<bool>("NoTestsFound"), Is.False);
            Assert.That(parsed.Value<int>("TestCount"), Is.EqualTo(0));
            Assert.That(parsed.Value<int>("RerunTargetCount"), Is.EqualTo(0));
            Assert.That(parsed.Property("RerunSourceCompletedAt"), Is.Not.Null);
            Assert.That(response.RerunSourceCompletedAt, Is.EqualTo(sourceCompletedAt));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "The EditMode run completed at 2026-01-02T03:04:05.0000000Z had no failed or inconclusive tests; nothing to rerun."));
        }
    }
}
