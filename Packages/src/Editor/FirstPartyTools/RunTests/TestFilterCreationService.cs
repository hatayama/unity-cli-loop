namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Test filter creation service
    /// Single function: Create filters for test execution
    /// Related classes: RunTestsTool, RunTestsUseCase, TestExecutionFilter
    /// </summary>
    public class TestFilterCreationService
    {
        // Why a dedicated message: an empty class name would otherwise become a pattern that
        // matches nothing, and the resulting NoTestsFound would point at the wrong cause.
        internal const string ClassFilterRequiresValueMessage =
            "FilterType 'class' requires FilterValue to name a test class (e.g. PlayerTests or MyGame.Tests.PlayerTests)";

        /// <summary>
        /// Create test execution filter. Returns (filter, errorMessage); errorMessage is non-null
        /// only when the caller supplied an out-of-range enum value cast from an integer or a
        /// class filter without a class name.
        /// </summary>
        /// <param name="filterType">Filter type</param>
        /// <param name="filterValue">Filter value</param>
        /// <returns>Test execution filter, or an error message for an unsupported filter type or a blank class filter value</returns>
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
                case TestFilterType.@class:
                    if (string.IsNullOrWhiteSpace(filterValue))
                    {
                        return (null, ClassFilterRequiresValueMessage);
                    }
                    return (TestExecutionFilter.ByTestClass(filterValue), null);
                default:
                    return (null, $"Unsupported filter type: {filterType}");
            }
        }
    }
}
