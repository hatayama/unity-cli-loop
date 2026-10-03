using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the execute-dynamic-code facade paths that finish without waiting: running when idle, adding timings
    /// to a result that has none, and shutting down while idle. The executor completes synchronously.
    /// </summary>
    public sealed class DynamicCodeExecutionFacadeIdleTests
    {
        /// <summary>
        /// Verifies an idle facade enters the run and returns the executor's result with acquire, executor, and
        /// scheduler timings added to a result that had no timings.
        /// </summary>
        [Test]
        public async Task TryExecuteIfIdleAsync_WhenIdle_EntersAndAddsTimings()
        {
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(new SynchronousExecutorProvider());
            using DynamicCodeExecutionFacade facade = new DynamicCodeExecutionFacade(pool);

            (bool entered, ExecutionResult result) = await facade.TryExecuteIfIdleAsync(
                new DynamicCodeExecutionRequest { Code = "return 1;", ClassName = "IdleProbe" },
                CancellationToken.None);

            Assert.That(entered, Is.True);
            Assert.That(result.Result, Is.EqualTo("return 1;"));
            Assert.That(result.Timings.Count, Is.EqualTo(3));
            Assert.That(result.Timings[0], Does.StartWith("[Perf] ExecutorAcquire: "));
            Assert.That(result.Timings[1], Does.StartWith("[Perf] ExecutorTotal: "));
            Assert.That(result.Timings[2], Does.StartWith("[Perf] SchedulerTotal: "));
        }

        /// <summary>
        /// Verifies shutting down an idle facade completes and disposes the executors it created.
        /// </summary>
        [Test]
        public async Task ShutdownAsync_WhenIdle_CompletesAndDisposesExecutors()
        {
            SynchronousExecutorProvider provider = new SynchronousExecutorProvider();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);
            using DynamicCodeExecutionFacade facade = new DynamicCodeExecutionFacade(pool);
            await facade.ExecuteAsync(
                new DynamicCodeExecutionRequest { Code = "return 1;", ClassName = "IdleProbe" },
                CancellationToken.None);

            Task shutdown = facade.ShutdownAsync();

            Assert.That(shutdown.IsCompleted, Is.True);
            await shutdown;
            Assert.That(provider.LastExecutor.DisposeCallCount, Is.EqualTo(1));
        }

        private sealed class SynchronousExecutorProvider : IDynamicCodeExecutorProvider
        {
            public SynchronousExecutor LastExecutor { get; private set; }

            public IDynamicCodeExecutor Create()
            {
                LastExecutor = new SynchronousExecutor();
                return LastExecutor;
            }
        }

        private sealed class SynchronousExecutor : IDynamicCodeExecutor
        {
            public int DisposeCallCount { get; private set; }

            public Task<ExecutionResult> ExecuteCodeAsync(
                string code,
                string className = DynamicCodeConstants.DEFAULT_CLASS_NAME,
                object[] parameters = null,
                CancellationToken cancellationToken = default,
                bool compileOnly = false)
            {
                return Task.FromResult(new ExecutionResult { Success = true, Result = code, Timings = null });
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }
    }
}
