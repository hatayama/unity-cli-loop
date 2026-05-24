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
    }

    internal interface IUnityTestFrameworkExecutionService
    {
        Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter, CancellationToken ct);
        Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct);
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
    }
}
