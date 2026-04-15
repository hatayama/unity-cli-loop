using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP.Factory;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class DynamicCodeExecutorPoolTests
    {
        [Test]
        public void GetOrCreate_WhenSameSecurityLevelRequestedTwice_ShouldReuseExecutor()
        {
            FakeDynamicCodeExecutorProvider provider = new FakeDynamicCodeExecutorProvider();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);

            IDynamicCodeExecutor first = pool.GetOrCreate(DynamicCodeSecurityLevel.Restricted);
            IDynamicCodeExecutor second = pool.GetOrCreate(DynamicCodeSecurityLevel.Restricted);

            Assert.That(first, Is.SameAs(second));
            Assert.That(provider.CreateCallsBySecurityLevel[DynamicCodeSecurityLevel.Restricted], Is.EqualTo(1));
        }

        [Test]
        public void Dispose_WhenExecutorsWereCreated_ShouldDisposeAllExecutors()
        {
            FakeDynamicCodeExecutorProvider provider = new FakeDynamicCodeExecutorProvider();
            DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);

            pool.GetOrCreate(DynamicCodeSecurityLevel.Restricted);
            pool.GetOrCreate(DynamicCodeSecurityLevel.FullAccess);
            pool.Dispose();

            Assert.That(provider.CreatedExecutors[0].DisposeCallCount, Is.EqualTo(1));
            Assert.That(provider.CreatedExecutors[1].DisposeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void GetOrCreate_AfterDispose_ShouldThrowObjectDisposedException()
        {
            FakeDynamicCodeExecutorProvider provider = new FakeDynamicCodeExecutorProvider();
            DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);

            pool.Dispose();

            Assert.That(
                () => pool.GetOrCreate(DynamicCodeSecurityLevel.Restricted),
                Throws.TypeOf<System.ObjectDisposedException>());
        }

        [Test]
        public void GetOrCreate_WhenProviderReturnsStubFirst_ShouldReplaceItWhenRealExecutorBecomesAvailable()
        {
            SequenceDynamicCodeExecutorProvider provider = new SequenceDynamicCodeExecutorProvider(
                new DynamicCodeExecutorStub(),
                new FakeDynamicCodeExecutor());
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);

            IDynamicCodeExecutor first = pool.GetOrCreate(DynamicCodeSecurityLevel.Restricted);
            IDynamicCodeExecutor second = pool.GetOrCreate(DynamicCodeSecurityLevel.Restricted);

            Assert.That(first, Is.TypeOf<DynamicCodeExecutorStub>());
            Assert.That(second, Is.TypeOf<FakeDynamicCodeExecutor>());
            Assert.That(second, Is.Not.SameAs(first));
        }

        private sealed class FakeDynamicCodeExecutorProvider : IDynamicCodeExecutorProvider
        {
            public Dictionary<DynamicCodeSecurityLevel, int> CreateCallsBySecurityLevel { get; } = new();

            public List<FakeDynamicCodeExecutor> CreatedExecutors { get; } = new();

            public IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel)
            {
                if (!CreateCallsBySecurityLevel.ContainsKey(securityLevel))
                {
                    CreateCallsBySecurityLevel[securityLevel] = 0;
                }

                CreateCallsBySecurityLevel[securityLevel]++;

                FakeDynamicCodeExecutor executor = new FakeDynamicCodeExecutor();
                CreatedExecutors.Add(executor);
                return executor;
            }
        }

        private sealed class SequenceDynamicCodeExecutorProvider : IDynamicCodeExecutorProvider
        {
            private readonly Queue<IDynamicCodeExecutor> _executors;

            public SequenceDynamicCodeExecutorProvider(params IDynamicCodeExecutor[] executors)
            {
                _executors = new Queue<IDynamicCodeExecutor>(executors);
            }

            public IDynamicCodeExecutor Create(DynamicCodeSecurityLevel securityLevel)
            {
                return _executors.Dequeue();
            }
        }

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
