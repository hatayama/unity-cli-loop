using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Test filter creation service
    /// Single function: Create filters for test execution
    /// Related classes: RunTestsTool, RunTestsUseCase, TestExecutionFilter
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public class TestFilterCreationService
    {
        /// <summary>
        /// Create test execution filter
        /// </summary>
        /// <param name="filterType">Filter type</param>
        /// <param name="filterValue">Filter value</param>
        /// <returns>Test execution filter</returns>
        public TestExecutionFilter CreateFilter(TestFilterType filterType, string filterValue)
        {
            return filterType switch
            {
                TestFilterType.all => TestExecutionFilter.All(),
                TestFilterType.exact => TestExecutionFilter.ByTestName(filterValue),
                TestFilterType.regex => TestExecutionFilter.ByClassName(filterValue),
                TestFilterType.assembly => TestExecutionFilter.ByAssemblyName(filterValue),
                _ => throw new ArgumentException($"Unsupported filter type: {filterType}")
            };
        }
    }
}