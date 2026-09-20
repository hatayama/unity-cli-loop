using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code Executor behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeExecutorTests
    {
        [Test]
        public async Task ExecuteCodeAsync_WhenCompilationIsCancelled_ShouldReturnNeutralCancelledMessage()
        {
            CancelledCompilationService compiler = new();
            CountingCompiledCommandInvoker invoker = new();
            DynamicCodeExecutor executor = new(
                compiler,
                invoker,
                new DynamicCodeSourcePreparationService());

            ExecutionResult result = await executor.ExecuteCodeAsync(
                "return 1;",
                cancellationToken: new CancellationToken(canceled: true));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
            Assert.That(result.Logs, Contains.Item("Execution cancelled"));
            Assert.That(invoker.ExecuteAsyncCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ExecuteCodeAsync_WhenCompileOnlyCompilationHasNullTimings_ShouldReturnExecutorStageTimings()
        {
            NullTimingCompilationService compiler = NullTimingCompilationService.CreateSuccessful();
            CountingCompiledCommandInvoker invoker = new();
            DynamicCodeExecutor executor = new(
                compiler,
                invoker,
                new DynamicCodeSourcePreparationService());

            ExecutionResult result = await executor.ExecuteCodeAsync(
                "return 1;",
                cancellationToken: CancellationToken.None,
                compileOnly: true);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Timings, Has.Count.EqualTo(2));
            Assert.That(result.Timings, Has.Some.StartsWith("[Perf] SourcePrepare: "));
            Assert.That(result.Timings, Has.Some.StartsWith("[Perf] CompileTotal: "));
            Assert.That(invoker.ExecuteAsyncCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ExecuteCodeAsync_WhenCompilationFailureHasNullTimings_ShouldReturnExecutorStageTimings()
        {
            NullTimingCompilationService compiler = NullTimingCompilationService.CreateFailed();
            CountingCompiledCommandInvoker invoker = new();
            DynamicCodeExecutor executor = new(
                compiler,
                invoker,
                new DynamicCodeSourcePreparationService());

            ExecutionResult result = await executor.ExecuteCodeAsync(
                "return 1;",
                cancellationToken: CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Compilation error occurred"));
            Assert.That(result.Timings, Has.Count.EqualTo(2));
            Assert.That(result.Timings, Has.Some.StartsWith("[Perf] SourcePrepare: "));
            Assert.That(result.Timings, Has.Some.StartsWith("[Perf] CompileTotal: "));
            Assert.That(invoker.ExecuteAsyncCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ExecuteCodeAsync_WhenCompileOnlyUsesAssemblyBuilderFallback_ShouldSurfaceWarningLog()
        {
            AdvisoryCompilationService compiler = AdvisoryCompilationService.CreateSuccessfulCompileOnly();
            CountingCompiledCommandInvoker invoker = new();
            DynamicCodeExecutor executor = new(
                compiler,
                invoker,
                new DynamicCodeSourcePreparationService());

            ExecutionResult result = await executor.ExecuteCodeAsync(
                "return 1;",
                cancellationToken: CancellationToken.None,
                compileOnly: true);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Logs, Contains.Item("Warning: Fast Roslyn path is unavailable; execute-dynamic-code is using AssemblyBuilder fallback, so new snippets compile slower."));
            Assert.That(result.Timings, Contains.Item("[Perf] Backend: AssemblyBuilderFallback"));
            Assert.That(result.Timings, Has.Some.StartsWith("[Perf] SourcePrepare: "));
            Assert.That(result.Timings, Has.Some.StartsWith("[Perf] CompileTotal: "));
            Assert.That(invoker.ExecuteAsyncCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class CancelledCompilationService : IDynamicCompilationService
        {
            public Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
            {
                throw new OperationCanceledException(ct);
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class CountingCompiledCommandInvoker : ICompiledCommandInvoker
        {
            public int ExecuteAsyncCallCount { get; private set; }

            public Task<ExecutionResult> ExecuteAsync(io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext context)
            {
                ExecuteAsyncCallCount++;
                return Task.FromResult(new ExecutionResult { Success = true });
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        /// <summary>
        /// Verifies a snippet compiles against the on-disk assembly of every active introduced type,
        /// so the snippet can reference such a type directly.
        /// </summary>
        [Test]
        public async Task ExecuteCodeAsync_WhenIntroducedTypesAreActive_CompilesAgainstTheirArtifacts()
        {
            Func<IReadOnlyList<string>> previousDescribe =
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths;
            HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths =
                () => new List<string> { "Library/Introduced/One.dll", "Library/Introduced/Two.dll" };
            try
            {
                RequestCapturingCompilationService compiler = new();
                DynamicCodeExecutor executor = new(
                    compiler,
                    new CountingCompiledCommandInvoker(),
                    new DynamicCodeSourcePreparationService());

                await executor.ExecuteCodeAsync(
                    "return 1;",
                    cancellationToken: CancellationToken.None,
                    compileOnly: true);

                Assert.That(
                    compiler.LastRequest.AdditionalReferences,
                    Is.EqualTo(new[] { "Library/Introduced/One.dll", "Library/Introduced/Two.dll" }));
            }
            finally
            {
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths = previousDescribe;
            }
        }

        /// <summary>
        /// Verifies a snippet compiled while hot reload is not installed asks for no additional
        /// reference, so the request is the one this tool has always sent.
        /// </summary>
        [Test]
        public async Task ExecuteCodeAsync_WhenHotReloadIsNotInstalled_CompilesWithoutAdditionalReferences()
        {
            Func<IReadOnlyList<string>> previousDescribe =
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths;
            HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths = null;
            try
            {
                RequestCapturingCompilationService compiler = new();
                DynamicCodeExecutor executor = new(
                    compiler,
                    new CountingCompiledCommandInvoker(),
                    new DynamicCodeSourcePreparationService());

                await executor.ExecuteCodeAsync(
                    "return 1;",
                    cancellationToken: CancellationToken.None,
                    compileOnly: true);

                Assert.That(compiler.LastRequest.AdditionalReferences, Is.Empty);
            }
            finally
            {
                HotReloadIntroducedTypeCoordination.DescribeActiveArtifactReferencePaths = previousDescribe;
            }
        }

        /// <summary>
        /// Test support type that keeps the request it was asked to compile, so a test can state what
        /// the executor sends rather than what it returns.
        /// </summary>
        private sealed class RequestCapturingCompilationService : IDynamicCompilationService
        {
            internal CompilationRequest LastRequest { get; private set; }

            public Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
            {
                LastRequest = request;
                return Task.FromResult(new CompilationResult { Success = true });
            }
        }

        private sealed class NullTimingCompilationService : IDynamicCompilationService
        {
            private readonly CompilationResult _result;

            private NullTimingCompilationService(CompilationResult result)
            {
                _result = result;
            }

            public static NullTimingCompilationService CreateSuccessful()
            {
                return new NullTimingCompilationService(new CompilationResult
                {
                    Success = true,
                    Timings = null
                });
            }

            public static NullTimingCompilationService CreateFailed()
            {
                return new NullTimingCompilationService(new CompilationResult
                {
                    Success = false,
                    Timings = null
                });
            }

            public Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
            {
                return Task.FromResult(_result);
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class AdvisoryCompilationService : IDynamicCompilationService
        {
            private readonly CompilationResult _result;

            private AdvisoryCompilationService(CompilationResult result)
            {
                _result = result;
            }

            public static AdvisoryCompilationService CreateSuccessfulCompileOnly()
            {
                return new AdvisoryCompilationService(new CompilationResult
                {
                    Success = true,
                    Timings = new List<string> { "[Perf] Backend: AssemblyBuilderFallback" },
                    AdvisoryLogs = new List<string>
                    {
                        "Warning: Fast Roslyn path is unavailable; execute-dynamic-code is using AssemblyBuilder fallback, so new snippets compile slower."
                    },
                    CompilationBackendKind = DynamicCompilationBackendKind.AssemblyBuilderFallback
                });
            }

            public Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
            {
                return Task.FromResult(_result);
            }
        }
    }
}
