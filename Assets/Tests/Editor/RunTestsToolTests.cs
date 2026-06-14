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
        public void ParseParameters_WithNullParams_ShouldSaveBeforeRunByDefault()
        {
            // Verifies that run-tests saves unsaved editor changes unless callers opt into a custom schema.
            
            RunTestsSchema schema = new();

            Assert.That(schema.TestMode, Is.EqualTo(UnityCliLoopTestMode.EditMode));
            Assert.That(schema.FilterType, Is.EqualTo(TestFilterType.all));
            Assert.That(schema.FilterValue ?? string.Empty, Is.EqualTo(string.Empty));
            Assert.That(schema.SaveBeforeRun, Is.True);
        }

        /// <summary>
        /// Test for filter creation via service.
        /// </summary>
        [Test]
        public void CreateFilter_WithRegexType_ShouldReturnRegexFilter()
        {
            TestExecutionFilter result = filterService.CreateFilter(TestFilterType.regex, "TestClass");

            Assert.That(result.FilterType, Is.EqualTo(TestExecutionFilterType.Regex));
            Assert.That(result.FilterValue, Is.EqualTo("TestClass"));
        }

        /// <summary>
        /// Test for creating exact filter.
        /// </summary>
        [Test]
        public void CreateFilter_WithExactType_ShouldReturnExactFilter()
        {
            TestExecutionFilter result = filterService.CreateFilter(TestFilterType.exact, "io.github.Test");

            Assert.That(result.FilterType, Is.EqualTo(TestExecutionFilterType.Exact));
            Assert.That(result.FilterValue, Is.EqualTo("io.github.Test"));
        }

        /// <summary>
        /// Test for unsupported filter types.
        /// </summary>
        [Test]
        public void CreateFilter_WithUnsupportedType_ShouldThrowException()
        {
            Assert.Throws<System.ArgumentException>(() =>
            {
                filterService.CreateFilter((TestFilterType)999, "value");
            });
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
        public void Constructor_WhenLegacySuccessfulCallerOmitsStatus_ShouldDerivePassedStatus()
        {
            // Verifies source-compatible constructor calls cannot report success as execution failure.
            RunTestsResponse response = new(
                success: true,
                message: "Test execution completed with status: Passed",
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 1,
                passedCount: 1,
                failedCount: 0,
                skippedCount: 0);

            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.Passed));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.False);
            Assert.That(response.NoTestsFoundExplanation, Is.Empty);
        }

        [Test]
        public void Constructor_WhenLegacyNoTestsCallerOmitsStatus_ShouldDeriveNoTestsFoundStatus()
        {
            // Verifies source-compatible zero-discovery responses remain distinct from execution failures.
            RunTestsResponse response = new(
                success: false,
                message: RunTestsResponse.NoTestsFoundMessage,
                completedAt: "2026-01-01T00:00:00.0000000Z",
                testCount: 0,
                passedCount: 0,
                failedCount: 0,
                skippedCount: 0);

            Assert.That(response.Status, Is.EqualTo(RunTestsExecutionStatus.NoTestsFound));
            Assert.That(response.HasFailures, Is.False);
            Assert.That(response.NoTestsFound, Is.True);
            Assert.That(response.NoTestsFoundExplanation, Does.Contain("not a test failure"));
        }
    }
} 
