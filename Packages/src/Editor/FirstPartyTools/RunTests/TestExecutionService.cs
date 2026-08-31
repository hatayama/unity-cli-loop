using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Test execution service
    /// Single function: Execute tests using Unity Test Runner
    /// Related classes: PlayModeTestExecuter, RunTestsUseCase, RunTestsTool
    /// </summary>
    public class TestExecutionService
    {
        public virtual bool IsTestFrameworkAvailable => UnityTestFrameworkExecutionServiceRegistry.IsAvailable;

        /// <summary>
        /// Execute tests in PlayMode
        /// </summary>
        /// <param name="filter">Test execution filter</param>
        /// <returns>Test execution result</returns>
        public virtual Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UnityTestFrameworkExecutionServiceRegistry.Current.ExecutePlayModeTestAsync(filter, ct);
        }

        /// <summary>
        /// Execute tests in EditMode
        /// </summary>
        /// <param name="filter">Test execution filter</param>
        /// <returns>Test execution result</returns>
        public virtual Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UnityTestFrameworkExecutionServiceRegistry.Current.ExecuteEditModeTestAsync(filter, ct);
        }

        /// <summary>
        /// Lists leaf test full names for a TestMode with no filter applied.
        /// </summary>
        internal virtual Task<RunTestsUnfilteredTestListResult> RetrieveUnfilteredTestNamesAsync(
            UnityCliLoopTestMode testMode,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UnityTestFrameworkExecutionServiceRegistry.Current.RetrieveUnfilteredTestNamesAsync(testMode, ct);
        }

        /// <summary>
        /// Names NUnit test methods compiled into Unity predefined assemblies.
        /// </summary>
        internal virtual RunTestsPredefinedAssemblyTestFindings ScanPredefinedAssemblyTests()
        {
            return UnityTestFrameworkExecutionServiceRegistry.Current.ScanPredefinedAssemblyTests();
        }
    }

    internal interface IUnityTestFrameworkExecutionService
    {
        Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter, CancellationToken ct);
        Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct);
        Task<RunTestsUnfilteredTestListResult> RetrieveUnfilteredTestNamesAsync(
            UnityCliLoopTestMode testMode,
            CancellationToken ct);
        RunTestsPredefinedAssemblyTestFindings ScanPredefinedAssemblyTests();
    }

    internal static class UnityTestFrameworkExecutionServiceRegistry
    {
        private static readonly IUnityTestFrameworkExecutionService UnavailableService =
            new TestFrameworkUnavailableExecutionService();

        private static IUnityTestFrameworkExecutionService _current = UnavailableService;

        public static IUnityTestFrameworkExecutionService Current => _current;
        public static bool IsAvailable => !ReferenceEquals(_current, UnavailableService);

        public static void Register(IUnityTestFrameworkExecutionService executionService)
        {
            Debug.Assert(executionService != null, "executionService must not be null");
            _current = executionService;
        }
    }

    internal sealed class TestFrameworkUnavailableExecutionService : IUnityTestFrameworkExecutionService
    {
        public Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(SerializableTestResult.CreateTestFrameworkUnavailable());
        }

        public Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(SerializableTestResult.CreateTestFrameworkUnavailable());
        }

        public Task<RunTestsUnfilteredTestListResult> RetrieveUnfilteredTestNamesAsync(
            UnityCliLoopTestMode testMode,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(RunTestsUnfilteredTestListResult.NotRetrieved());
        }

        public RunTestsPredefinedAssemblyTestFindings ScanPredefinedAssemblyTests()
        {
            return RunTestsPredefinedAssemblyTestFindings.None();
        }
    }
}
