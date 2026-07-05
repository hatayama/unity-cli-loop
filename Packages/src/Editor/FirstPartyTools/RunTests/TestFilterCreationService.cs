namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Test filter creation service
    /// Single function: Create filters for test execution
    /// Related classes: RunTestsTool, RunTestsUseCase, TestExecutionFilter
    /// </summary>
    public class TestFilterCreationService
    {
        /// <summary>
        /// Create test execution filter. Returns (filter, errorMessage); errorMessage is non-null
        /// only when the caller supplied an out-of-range enum value cast from an integer.
        /// </summary>
        /// <param name="filterType">Filter type</param>
        /// <param name="filterValue">Filter value</param>
        /// <returns>Test execution filter or an error message describing an invalid filter type</returns>
        public (TestExecutionFilter filter, string errorMessage) TryCreateFilter(TestFilterType filterType, string filterValue)
        {
            switch (filterType)
            {
                case TestFilterType.all:
                    return (TestExecutionFilter.All(), null);
                case TestFilterType.exact:
                    return (TestExecutionFilter.ByTestName(filterValue), null);
                case TestFilterType.regex:
                    return (TestExecutionFilter.ByClassName(filterValue), null);
                case TestFilterType.assembly:
                    return (TestExecutionFilter.ByAssemblyName(filterValue), null);
                default:
                    return (null, $"Unsupported filter type: {filterType}");
            }
        }
    }
}
