using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Execution Facade behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeExecutionFacadeTests
    {
        [Test]
        public async Task ExecuteAsync_WhenSameSecurityLevelUsedTwice_ShouldReuseExecutor()
        {
            FakeDynamicCodeExecutorProvider provider = new();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);
            using DynamicCodeExecutionFacade facade = new DynamicCodeExecutionFacade(pool);

            await facade.ExecuteAsync(
                CreateRequest(DynamicCodeSecurityLevel.Restricted, "return 1;"),
                CancellationToken.None);
            await facade.ExecuteAsync(
                CreateRequest(DynamicCodeSecurityLevel.Restricted, "return 2;"),
                CancellationToken.None);

            Assert.That(provider.CreateCallsBySecurityLevel[DynamicCodeSecurityLevel.Restricted], Is.EqualTo(1));
        }

        [Test]
        public async Task ExecuteAsync_WhenSecurityLevelChanges_ShouldCreateSeparateExecutors()
        {
            FakeDynamicCodeExecutorProvider provider = new();
            using DynamicCodeExecutorPool pool = new DynamicCodeExecutorPool(provider);
            using DynamicCodeExecutionFacade facade = new DynamicCodeExecutionFacade(pool);

            await facade.ExecuteAsync(
                CreateRequest(DynamicCodeSecurityLevel.Restricted, "return 1;"),
                CancellationToken.None);
            await facade.ExecuteAsync(
                CreateRequest(DynamicCodeSecurityLevel.FullAccess, "return 2;"),
                CancellationToken.None);

            Assert.That(provider.CreateCallsBySecurityLevel[DynamicCodeSecurityLevel.Restricted], Is.EqualTo(1));
            Assert.That(provider.CreateCallsBySecurityLevel[DynamicCodeSecurityLevel.FullAccess], Is.EqualTo(1));
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
                    CreateRequest(DynamicCodeSecurityLevel.Restricted, "return 1;"),
                    CancellationToken.None);
            });

            facade.Dispose();

            Assert.That(provider.CreatedExecutors[0].DisposeCallCount, Is.EqualTo(1));
        }

        private static DynamicCodeExecutionRequest CreateRequest(
            DynamicCodeSecurityLevel securityLevel,
            string code)
        {
            return new DynamicCodeExecutionRequest
            {
                SecurityLevel = securityLevel,
                Code = code,
                ClassName = "FacadeTestCommand"
            };
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
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

                FakeDynamicCodeExecutor executor = new();
                CreatedExecutors.Add(executor);
                return executor;
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
