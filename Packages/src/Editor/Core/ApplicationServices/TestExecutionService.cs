using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Test execution service
    /// Single function: Execute tests using Unity Test Runner
    /// Related classes: PlayModeTestExecuter, RunTestsUseCase, RunTestsTool
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public class TestExecutionService
    {
#if ULOOPMCP_HAS_TEST_FRAMEWORK
        /// <summary>
        /// Execute tests in PlayMode
        /// </summary>
        /// <param name="filter">Test execution filter</param>
        /// <returns>Test execution result</returns>
        public virtual async Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter)
        {
            return await PlayModeTestExecuter.ExecutePlayModeTest(filter);
        }

        /// <summary>
        /// Execute tests in EditMode
        /// </summary>
        /// <param name="filter">Test execution filter</param>
        /// <returns>Test execution result</returns>
        public virtual async Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter)
        {
            return await PlayModeTestExecuter.ExecuteEditModeTest(filter);
        }
#else
        public virtual Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter)
        {
            return Task.FromResult(SerializableTestResult.CreateTestFrameworkUnavailable());
        }

        public virtual Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter)
        {
            return Task.FromResult(SerializableTestResult.CreateTestFrameworkUnavailable());
        }
#endif
    }
}
