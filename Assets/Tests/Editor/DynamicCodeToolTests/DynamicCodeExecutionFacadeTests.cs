using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Execution Facade behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeExecutionFacadeTests
    {
        private static readonly TimeSpan CancellationPropagationTimeout = TimeSpan.FromSeconds(1);

        [Test]
        public async Task ExecuteAsync_WhenCalledTwice_ShouldReuseExecutor()
        {
            // Verifies dynamic code execution reuses the same cached executor.
            FakeDynamicCodeExecutorProvider provider = new();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);
            using DynamicCodeExecutionFacade facade = new DynamicCodeExecutionFacade(pool);

            await facade.ExecuteAsync(
                CreateRequest("return 1;"),
                CancellationToken.None);
            await facade.ExecuteAsync(
                CreateRequest("return 2;"),
                CancellationToken.None);

            Assert.That(provider.CreateCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_WhenExecutorsWereCreated_ShouldDisposeCachedExecutors()
        {
            FakeDynamicCodeExecutorProvider provider = new();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);
            DynamicCodeExecutionFacade facade = new(pool);

            Assert.DoesNotThrowAsync(async () =>
            {
                await facade.ExecuteAsync(
                    CreateRequest("return 1;"),
                    CancellationToken.None);
            });

            facade.Dispose();

            Assert.That(provider.CreatedExecutors[0].DisposeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void ResetServerScopedServicesBeforeDomainReload_ShouldSignalShutdownWithoutWaitingForRuntimeDrain()
        {
            // Tests that domain reload reset does not leave a pending drain task that can block Unity teardown.
            DynamicCodeServicesRegistry registry = new();
            FakeShutdownAwareRuntime runtime = new();
            registry.SetRuntimeFacadeForTests(runtime);

            registry.ResetServerScopedServicesBeforeDomainReload();

            Assert.That(runtime.ShutdownCallCount, Is.EqualTo(1));
            Assert.That(runtime.DisposeCallCount, Is.EqualTo(0));
            Assert.That(registry.GetServerScopedDrainTaskForTests().IsCompleted, Is.True);

            runtime.CompleteShutdown();
        }

        [Test]
        public async Task ExecuteAsync_WhenForegroundExecutionIsCancelled_ShouldAllowNextExecution()
        {
            // Verifies CLI disconnect cancellation releases the scheduler slot instead of leaving execute-dynamic-code busy.
            FakeDynamicCodeExecutorProvider provider = new();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);
            using DynamicCodeExecutionFacade facade = new DynamicCodeExecutionFacade(pool);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            Task<ExecutionResult> firstExecution = facade.ExecuteAsync(
                CreateRequest(FakeDynamicCodeExecutor.BlockingCode),
                cancellationTokenSource.Token);
            FakeDynamicCodeExecutor executor = await provider.CreatedExecutorTask;
            await executor.BlockingExecutionStartedTask;

            cancellationTokenSource.Cancel();

            await AssertCanceledWithinTimeoutAsync(firstExecution);

            ExecutionResult secondExecution = await facade.ExecuteAsync(
                CreateRequest("return 2;"),
                CancellationToken.None);

            Assert.That(secondExecution.Success, Is.True);
            Assert.That(secondExecution.Result, Is.EqualTo("return 2;"));
        }

        private static async Task AssertCanceledWithinTimeoutAsync(Task task)
        {
            Task timeoutTask = Task.Delay(CancellationPropagationTimeout);
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask == timeoutTask)
            {
                Assert.Fail("Foreground execution task did not complete before the timeout.");
            }

            if (task.IsFaulted)
            {
                Assert.Fail($"Expected cancellation, but the task faulted: {task.Exception}");
            }

            Assert.That(task.IsCanceled, Is.True);
        }

        private static DynamicCodeExecutionRequest CreateRequest(string code)
        {
            return new DynamicCodeExecutionRequest
            {
                Code = code,
                ClassName = "FacadeTestCommand"
            };
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class FakeDynamicCodeExecutorProvider : IDynamicCodeExecutorProvider
        {
            public int CreateCallCount { get; private set; }

            public List<FakeDynamicCodeExecutor> CreatedExecutors { get; } = new();

            private readonly TaskCompletionSource<FakeDynamicCodeExecutor> _createdExecutorCompletionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<FakeDynamicCodeExecutor> CreatedExecutorTask => _createdExecutorCompletionSource.Task;

            public IDynamicCodeExecutor Create()
            {
                CreateCallCount++;

                FakeDynamicCodeExecutor executor = new();
                CreatedExecutors.Add(executor);
                _createdExecutorCompletionSource.TrySetResult(executor);
                return executor;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class FakeDynamicCodeExecutor : IDynamicCodeExecutor
        {
            public const string BlockingCode = "__block_until_cancel__";

            private readonly TaskCompletionSource<bool> _blockingExecutionStartedCompletionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int DisposeCallCount { get; private set; }

            public Task BlockingExecutionStartedTask => _blockingExecutionStartedCompletionSource.Task;

            public async Task<ExecutionResult> ExecuteCodeAsync(
                string code,
                string className = DynamicCodeConstants.DEFAULT_CLASS_NAME,
                object[] parameters = null,
                CancellationToken cancellationToken = default,
                bool compileOnly = false)
            {
                if (code == BlockingCode)
                {
                    _blockingExecutionStartedCompletionSource.TrySetResult(true);
                    await WaitForCancellationOrFailAsync(cancellationToken);
                }

                return new ExecutionResult
                {
                    Success = true,
                    Result = code
                };
            }

            public ExecutionStatistics GetStatistics()
            {
                return new ExecutionStatistics();
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }

            /// <summary>
            /// Waits for cancellation with a bounded timeout so regression failures do not freeze Unity.
            /// </summary>
            private static async Task WaitForCancellationOrFailAsync(CancellationToken cancellationToken)
            {
                TaskCompletionSource<bool> cancellationCompletionSource =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration cancellationRegistration =
                    cancellationToken.Register(() => cancellationCompletionSource.TrySetCanceled());
                Task timeoutTask = Task.Delay(CancellationPropagationTimeout);
                Task completedTask = await Task.WhenAny(cancellationCompletionSource.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Assert.Fail("Foreground execution cancellation was not observed before the timeout.");
                }

                await cancellationCompletionSource.Task;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class FakeShutdownAwareRuntime : IShutdownAwareDynamicCodeExecutionRuntime, System.IDisposable
        {
            private readonly TaskCompletionSource<bool> _shutdownCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int ShutdownCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new System.NotSupportedException();
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new System.NotSupportedException();
            }

            public Task ShutdownAsync()
            {
                ShutdownCallCount++;
                return _shutdownCompletionSource.Task;
            }

            public void CompleteShutdown()
            {
                _shutdownCompletionSource.SetResult(true);
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

    }
}
