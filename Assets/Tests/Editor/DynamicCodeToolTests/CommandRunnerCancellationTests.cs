using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using DynamicExecutionContext = io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies CommandRunner cancellation behavior.
    /// </summary>
    [TestFixture]
    public class CommandRunnerCancellationTests
    {
        private static readonly TimeSpan CancellationPropagationTimeout = TimeSpan.FromSeconds(1);

        [Test]
        public async Task ExecuteAsync_WhenAsyncResultIgnoresCancellation_ShouldCancelAndAllowNextExecution()
        {
            // Verifies user tasks are cancellation-bound so the runner does not stay busy forever.
            WrappedDynamicCommandState.PrepareBlockingCommand();
            CommandRunner runner = new();
            using CancellationTokenSource cancellationTokenSource = new();

            try
            {
                Task<ExecutionResult> firstExecution = runner.ExecuteAsync(
                    CreateContext(cancellationTokenSource.Token));
                await WrappedDynamicCommandState.StartedTask;

                cancellationTokenSource.Cancel();

                ExecutionResult firstResult = await AwaitResultWithinTimeoutAsync(firstExecution);
                Assert.That(firstResult.Success, Is.False);
                Assert.That(firstResult.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
                Assert.That(runner.IsRunning, Is.False);

                WrappedDynamicCommandState.PrepareReturningCommand("ready");

                ExecutionResult secondResult = await runner.ExecuteAsync(
                    CreateContext(CancellationToken.None));

                Assert.That(secondResult.Success, Is.True);
                Assert.That(secondResult.Result, Is.EqualTo("ready"));
                Assert.That(runner.IsRunning, Is.False);
            }
            finally
            {
                WrappedDynamicCommandState.CompleteBlockingCommand();
            }
        }

        private static DynamicExecutionContext CreateContext(CancellationToken cancellationToken)
        {
            return new DynamicExecutionContext
            {
                CompiledAssembly = typeof(global::UnityCliLoop.Dynamic.DynamicCommand).Assembly,
                CancellationToken = cancellationToken
            };
        }

        private static async Task<ExecutionResult> AwaitResultWithinTimeoutAsync(
            Task<ExecutionResult> executionTask)
        {
            Task timeoutTask = Task.Delay(CancellationPropagationTimeout);
            Task completedTask = await Task.WhenAny(executionTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Assert.Fail("CommandRunner did not complete after cancellation.");
            }

            ExecutionResult result = await executionTask;
            return result;
        }
    }

    /// <summary>
    /// Holds mutable test state for the wrapped dynamic command entry point.
    /// </summary>
    internal static class WrappedDynamicCommandState
    {
        private static string _returnValue = "";
        private static bool _shouldComplete;
        private static TaskCompletionSource<object> _blockingCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<bool> _startedCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task StartedTask => _startedCompletionSource.Task;

        public static void PrepareBlockingCommand()
        {
            _shouldComplete = false;
            _returnValue = "";
            _blockingCompletionSource = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _startedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void PrepareReturningCommand(string returnValue)
        {
            _shouldComplete = true;
            _returnValue = returnValue;
            _startedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void CompleteBlockingCommand()
        {
            _blockingCompletionSource.TrySetResult(null);
        }

        public static Task<object> ExecuteAsync(CancellationToken ct)
        {
            // This intentionally ignores ct because the regression occurs when user code does not observe it.
            _startedCompletionSource.TrySetResult(true);
            if (_shouldComplete)
            {
                return Task.FromResult<object>(_returnValue);
            }

            return _blockingCompletionSource.Task;
        }
    }
}

namespace UnityCliLoop.Dynamic
{
    /// <summary>
    /// Test dynamic-code wrapper type used by CommandRunner entry point resolution.
    /// </summary>
    public class DynamicCommand
    {
        public Task<object> ExecuteAsync(CancellationToken ct)
        {
            return io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
                .WrappedDynamicCommandState.ExecuteAsync(ct);
        }
    }
}
