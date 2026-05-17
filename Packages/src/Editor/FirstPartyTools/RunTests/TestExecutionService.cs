using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Test execution service
    /// Single function: Execute tests using Unity Test Runner
    /// Related classes: PlayModeTestExecuter, RunTestsUseCase, RunTestsTool
    /// </summary>
    public class TestExecutionService
    {
        /// <summary>
        /// Execute tests in PlayMode
        /// </summary>
        /// <param name="filter">Test execution filter</param>
        /// <returns>Test execution result</returns>
        public virtual async Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter)
        {
#if !ULOOP_HAS_TEST_FRAMEWORK
            return await Task.FromResult(SerializableTestResult.CreateTestFrameworkUnavailable());
#else
            return await PlayModeTestExecuter.ExecutePlayModeTest(filter);
#endif
        }

        /// <summary>
        /// Execute tests in EditMode
        /// </summary>
        /// <param name="filter">Test execution filter</param>
        /// <returns>Test execution result</returns>
        public virtual async Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter)
        {
#if !ULOOP_HAS_TEST_FRAMEWORK
            return await Task.FromResult(SerializableTestResult.CreateTestFrameworkUnavailable());
#else
            return await PlayModeTestExecuter.ExecuteEditModeTest(filter);
#endif
        }
    }
}
