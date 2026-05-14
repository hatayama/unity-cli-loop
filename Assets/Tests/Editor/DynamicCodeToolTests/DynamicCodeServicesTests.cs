using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class DynamicCodeServicesTests
    {
        [TearDown]
        public void TearDown()
        {
            DynamicCodeServices.ResetStateForTests();
        }

        [Test]
        public void ResetServerScopedServicesBeforeDomainReload_ShouldCancelLifetimeWithoutAwaitingRuntimeShutdown()
        {
            CancellationTokenSource lifetimeCancellationTokenSource = new CancellationTokenSource();
            FakeExecutorPool executorPool = new FakeExecutorPool();
            FakeShutdownAwareRuntime runtime = new FakeShutdownAwareRuntime();
            DynamicCodeServices.SetServerScopedServicesForTests(
                lifetimeCancellationTokenSource,
                executorPool,
                runtime,
                null);

            DynamicCodeServices.ResetServerScopedServicesBeforeDomainReload();

            Assert.IsTrue(lifetimeCancellationTokenSource.IsCancellationRequested, "Lifetime token should be cancelled");
            Assert.AreEqual(1, runtime.ShutdownCallCount, "Domain reload reset should signal runtime shutdown");
            Assert.AreEqual(0, runtime.DisposeCallCount, "Domain reload reset should leave managed disposal to reload teardown");
            Assert.AreEqual(0, executorPool.DisposeCallCount, "Domain reload reset should not dispose executor pool synchronously");
            Assert.IsTrue(
                DynamicCodeServices.GetServerScopedDrainTaskForTests().IsCompleted,
                "Domain reload reset should not leave a drain task that later runs into reload teardown");

            runtime.CompleteShutdown();
            lifetimeCancellationTokenSource.Dispose();
        }

        private sealed class FakeExecutorPool : IDynamicCodeExecutorPool
        {
            public int DisposeCallCount { get; private set; }

            public IDynamicCodeExecutor GetOrCreate(DynamicCodeSecurityLevel securityLevel)
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class FakeShutdownAwareRuntime : IShutdownAwareDynamicCodeExecutionRuntime, IDisposable
        {
            private readonly TaskCompletionSource<bool> _shutdownCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int ShutdownCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public bool SupportsAutoPrewarm()
            {
                return true;
            }

            public Task<ExecutionResult> ExecuteAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
                DynamicCodeExecutionRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
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
