namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Type of test execution filter.
    /// </summary>
    public enum TestExecutionFilterType
    {
        /// <summary>
        /// Run all tests without any filter.
        /// </summary>
        All,
        
        /// <summary>
        /// Filter by exact test method name (complete match).
        /// </summary>
        Exact,
        
        /// <summary>
        /// Filter by regex pattern (class name or namespace).
        /// </summary>
        Regex,
        
        /// <summary>
        /// Filter by assembly name.
        /// </summary>
        AssemblyName
    }
    
    /// <summary>
    /// Class that holds test execution filter information.
    /// </summary>
    public class TestExecutionFilter
    {
        /// <summary>
        /// The type of filter to apply (TestName, ClassName, Namespace, or AssemblyName).
        /// This determines how the FilterValue will be interpreted.
        /// </summary>
        public TestExecutionFilterType FilterType { get; }
        
        /// <summary>
        /// The specific value to filter tests by, based on the FilterType.
        /// Examples:
        /// • When FilterType is Exact: "io.github.hatayama.UnityCliLoop.ConsoleLogRetrieverTests.GetAllLogs_WithMaskAllOff_StillReturnsAllLogs"
        /// • When FilterType is Regex: "io.github.hatayama.UnityCliLoop.ConsoleLogRetrieverTests" or "io.github.hatayama.UnityCliLoop"
        /// • When FilterType is AssemblyName: "uLoopMCP.Tests.Editor"
        /// </summary>
        public string FilterValue { get; }
        
        /// <summary>
        /// Creates a test execution filter.
        /// </summary>
        public TestExecutionFilter(TestExecutionFilterType filterType, string filterValue)
        {
            FilterType = filterType;
            FilterValue = filterValue;
        }
        
        /// <summary>
        /// Creates a filter by exact test method name.
        /// </summary>
        public static TestExecutionFilter ByTestName(string testName)
        {
            return new TestExecutionFilter(TestExecutionFilterType.Exact, testName);
        }
        
        /// <summary>
        /// Creates a filter by regex pattern (class name or namespace).
        /// </summary>
        public static TestExecutionFilter ByClassName(string className)
        {
            return new TestExecutionFilter(TestExecutionFilterType.Regex, className);
        }
        
        /// <summary>
        /// Creates a filter by assembly name.
        /// </summary>
        public static TestExecutionFilter ByAssemblyName(string assemblyName)
        {
            return new TestExecutionFilter(TestExecutionFilterType.AssemblyName, assemblyName);
        }
        
        /// <summary>
        /// Creates a filter to run all tests without any filter.
        /// </summary>
        public static TestExecutionFilter All()
        {
            return new TestExecutionFilter(TestExecutionFilterType.All, string.Empty);
        }
    }
} 