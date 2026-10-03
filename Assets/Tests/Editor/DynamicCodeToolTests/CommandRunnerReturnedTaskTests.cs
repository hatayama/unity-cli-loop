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
    /// Every returned task is already finished, so no test waits on user code. The custom awaitables count their
    /// results and throw past a bound, so a walk without its depth cap fails instead of hanging the Editor.
    /// </summary>
    public sealed class CommandRunnerReturnedTaskTests
    {
        private const string CounterParameterName = "counter";

        // Far above the depth cap, so only a walk without the cap reaches it.
        private const int UnboundedWalkResultLimit = 1000;
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

        /// <summary>
        /// Verifies a returned finished custom awaitable is awaited, so the value its awaiter yields becomes the result.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsCustomAwaitable_ReturnsItsValue()
        {
            ExecutionResult result = await CreateRunner().ExecuteAsync(CreateContext(typeof(ReturnsValueAwaitable)));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo("7"));
        }

        /// <summary>
        /// Verifies a returned class awaitable whose result is itself is awaited once and then stops.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsSelfReturningClassAwaitable_StopsAfterOneAwait()
        {
            ResultCounter counter = new ResultCounter();

            ExecutionResult result = await CreateRunner().ExecuteAsync(
                CreateContextWithCounter(typeof(ReturnsSelfReturningClassAwaitable), counter));

            Assert.That(result.Success, Is.True);
            Assert.That(counter.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a returned struct awaitable whose result is itself, boxed anew on every await, stops at the depth cap.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsSelfReturningStructAwaitable_StopsAtTheDepthCap()
        {
            ResultCounter counter = new ResultCounter();

            ExecutionResult result = await CreateRunner().ExecuteAsync(
                CreateContextWithCounter(typeof(ReturnsSelfReturningStructAwaitable), counter));

            Assert.That(result.Success, Is.True);
            Assert.That(counter.Count, Is.InRange(1, AwaitableHelper.MaxReturnedAwaitableDepth));
        }

        /// <summary>
        /// Verifies a returned awaitable that keeps yielding another awaitable stops at the depth cap.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenSnippetReturnsEndlessAwaitableChain_StopsAtTheDepthCap()
        {
            ResultCounter counter = new ResultCounter();

            ExecutionResult result = await CreateRunner().ExecuteAsync(
                CreateContextWithCounter(typeof(ReturnsEndlessAwaitableChain), counter));

            Assert.That(result.Success, Is.True);
            Assert.That(counter.Count, Is.InRange(1, AwaitableHelper.MaxReturnedAwaitableDepth));
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

        private static DynamicExecutionContext CreateContextWithCounter(Type commandType, ResultCounter counter)
        {
            DynamicExecutionContext context = CreateContext(commandType);
            context.Parameters[CounterParameterName] = counter;
            return context;
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

        /// <summary>
        /// Counts results handed out by a test awaitable and fails once a walk has clearly lost its bound.
        /// </summary>
        private sealed class ResultCounter
        {
            public int Count { get; private set; }

            public void Increment()
            {
                Count++;
                if (Count > UnboundedWalkResultLimit)
                {
                    throw new InvalidOperationException("The returned-awaitable walk did not stop.");
                }
            }
        }

        /// <summary>
        /// A finished awaiter that hands out whatever its factory produces.
        /// </summary>
        private sealed class FinishedAwaiter
        {
            private readonly Func<object> _result;

            public FinishedAwaiter(Func<object> result)
            {
                _result = result;
            }

            public bool IsCompleted => true;

            public object GetResult() => _result();

            public void OnCompleted(Action continuation) => continuation();
        }

        private sealed class ValueAwaitable
        {
            public FinishedAwaiter GetAwaiter() => new FinishedAwaiter(() => 7);
        }

        private sealed class SelfReturningClassAwaitable
        {
            private readonly ResultCounter _counter;

            public SelfReturningClassAwaitable(ResultCounter counter)
            {
                _counter = counter;
            }

            public FinishedAwaiter GetAwaiter() => new FinishedAwaiter(() =>
            {
                _counter.Increment();
                return this;
            });
        }

        private struct SelfReturningStructAwaitable
        {
            private readonly ResultCounter _counter;

            public SelfReturningStructAwaitable(ResultCounter counter)
            {
                _counter = counter;
            }

            public FinishedAwaiter GetAwaiter()
            {
                SelfReturningStructAwaitable self = this;
                return new FinishedAwaiter(() =>
                {
                    self._counter.Increment();
                    return self;
                });
            }
        }

        private sealed class EndlessAwaitableChain
        {
            private readonly ResultCounter _counter;

            public EndlessAwaitableChain(ResultCounter counter)
            {
                _counter = counter;
            }

            public FinishedAwaiter GetAwaiter() => new FinishedAwaiter(() =>
            {
                _counter.Increment();
                return new EndlessAwaitableChain(_counter);
            });
        }

        private sealed class ReturnsValueAwaitable
        {
            public Task<object> ExecuteAsync() => Task.FromResult<object>(new ValueAwaitable());
        }

        private sealed class ReturnsSelfReturningClassAwaitable
        {
            public Task<object> ExecuteAsync(Dictionary<string, object> parameters) =>
                Task.FromResult<object>(new SelfReturningClassAwaitable((ResultCounter)parameters[CounterParameterName]));
        }

        private sealed class ReturnsSelfReturningStructAwaitable
        {
            public Task<object> ExecuteAsync(Dictionary<string, object> parameters) =>
                Task.FromResult<object>(new SelfReturningStructAwaitable((ResultCounter)parameters[CounterParameterName]));
        }

        private sealed class ReturnsEndlessAwaitableChain
        {
            public Task<object> ExecuteAsync(Dictionary<string, object> parameters) =>
                Task.FromResult<object>(new EndlessAwaitableChain((ResultCounter)parameters[CounterParameterName]));
        }
    }
}
