using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using DynamicExecutionContext = io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how CommandRunner runs each supported entry point shape and turns a missing assembly, a missing
    /// constructor, a throwing command, and cancellation into results. A fake assembly hands out ordinary test
    /// types, and Undo calls go to no-op hooks.
    /// </summary>
    public sealed class CommandRunnerEntryPointTests
    {
        [TearDown]
        public void TearDown()
        {
            UloopDynamicCodePartialResults.Clear();
        }

        /// <summary>
        /// Verifies a synchronous Execute is run with whichever supported parameters it declares.
        /// </summary>
        [TestCase(typeof(SyncExecuteWithParameters), "parameters:7", TestName = "ExecuteAsync_WithASyncExecuteTakingParameters_PassesTheParameters")]
        [TestCase(typeof(SyncExecuteWithCancellation), "token:False", TestName = "ExecuteAsync_WithASyncExecuteTakingACancellationToken_PassesTheToken")]
        [TestCase(typeof(SyncExecuteWithParametersAndCancellation), "both:7:False", TestName = "ExecuteAsync_WithASyncExecuteTakingParametersAndAToken_PassesBoth")]
        [TestCase(typeof(SyncExecuteWithoutParameters), "none", TestName = "ExecuteAsync_WithAParameterlessSyncExecute_RunsIt")]
        public async Task ExecuteAsync_WithASyncExecute_ReturnsItsResult(Type commandType, string expectedResult)
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(commandType));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Verifies a parameterless ExecuteAsync is run and its result returned.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WithAParameterlessExecuteAsync_ReturnsItsResult()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(AsyncExecuteWithoutParameters)));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo("async-none"));
        }

        /// <summary>
        /// Verifies an exception thrown by a synchronous Execute becomes a failed result carrying its message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenTheSyncExecuteThrows_ReturnsItsMessage()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ThrowingSyncExecute)));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("sync boom"));
            Assert.That(result.Logs[0], Is.EqualTo("Target invocation exception: sync boom"));
        }

        /// <summary>
        /// Verifies a cancellation thrown by Execute or ExecuteAsync becomes the cancelled result rather than an error.
        /// </summary>
        [TestCase(typeof(CancellingSyncExecute), TestName = "ExecuteAsync_WhenTheSyncExecuteThrowsACancellation_ReturnsTheCancelledResult")]
        [TestCase(typeof(CancellingAsyncExecute), TestName = "ExecuteAsync_WhenExecuteAsyncThrowsACancellationBeforeReturning_ReturnsTheCancelledResult")]
        public async Task ExecuteAsync_WhenTheCommandThrowsACancellation_ReturnsTheCancelledResult(Type commandType)
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(commandType));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
        }

        /// <summary>
        /// Verifies a command type without a parameterless constructor becomes a failed result for both entry point kinds.
        /// </summary>
        [TestCase(typeof(SyncExecuteWithoutDefaultConstructor), TestName = "ExecuteAsync_WhenTheSyncCommandHasNoParameterlessConstructor_ReturnsAnExecutionError")]
        [TestCase(typeof(AsyncExecuteWithoutDefaultConstructor), TestName = "ExecuteAsync_WhenTheAsyncCommandHasNoParameterlessConstructor_ReturnsAnExecutionError")]
        public async Task ExecuteAsync_WhenTheCommandHasNoParameterlessConstructor_ReturnsAnExecutionError(Type commandType)
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(commandType));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Logs[0], Does.StartWith("Execution exception: "));
        }

        /// <summary>
        /// Verifies a context without a compiled assembly is rejected with the no-assembly error.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WithoutACompiledAssembly_ReturnsTheNoAssemblyError()
        {
            DynamicExecutionContext context = new DynamicExecutionContext
            {
                CompiledAssembly = null,
                CancellationToken = CancellationToken.None
            };

            ExecutionResult result = await CreateRunner().ExecuteAsync(context);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_NO_COMPILED_ASSEMBLY));
        }

        /// <summary>
        /// Verifies an already cancelled request returns the cancelled result without running the command.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WithAnAlreadyCancelledToken_ReturnsTheCancelledResultWithoutRunning()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            DynamicExecutionContext context = CreateContext(typeof(ThrowingSyncExecute));
            context.CancellationToken = cancellation.Token;

            ExecutionResult result = await CreateRunner().ExecuteAsync(context);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
        }

        private static CommandRunner CreateRunner()
        {
            CommandRunnerUndoHooks hooks = new CommandRunnerUndoHooks
            {
                GetCurrentGroup = () => 0,
                CollapseUndoOperations = group => { },
                IncrementCurrentGroup = () => { }
            };
            return new CommandRunner(new CompiledCommandEntryPointResolver(), hooks);
        }

        private static DynamicExecutionContext CreateContext(Type commandType)
        {
            return new DynamicExecutionContext
            {
                CompiledAssembly = new SingleTypeAssembly(commandType),
                Parameters = new Dictionary<string, object> { { "count", 7 } },
                CancellationToken = CancellationToken.None
            };
        }

        // The resolver looks up the wrapped type by name first and then scans every type, so this assembly hides
        // the name lookup and offers only the type under test.
        private sealed class SingleTypeAssembly : Assembly
        {
            private readonly Type _type;

            public SingleTypeAssembly(Type type)
            {
                _type = type;
            }

            public override Type GetType(string name, bool throwOnError)
            {
                return null;
            }

            public override Type[] GetTypes()
            {
                return new[] { _type };
            }
        }

        private sealed class SyncExecuteWithParameters
        {
            public object Execute(Dictionary<string, object> parameters) => "parameters:" + parameters["count"];
        }

        private sealed class SyncExecuteWithCancellation
        {
            public object Execute(CancellationToken cancellationToken) => "token:" + cancellationToken.IsCancellationRequested;
        }

        private sealed class SyncExecuteWithParametersAndCancellation
        {
            public object Execute(Dictionary<string, object> parameters, CancellationToken cancellationToken) =>
                "both:" + parameters["count"] + ":" + cancellationToken.IsCancellationRequested;
        }

        private sealed class SyncExecuteWithoutParameters
        {
            public object Execute() => "none";
        }

        private sealed class AsyncExecuteWithoutParameters
        {
            public Task<object> ExecuteAsync() => Task.FromResult<object>("async-none");
        }

        private sealed class ThrowingSyncExecute
        {
            public object Execute() => throw new InvalidOperationException("sync boom");
        }

        private sealed class CancellingSyncExecute
        {
            public object Execute() => throw new OperationCanceledException();
        }

        private sealed class CancellingAsyncExecute
        {
            public Task<object> ExecuteAsync() => throw new OperationCanceledException();
        }

        private sealed class SyncExecuteWithoutDefaultConstructor
        {
            public SyncExecuteWithoutDefaultConstructor(int value)
            {
            }

            public object Execute() => "unreachable";
        }

        private sealed class AsyncExecuteWithoutDefaultConstructor
        {
            public AsyncExecuteWithoutDefaultConstructor(int value)
            {
            }

            public Task<object> ExecuteAsync() => Task.FromResult<object>("unreachable");
        }
    }
}
