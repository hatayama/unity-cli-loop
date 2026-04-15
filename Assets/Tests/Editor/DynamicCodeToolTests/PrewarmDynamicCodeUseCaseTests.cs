using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class PrewarmDynamicCodeUseCaseTests
    {
        [SetUp]
        public void SetUp()
        {
            DynamicCodeStartupTelemetry.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            DynamicCodeStartupTelemetry.Reset();
        }

        [Test]
        public async Task RequestAsync_WhenSupportedAndWarmupSucceeds_ShouldRetryOnNextRequest()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(
                true,
                new ExecutionResult { Success = true },
                new ExecutionResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime);
            DynamicCodeSecurityLevel previous = ULoopSettings.GetDynamicCodeSecurityLevel();
            ULoopSettings.SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel.FullAccess);

            try
            {
                await useCase.RequestAsync();
                await useCase.RequestAsync();

                Assert.That(runtime.Requests, Has.Count.EqualTo(2));
                Assert.That(runtime.Requests[0].Code, Is.EqualTo("return null;"));
                Assert.That(runtime.Requests[0].ClassName, Is.EqualTo("DynamicCodeAutoPrewarmCommand"));
                Assert.That(runtime.Requests[0].SecurityLevel, Is.EqualTo(DynamicCodeSecurityLevel.FullAccess));
                Assert.That(runtime.Requests[0].CompileOnly, Is.False);
                Assert.That(runtime.Requests[0].YieldToForegroundRequests, Is.True);
                Assert.That(DynamicCodeStartupTelemetry.CreateTimingEntries(), Has.Member("[Perf] WarmReady: True"));
            }
            finally
            {
                ULoopSettings.SetDynamicCodeSecurityLevel(previous);
            }
        }

        [Test]
        public async Task RequestAsync_WhenFastPathIsUnavailable_ShouldSkipExecution()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(false, System.Array.Empty<ExecutionResult>());
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime);

            await useCase.RequestAsync();
            await useCase.RequestAsync();

            Assert.That(runtime.Requests, Is.Empty);
            Assert.That(
                DynamicCodeStartupTelemetry.CreateTimingEntries(),
                Has.Member("[Perf] PrewarmDetail: fast_path_unavailable"));
        }

        [Test]
        public async Task RequestAsync_WhenWarmupFails_ShouldRetryOnNextRequest()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(
                true,
                new ExecutionResult
                {
                    Success = false,
                    ErrorMessage = "warmup failed"
                },
                new ExecutionResult
                {
                    Success = true
                });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime);

            await useCase.RequestAsync();
            await useCase.RequestAsync();

            Assert.That(runtime.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task RequestAsync_WhenRuntimeIsBusy_ShouldSkipExecution()
        {
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(
                true,
                false,
                System.Array.Empty<ExecutionResult>());
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime);

            await useCase.RequestAsync();

            Assert.That(runtime.Requests, Is.Empty);
        }

        [Test]
        public async Task RequestAsync_WhenCalledTwiceBeforeCompletion_ShouldReturnSameTask()
        {
            TaskCompletionSource<ExecutionResult> completionSource =
                new TaskCompletionSource<ExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(
                true,
                new[] { completionSource.Task });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(runtime);

            Task firstTask = useCase.RequestAsync();
            Task secondTask = useCase.RequestAsync();

            Assert.That(secondTask, Is.SameAs(firstTask));
            completionSource.SetResult(new ExecutionResult { Success = true });
            await firstTask;
            Assert.That(runtime.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RequestAsync_WhenLifecycleIsCancelled_ShouldStopBeforeExecution()
        {
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();
            FakePrewarmRuntime runtime = new FakePrewarmRuntime(
                true,
                new ExecutionResult { Success = true });
            PrewarmDynamicCodeUseCase useCase = new PrewarmDynamicCodeUseCase(
                runtime,
                cancellationTokenSource.Token);

            await useCase.RequestAsync();

            Assert.That(runtime.Requests, Is.Empty);
        }

        private sealed class FakePrewarmRuntime : IDynamicCodeExecutionRuntime
        {
            private readonly Queue<Task<ExecutionResult>> _resultTasks;
            private readonly bool _supportsAutoPrewarm;
            private readonly bool _idle;

            public FakePrewarmRuntime(
                bool supportsAutoPrewarm,
                params ExecutionResult[] results)
                : this(supportsAutoPrewarm, true, results)
            {
            }

            public FakePrewarmRuntime(
                bool supportsAutoPrewarm,
                bool idle,
                params ExecutionResult[] results)
                : this(
                    supportsAutoPrewarm,
                    idle,
                    WrapResults(results))
            {
            }

            public FakePrewarmRuntime(
                bool supportsAutoPrewarm,
                params Task<ExecutionResult>[] resultTasks)
                : this(supportsAutoPrewarm, true, resultTasks)
            {
            }

            public FakePrewarmRuntime(
                bool supportsAutoPrewarm,
                bool idle,
                params Task<ExecutionResult>[] resultTasks)
            {
                _supportsAutoPrewarm = supportsAutoPrewarm;
                _idle = idle;
                _resultTasks = new Queue<Task<ExecutionResult>>(resultTasks ?? new Task<ExecutionResult>[0]);
            }

            public List<DynamicCodeExecutionRequest> Requests { get; } = new List<DynamicCodeExecutionRequest>();

            public bool SupportsAutoPrewarm()
            {
                return _supportsAutoPrewarm;
            }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                Requests.Add(new DynamicCodeExecutionRequest
                {
                    Code = request.Code,
                    ClassName = request.ClassName,
                    Parameters = request.Parameters,
                    CompileOnly = request.CompileOnly,
                    SecurityLevel = request.SecurityLevel,
                    YieldToForegroundRequests = request.YieldToForegroundRequests
                });

                return _resultTasks.Dequeue();
            }

            public async Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                if (!_idle)
                {
                    return (false, null);
                }

                Requests.Add(new DynamicCodeExecutionRequest
                {
                    Code = request.Code,
                    ClassName = request.ClassName,
                    Parameters = request.Parameters,
                    CompileOnly = request.CompileOnly,
                    SecurityLevel = request.SecurityLevel,
                    YieldToForegroundRequests = request.YieldToForegroundRequests
                });

                ExecutionResult result = await _resultTasks.Dequeue();
                return (true, result);
            }

            private static Task<ExecutionResult>[] WrapResults(ExecutionResult[] results)
            {
                if (results == null)
                {
                    return new Task<ExecutionResult>[0];
                }

                Task<ExecutionResult>[] wrappedResults = new Task<ExecutionResult>[results.Length];
                for (int index = 0; index < results.Length; index++)
                {
                    wrappedResults[index] = Task.FromResult(results[index]);
                }

                return wrappedResults;
            }
        }
    }
}
