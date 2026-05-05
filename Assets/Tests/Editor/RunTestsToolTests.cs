using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop
{
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
        /// Parameter parsing test for filtered test execution.
        /// </summary>
        [Test]
        public void ParseParameters_ShouldParseCorrectly()
        {
            // This test is now obsolete as the new implementation uses type-safe Schema classes
            // instead of JSON parameter parsing. The parsing is handled by the tool dispatch layer.
            
            // Arrange - Test the Schema object directly
            RunTestsSchema schema = new RunTestsSchema
            {
                FilterType = TestFilterType.regex,
                FilterValue = "TestClass"
            };

            // Assert - Schema properties should match what we set
            Assert.That(schema.FilterType, Is.EqualTo(TestFilterType.regex));
            Assert.That(schema.FilterValue, Is.EqualTo("TestClass"));
        }

        /// <summary>
        /// Default value test with default schema.
        /// </summary>
        [Test]
        public void ParseParameters_WithNullParams_ShouldReturnDefaults()
        {
            // This test is now obsolete as the new implementation uses type-safe Schema classes
            // Test the default values of the Schema object
            
            // Act - Create Schema with default values
            RunTestsSchema schema = new RunTestsSchema();

            // Assert - Schema should have default values
            Assert.That(schema.FilterType, Is.EqualTo(TestFilterType.all));
            Assert.That(schema.FilterValue ?? string.Empty, Is.EqualTo(string.Empty));
            Assert.That(schema.SaveBeforeRun, Is.False);
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
    }
} 
