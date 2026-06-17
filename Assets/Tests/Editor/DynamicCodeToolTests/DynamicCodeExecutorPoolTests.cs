using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Executor Pool behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeExecutorPoolTests
    {
        [Test]
        public void GetOrCreate_WhenRequestedTwice_ShouldReuseExecutor()
        {
            // Verifies the pool keeps a single executor for dynamic code execution.
            FakeDynamicCodeExecutorProvider provider = new();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);

            IDynamicCodeExecutor first = pool.GetOrCreate();
            IDynamicCodeExecutor second = pool.GetOrCreate();

            Assert.That(first, Is.SameAs(second));
            Assert.That(provider.CreateCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_WhenExecutorsWereCreated_ShouldDisposeAllExecutors()
        {
            // Verifies disposing the pool disposes its cached executor once.
            FakeDynamicCodeExecutorProvider provider = new();
            DynamicCodeExecutorPool pool = new(provider);

            pool.GetOrCreate();
            pool.Dispose();

            Assert.That(provider.CreatedExecutors[0].DisposeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void GetOrCreate_AfterDispose_ShouldThrowObjectDisposedException()
        {
            // Verifies the pool fails fast when used after disposal.
            FakeDynamicCodeExecutorProvider provider = new();
            DynamicCodeExecutorPool pool = new(provider);

            pool.Dispose();

            Assert.That(
                () => pool.GetOrCreate(),
                Throws.TypeOf<System.ObjectDisposedException>());
        }

        [Test]
        public void GetOrCreate_WhenProviderReturnsStubFirst_ShouldReplaceItWhenRealExecutorBecomesAvailable()
        {
            // Verifies unavailable compiler stubs are not cached permanently.
            SequenceDynamicCodeExecutorProvider provider = new(
                new DynamicCodeExecutorStub(),
                new FakeDynamicCodeExecutor());
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);

            IDynamicCodeExecutor first = pool.GetOrCreate();
            IDynamicCodeExecutor second = pool.GetOrCreate();

            Assert.That(first, Is.TypeOf<DynamicCodeExecutorStub>());
            Assert.That(second, Is.TypeOf<FakeDynamicCodeExecutor>());
            Assert.That(second, Is.Not.SameAs(first));
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class FakeDynamicCodeExecutorProvider : IDynamicCodeExecutorProvider
        {
            public int CreateCallCount { get; private set; }

            public List<FakeDynamicCodeExecutor> CreatedExecutors { get; } = new();

            public IDynamicCodeExecutor Create()
            {
                CreateCallCount++;

                FakeDynamicCodeExecutor executor = new();
                CreatedExecutors.Add(executor);
                return executor;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class SequenceDynamicCodeExecutorProvider : IDynamicCodeExecutorProvider
        {
            private readonly Queue<IDynamicCodeExecutor> _executors;

            public SequenceDynamicCodeExecutorProvider(params IDynamicCodeExecutor[] executors)
            {
                _executors = new Queue<IDynamicCodeExecutor>(executors);
            }

            public IDynamicCodeExecutor Create()
            {
                return _executors.Dequeue();
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class FakeDynamicCodeExecutor : IDynamicCodeExecutor
        {
            public int DisposeCallCount { get; private set; }

            public Task<ExecutionResult> ExecuteCodeAsync(
                string code,
                string className = DynamicCodeConstants.DEFAULT_CLASS_NAME,
                object[] parameters = null,
                CancellationToken cancellationToken = default,
                bool compileOnly = false)
            {
                return Task.FromResult(new ExecutionResult
                {
                    Success = true,
                    Result = code
                });
            }

            public ExecutionStatistics GetStatistics()
            {
                return new ExecutionStatistics();
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }
    }
}
