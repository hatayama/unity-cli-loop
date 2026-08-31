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

        [TearDown]
        public void TearDown()
        {
            UloopDynamicCodePartialResults.Clear();
        }

        /// <summary>
        /// What: cancellation returns partial results captured before the command's task outlives the request.
        /// </summary>
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
                await AwaitStartSignalWithinTimeoutAsync(WrappedDynamicCommandState.StartedTask);

                cancellationTokenSource.Cancel();

                ExecutionResult firstResult = await AwaitResultWithinTimeoutAsync(firstExecution);
                Assert.That(firstResult.Success, Is.False);
                Assert.That(firstResult.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
                Assert.That(firstResult.PartialResults["phase"], Is.EqualTo("running"));
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

        /// <summary>
        /// What: a successful command replaces stale entries with the values it opted in to report.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCommandSucceeds_CapturesOnlyCurrentPartialResults()
        {
            UloopDynamicCodePartialResults.OpenExecutionScope();
            UloopDynamicCodePartialResults.Set("stale", "previous request");
            WrappedDynamicCommandState.PrepareReturningCommandWithPartialResult(
                "ready",
                "completed",
                3);
            CommandRunner runner = new();

            ExecutionResult result = await runner.ExecuteAsync(CreateContext(CancellationToken.None));

            Assert.That(result.Success, Is.True);
            Assert.That(result.PartialResults, Has.Count.EqualTo(1));
            Assert.That(result.PartialResults["completed"], Is.EqualTo("3"));
        }

        /// <summary>
        /// What: an invocation exception retains values captured before the exception was thrown.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCommandThrows_CapturesPartialResults()
        {
            WrappedDynamicCommandState.PrepareThrowingCommand("beforeThrow", "saved");
            CommandRunner runner = new();

            ExecutionResult result = await runner.ExecuteAsync(CreateContext(CancellationToken.None));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("planned failure"));
            Assert.That(result.PartialResults["beforeThrow"], Is.EqualTo("saved"));
        }

        /// <summary>
        /// What: a late Set from a cancelled request cannot appear in the next request's partial results.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCancelledRequestCompletesLate_DropsItsPartialResultsFromNextRequest()
        {
            WrappedDynamicCommandState.PrepareCancelledRequestThenNextRequestSequence();
            CommandRunner runner = new();
            using CancellationTokenSource cancellationTokenSource = new();

            try
            {
                Task<ExecutionResult> cancelledRequest = runner.ExecuteAsync(
                    CreateContext(cancellationTokenSource.Token));
                await AwaitCancelledRequestStartWithinTimeoutAsync();

                cancellationTokenSource.Cancel();

                ExecutionResult cancelledResult = await AwaitResultWithinTimeoutAsync(cancelledRequest);
                Assert.That(cancelledResult.PartialResults["phase"], Is.EqualTo("running"));

                Task<ExecutionResult> nextRequest = runner.ExecuteAsync(CreateContext(CancellationToken.None));
                await AwaitNextRequestStartWithinTimeoutAsync();

                WrappedDynamicCommandState.ReleaseCancelledRequest();
                await AwaitLatePartialResultWithinTimeoutAsync();
                WrappedDynamicCommandState.CompleteNextRequest();

                ExecutionResult nextResult = await AwaitResultWithinTimeoutAsync(nextRequest);
                Assert.That(nextResult.Success, Is.True);
                Assert.That(nextResult.PartialResults["currentRequest"], Is.EqualTo("ready"));
                Assert.That(nextResult.PartialResults.ContainsKey("lateFromCancelledRequest"), Is.False);
            }
            finally
            {
                WrappedDynamicCommandState.ReleaseCancelledRequest();
                WrappedDynamicCommandState.CompleteNextRequest();
            }
        }

        [Test]
        public async Task ObserveAbandonedTaskFaultAsync_WhenTaskFaultsLater_ShouldObserveFault()
        {
            // Verifies abandoned user task faults are consumed after cancellation releases the runner.
            TaskCompletionSource<object> completionSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task observationTask = AwaitableHelper.ObserveAbandonedTaskFaultAsync(completionSource.Task);

            completionSource.SetException(new InvalidOperationException("late failure"));

            await AwaitFaultObservationWithinTimeoutAsync(observationTask);
            Assert.That(observationTask.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public async Task AwaitIfNeeded_WhenTokenAlreadyCanceledButTaskCompleted_ShouldReturnTaskResult()
        {
            // Verifies completed user tasks win over cancellation that arrives after the task already finished.
            using CancellationTokenSource cancellationTokenSource = new();
            cancellationTokenSource.Cancel();

            object result = await AwaitableHelper.AwaitIfNeeded(
                Task.FromResult<object>("finished"),
                cancellationTokenSource.Token);

            Assert.That(result, Is.EqualTo("finished"));
        }

        [Test]
        public void AwaitIfNeeded_WhenTokenAlreadyCanceledButTaskFaulted_ShouldSurfaceTaskFault()
        {
            // Verifies completed user task faults are not hidden by cancellation that arrives after completion.
            using CancellationTokenSource cancellationTokenSource = new();
            cancellationTokenSource.Cancel();
            Task<object> faultedTask = Task.FromException<object>(
                new InvalidOperationException("finished failure"));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await AwaitableHelper.AwaitIfNeeded(
                    faultedTask,
                    cancellationTokenSource.Token));

            Assert.That(exception.Message, Is.EqualTo("finished failure"));
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

        private static async Task AwaitStartSignalWithinTimeoutAsync(Task startedTask)
        {
            Task timeoutTask = Task.Delay(CancellationPropagationTimeout);
            Task completedTask = await Task.WhenAny(startedTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Assert.Fail("CommandRunner did not invoke the dynamic command before the timeout.");
            }

            await startedTask;
        }

        private static async Task AwaitCancelledRequestStartWithinTimeoutAsync()
        {
            await AwaitSignalWithinTimeoutAsync(
                WrappedDynamicCommandState.CancelledRequestStartedTask,
                "CommandRunner did not invoke the cancelled dynamic command before the timeout.");
        }

        private static async Task AwaitNextRequestStartWithinTimeoutAsync()
        {
            await AwaitSignalWithinTimeoutAsync(
                WrappedDynamicCommandState.NextRequestStartedTask,
                "CommandRunner did not invoke the next dynamic command before the timeout.");
        }

        private static async Task AwaitLatePartialResultWithinTimeoutAsync()
        {
            await AwaitSignalWithinTimeoutAsync(
                WrappedDynamicCommandState.LatePartialResultSetTask,
                "The cancelled dynamic command did not attempt its late partial result before the timeout.");
        }

        private static async Task AwaitSignalWithinTimeoutAsync(Task signalTask, string timeoutMessage)
        {
            Task timeoutTask = Task.Delay(CancellationPropagationTimeout);
            Task completedTask = await Task.WhenAny(signalTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Assert.Fail(timeoutMessage);
            }

            await signalTask;
        }

        private static async Task AwaitFaultObservationWithinTimeoutAsync(Task observationTask)
        {
            Task timeoutTask = Task.Delay(CancellationPropagationTimeout);
            Task completedTask = await Task.WhenAny(observationTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Assert.Fail("Abandoned task fault observation did not complete before the timeout.");
            }

            await observationTask;
        }
    }

    /// <summary>
    /// Holds mutable test state for the wrapped dynamic command entry point.
    /// </summary>
    internal static class WrappedDynamicCommandState
    {
        private static string _returnValue = "";
        private static bool _shouldComplete;
        private static bool _shouldThrow;
        private static bool _runCancelledRequestThenNextRequestSequence;
        private static int _sequenceInvocationCount;
        private static string _partialResultName = string.Empty;
        private static object _partialResultValue;
        private static TaskCompletionSource<object> _blockingCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<bool> _startedCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<object> _cancelledRequestCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<object> _nextRequestCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<bool> _cancelledRequestStartedCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<bool> _nextRequestStartedCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<bool> _latePartialResultSetCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task StartedTask => _startedCompletionSource.Task;

        public static Task CancelledRequestStartedTask => _cancelledRequestStartedCompletionSource.Task;

        public static Task NextRequestStartedTask => _nextRequestStartedCompletionSource.Task;

        public static Task LatePartialResultSetTask => _latePartialResultSetCompletionSource.Task;

        public static void PrepareBlockingCommand()
        {
            _shouldComplete = false;
            _shouldThrow = false;
            _runCancelledRequestThenNextRequestSequence = false;
            _returnValue = "";
            _partialResultName = "phase";
            _partialResultValue = "running";
            _blockingCompletionSource = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _startedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void PrepareReturningCommand(string returnValue)
        {
            _shouldComplete = true;
            _shouldThrow = false;
            _runCancelledRequestThenNextRequestSequence = false;
            _returnValue = returnValue;
            _partialResultName = string.Empty;
            _partialResultValue = null;
            _startedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void PrepareReturningCommandWithPartialResult(
            string returnValue,
            string partialResultName,
            object partialResultValue)
        {
            _shouldComplete = true;
            _shouldThrow = false;
            _runCancelledRequestThenNextRequestSequence = false;
            _returnValue = returnValue;
            _partialResultName = partialResultName;
            _partialResultValue = partialResultValue;
            _startedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void PrepareThrowingCommand(string partialResultName, object partialResultValue)
        {
            _shouldComplete = false;
            _shouldThrow = true;
            _runCancelledRequestThenNextRequestSequence = false;
            _returnValue = string.Empty;
            _partialResultName = partialResultName;
            _partialResultValue = partialResultValue;
            _startedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void PrepareCancelledRequestThenNextRequestSequence()
        {
            _shouldComplete = false;
            _shouldThrow = false;
            _runCancelledRequestThenNextRequestSequence = true;
            _sequenceInvocationCount = 0;
            _returnValue = string.Empty;
            _partialResultName = string.Empty;
            _partialResultValue = null;
            _cancelledRequestCompletionSource = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _nextRequestCompletionSource = new TaskCompletionSource<object>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _cancelledRequestStartedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _nextRequestStartedCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _latePartialResultSetCompletionSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void CompleteBlockingCommand()
        {
            _blockingCompletionSource.TrySetResult(null);
        }

        public static void ReleaseCancelledRequest()
        {
            _cancelledRequestCompletionSource.TrySetResult(null);
        }

        public static void CompleteNextRequest()
        {
            _nextRequestCompletionSource.TrySetResult(null);
        }

        public static Task<object> ExecuteAsync(CancellationToken ct)
        {
            if (_runCancelledRequestThenNextRequestSequence)
            {
                return ExecuteCancelledRequestThenNextRequestSequenceAsync();
            }

            // This intentionally ignores ct because the regression occurs when user code does not observe it.
            _startedCompletionSource.TrySetResult(true);
            if (!string.IsNullOrEmpty(_partialResultName))
            {
                UloopDynamicCodePartialResults.Set(_partialResultName, _partialResultValue);
            }

            if (_shouldThrow)
            {
                throw new InvalidOperationException("planned failure");
            }

            if (_shouldComplete)
            {
                return Task.FromResult<object>(_returnValue);
            }

            return _blockingCompletionSource.Task;
        }

        private static async Task<object> ExecuteCancelledRequestThenNextRequestSequenceAsync()
        {
            int invocation = Interlocked.Increment(ref _sequenceInvocationCount);
            if (invocation == 1)
            {
                UloopDynamicCodePartialResults.Set("phase", "running");
                _cancelledRequestStartedCompletionSource.TrySetResult(true);
                await _cancelledRequestCompletionSource.Task;
                UloopDynamicCodePartialResults.Set("lateFromCancelledRequest", "late");
                _latePartialResultSetCompletionSource.TrySetResult(true);
                return "cancelled request completed";
            }

            System.Diagnostics.Debug.Assert(invocation == 2, "The test sequence supports exactly two requests.");
            UloopDynamicCodePartialResults.Set("currentRequest", "ready");
            _nextRequestStartedCompletionSource.TrySetResult(true);
            return await _nextRequestCompletionSource.Task;
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
