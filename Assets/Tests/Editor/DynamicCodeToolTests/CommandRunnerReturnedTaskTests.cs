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
    /// Verifies that a task a snippet returns from ExecuteAsync is awaited like one it awaited itself: its value
    /// becomes the result, its fault becomes a failed result, and its cancellation becomes the cancelled result.
    /// Every returned task is already finished, so no test waits on user code.
    /// </summary>
    public sealed class CommandRunnerReturnedTaskTests
    {
        [TearDown]
        public void TearDown()
        {
            UloopDynamicCodePartialResults.Clear();
        }

        /// <summary>
        /// Verifies a returned completed Task of int yields its value as the result.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsCompletedGenericTask_ReturnsItsValue()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsCompletedGenericTask)));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo("42"));
        }

        /// <summary>
        /// Verifies a returned completed non-generic Task yields an empty result instead of its type name.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsCompletedTask_ReturnsEmptyResult()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsCompletedTask)));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo(""));
        }

        /// <summary>
        /// Verifies a returned faulted Task becomes a failed result carrying the fault's message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsFaultedTask_ReturnsItsMessage()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsFaultedTask)));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("returned boom"));
        }

        /// <summary>
        /// Verifies a returned cancelled Task becomes the cancelled result rather than a success.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsCancelledTask_ReturnsTheCancelledResult()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsCancelledTask)));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED));
        }

        /// <summary>
        /// Verifies a task nested in a returned task is awaited too, so the innermost value becomes the result.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsNestedTask_ReturnsInnermostValue()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsNestedTask)));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo("5"));
        }

        /// <summary>
        /// Verifies a returned faulted ValueTask becomes a failed result carrying the fault's message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsFaultedValueTask_ReturnsItsMessage()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsFaultedValueTask)));

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("value boom"));
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
                Parameters = new Dictionary<string, object>(),
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

        private sealed class ReturnsCompletedGenericTask
        {
            public Task<object> ExecuteAsync() => Task.FromResult<object>(Task.FromResult(42));
        }

        private sealed class ReturnsCompletedTask
        {
            public Task<object> ExecuteAsync() => Task.FromResult<object>(Task.CompletedTask);
        }

        private sealed class ReturnsFaultedTask
        {
            public Task<object> ExecuteAsync() =>
                Task.FromResult<object>(Task.FromException(new InvalidOperationException("returned boom")));
        }

        private sealed class ReturnsCancelledTask
        {
            public Task<object> ExecuteAsync() => Task.FromResult<object>(Task.FromCanceled(new CancellationToken(true)));
        }

        private sealed class ReturnsNestedTask
        {
            public Task<object> ExecuteAsync() => Task.FromResult<object>(Task.FromResult(Task.FromResult(5)));
        }

        private sealed class ReturnsFaultedValueTask
        {
            public Task<object> ExecuteAsync() =>
                Task.FromResult<object>(new ValueTask(Task.FromException(new InvalidOperationException("value boom"))));
        }
    }
}
