using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class DynamicCodeServicesTests
    {
        private TimeSpan _previousDrainTimeout;
        private bool _drainTimeoutSwapped;

        [SetUp]
        public void SetUp()
        {
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            DynamicCodeServices.ResetStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_drainTimeoutSwapped)
            {
                DynamicCodeServices.SwapDrainTimeoutForTests(_previousDrainTimeout);
                _drainTimeoutSwapped = false;
            }

            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            DynamicCodeServices.ResetStateForTests();
        }

        [Test]
        [Timeout(5000)]
        public async Task AwaitDrainTaskAsync_WhenDrainTaskIsIncomplete_ShouldWaitForCompletion()
        {
            TaskCompletionSource<bool> drainTaskCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task awaitTask = DynamicCodeServices.AwaitDrainTaskAsync(drainTaskCompletionSource.Task);

            Assert.That(awaitTask.IsCompleted, Is.False);

            drainTaskCompletionSource.SetResult(true);
            await awaitTask;

            Assert.That(awaitTask.IsCompleted, Is.True);
            Assert.That(drainTaskCompletionSource.Task.IsCompleted, Is.True);
        }

        [Test]
        [Timeout(5000)]
        public async Task AwaitDrainTaskAsync_WhenDrainTaskExceedsTimeout_ShouldContinueWithoutWaitingForCompletion()
        {
            _previousDrainTimeout = DynamicCodeServices.SwapDrainTimeoutForTests(TimeSpan.FromMilliseconds(10));
            _drainTimeoutSwapped = true;

            TaskCompletionSource<bool> drainTaskCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await DynamicCodeServices.AwaitDrainTaskAsync(drainTaskCompletionSource.Task);

            Assert.That(drainTaskCompletionSource.Task.IsCompleted, Is.False);
        }

        [Test]
        [Timeout(10000)]
        public async Task ResetServerScopedServices_WhenRuntimeShutdownBlocks_ShouldCleanupSharedWorkerBeforeDrainCompletes()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class DynamicCodeServicesResetCleanupTest { public static int Execute() { return 41; } }",
                out string workerDirectoryPath);

            Assert.That(messages, Is.Not.Null);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.True);

            TaskCompletionSource<bool> shutdownCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenSource lifetimeCancellationTokenSource = new();
            StubDynamicCodeExecutorPool executorPool = new();
            BlockingShutdownRuntime runtime = new(shutdownCompletionSource.Task);
            DynamicCodeServices.SetServerScopedServicesForTests(
                lifetimeCancellationTokenSource,
                executorPool,
                runtime,
                null);

            DynamicCodeServices.ResetServerScopedServices();

            Assert.That(lifetimeCancellationTokenSource.IsCancellationRequested, Is.True);
            Assert.That(runtime.ShutdownCallCount, Is.EqualTo(1));
            Assert.That(Directory.Exists(workerDirectoryPath), Is.False);

            shutdownCompletionSource.SetResult(true);
            await DynamicCodeServices.GetServerScopedDrainTaskForTests();
        }

        private static CompilerMessage[] CompileWithWorker(string source, out string workerDirectoryPath)
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            DynamicReferenceSetBuilderService referenceSetBuilder = new();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);

            string tempDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"DynamicCodeServicesTests_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectoryPath);

            try
            {
                string sourcePath = Path.Combine(tempDirectoryPath, "DynamicCodeServicesResetCleanupTest.cs");
                string dllPath = Path.Combine(tempDirectoryPath, "DynamicCodeServicesResetCleanupTest.dll");
                string requestFilePath = Path.Combine(tempDirectoryPath, "DynamicCodeServicesResetCleanupTest.worker");

                File.WriteAllText(sourcePath, source);
                File.WriteAllLines(
                    requestFilePath,
                    new[] { Path.GetFullPath(sourcePath), Path.GetFullPath(dllPath) }
                        .Concat(references.Select(Path.GetFullPath)));

                CompilerMessage[] messages = SharedRoslynCompilerWorkerHost.TryCompile(
                    requestFilePath,
                    externalCompilerPaths,
                    CancellationToken.None,
                    () => { },
                    () => { },
                    () => { });

                workerDirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "uLoopMCPCompilation",
                    $"RoslynWorker-{Process.GetCurrentProcess().Id}");
                return messages;
            }
            finally
            {
                if (Directory.Exists(tempDirectoryPath))
                {
                    Directory.Delete(tempDirectoryPath, true);
                }
            }
        }

        private sealed class BlockingShutdownRuntime : IShutdownAwareDynamicCodeExecutionRuntime
        {
            private readonly Task _shutdownTask;

            public BlockingShutdownRuntime(Task shutdownTask)
            {
                _shutdownTask = shutdownTask;
            }

            public int ShutdownCallCount { get; private set; }

            public bool SupportsAutoPrewarm()
            {
                return false;
            }

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
                return _shutdownTask;
            }
        }

        private sealed class StubDynamicCodeExecutorPool : IDynamicCodeExecutorPool
        {
            public void Dispose()
            {
            }

            public IDynamicCodeExecutor GetOrCreate(DynamicCodeSecurityLevel securityLevel)
            {
                throw new System.NotSupportedException();
            }
        }
    }
}
