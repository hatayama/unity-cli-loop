using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Run Tests Tool behavior.
    /// </summary>
    public class RunTestsToolTests
    {
        private RunTestsTool runTestsTool;
        private TestFilterCreationService filterService;

        [SetUp]
        public void Setup()
        {
            runTestsTool = new RunTestsTool();
            filterService = new TestFilterCreationService();
        }

        /// <summary>
        /// Test for tool name.
        /// - Asserts that the tool name is "run-tests".
        /// </summary>
        [Test]
        public void ToolName_ShouldReturnRunTests()
        {
            // Assert
            Assert.That(runTestsTool.ToolName, Is.EqualTo("run-tests"));
        }

        /// <summary>
        /// Default value test with default schema.
        /// </summary>
        [Test]
        public void ParseParameters_WithNullParams_ShouldUseSaveUnsavedChangesModeByDefault()
        {
            // Verifies that run-tests saves unsaved editor changes unless callers opt into a custom schema.
            
            RunTestsSchema schema = new();

            Assert.That(schema.TestMode, Is.EqualTo(UnityCliLoopTestMode.EditMode));
            Assert.That(schema.FilterType, Is.EqualTo(TestFilterType.all));
            Assert.That(schema.FilterValue ?? string.Empty, Is.EqualTo(string.Empty));
            Assert.That(schema.UnsavedChanges, Is.EqualTo(RunTestsUnsavedChangesMode.save));
        }

        /// <summary>
        /// Test for filter creation via service.
        /// </summary>
        [Test]
        public void TryCreateFilter_WithRegexType_ShouldReturnRegexFilter()
        {
            // Verifies regex filter type is mapped to a class-name filter with the caller's value.
            (TestExecutionFilter result, string errorMessage) = filterService.TryCreateFilter(TestFilterType.regex, "TestClass");

            Assert.That(errorMessage, Is.Null);
            Assert.That(result.FilterType, Is.EqualTo(TestExecutionFilterType.Regex));
            Assert.That(result.FilterValue, Is.EqualTo("TestClass"));
        }

        /// <summary>
        /// Test for creating exact filter.
        /// </summary>
        [Test]
        public void TryCreateFilter_WithExactType_ShouldReturnExactFilter()
        {
            // Verifies exact filter type is mapped to a test-name filter with the caller's value.
            (TestExecutionFilter result, string errorMessage) = filterService.TryCreateFilter(TestFilterType.exact, "io.github.Test");

            Assert.That(errorMessage, Is.Null);
            Assert.That(result.FilterType, Is.EqualTo(TestExecutionFilterType.Exact));
            Assert.That(result.FilterValue, Is.EqualTo("io.github.Test"));
        }

        /// <summary>
        /// Test for unsupported filter types.
        /// </summary>
        [Test]
        public void TryCreateFilter_WithUnsupportedType_ShouldReturnErrorMessage()
        {
            // Verifies out-of-range enum values surface as an error message rather than an exception.
            (TestExecutionFilter result, string errorMessage) = filterService.TryCreateFilter((TestFilterType)999, "value");

            Assert.That(result, Is.Null);
            Assert.That(errorMessage, Does.Contain("Unsupported filter type"));
        }

        [Test]
        public void CreateTestFrameworkUnavailable_ShouldReturnUnsupportedResponse()
        {
            // Verifies that run-tests reports the optional dependency requirement in-band.
            RunTestsResponse response = RunTestsResponse.CreateTestFrameworkUnavailable();

            Assert.That(response.Success, Is.False);
            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.False);
            Assert.That(response.NoTestsFoundExplanation, Is.Empty);
            Assert.That(response.Message, Does.Contain(UnityCliLoopConstants.PACKAGE_NAME_TEST_FRAMEWORK));
            Assert.That(response.CompletedAt, Is.Not.Empty);
            Assert.That(response.TestCount, Is.EqualTo(0));
            Assert.That(response.PassedCount, Is.EqualTo(0));
            Assert.That(response.FailedCount, Is.EqualTo(0));
            Assert.That(response.SkippedCount, Is.EqualTo(0));
            Assert.That(response.XmlPath, Is.Null);
        }

        [Test]
        public void Constructor_ShouldStoreEveryArgumentVerbatim()
        {
            // Verifies the constructor propagates every field as supplied instead of deriving any of them.
            RunTestsResponse response = new(
                success: true,
                message: "arbitrary message",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 5,
                passedCount: 3,
                failedCount: 2,
                skippedCount: 1,
                xmlPath: "/tmp/results.xml",
                status: "CustomStatus",
                hasFailures: false,
                noTestsFound: true,
                noTestsFoundExplanation: "custom explanation");

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo("arbitrary message"));
            Assert.That(response.CompletedAt, Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
            Assert.That(response.TestCount, Is.EqualTo(5));
            Assert.That(response.PassedCount, Is.EqualTo(3));
            Assert.That(response.FailedCount, Is.EqualTo(2));
            Assert.That(response.SkippedCount, Is.EqualTo(1));
            Assert.That(response.XmlPath, Is.EqualTo("/tmp/results.xml"));
            Assert.That(response.Status, Is.EqualTo("CustomStatus"));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.True);
            Assert.That(response.NoTestsFoundExplanation, Is.EqualTo("custom explanation"));
        }
    }
}
