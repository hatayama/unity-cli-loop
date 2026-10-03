using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how the awaitable helper unwraps values that are already complete: plain values, Task and
    /// ValueTask shapes, and custom awaiters that finish synchronously. No test waits on anything pending.
    /// </summary>
    public sealed class AwaitableHelperCompletedValueTests
    {
        /// <summary>
        /// Verifies a null value is returned as null.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithNull_ReturnsNull()
        {
            Assert.That(await AwaitableHelper.AwaitIfNeeded(null, CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Verifies a non-generic Task has no result to return.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithACompletedNonGenericTask_ReturnsNull()
        {
            Assert.That(await AwaitableHelper.AwaitIfNeeded(Task.CompletedTask, CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Verifies a completed async Task method has no result to return, even though the runtime backs its
        /// task with an internal generic Task subtype whose Result is a placeholder object.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithACompletedAsyncTaskMethod_ReturnsNull()
        {
            Assert.That(await AwaitableHelper.AwaitIfNeeded(CompleteWithoutResultAsync(), CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Verifies a completed async Task method that returns a value still yields that value.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithACompletedAsyncTaskMethodReturningAValue_ReturnsItsResult()
        {
            Assert.That(await AwaitableHelper.AwaitIfNeeded(CompleteWithResultAsync(), CancellationToken.None), Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies a non-generic ValueTask has no result to return.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithACompletedNonGenericValueTask_ReturnsNull()
        {
            Assert.That(await AwaitableHelper.AwaitIfNeeded(default(ValueTask), CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Verifies a generic ValueTask returns its result.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithACompletedGenericValueTask_ReturnsItsResult()
        {
            object result = await AwaitableHelper.AwaitIfNeeded(new ValueTask<int>(7), CancellationToken.None);

            Assert.That(result, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies a value without an awaiter is returned unchanged.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithAPlainValue_ReturnsItUnchanged()
        {
            object result = await AwaitableHelper.AwaitIfNeeded("plain", CancellationToken.None);

            Assert.That(result, Is.EqualTo("plain"));
        }

        /// <summary>
        /// Verifies a value whose awaiter is null is returned unchanged.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WhenTheAwaiterIsNull_ReturnsTheValue()
        {
            NullAwaiterAwaitable awaitable = new NullAwaiterAwaitable();

            object result = await AwaitableHelper.AwaitIfNeeded(awaitable, CancellationToken.None);

            Assert.That(result, Is.SameAs(awaitable));
        }

        /// <summary>
        /// Verifies an already-completed custom awaiter returns its result without registering a continuation.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithACompletedCustomAwaiter_ReturnsItsResultWithoutRegistering()
        {
            ScriptedAwaiter awaiter = new ScriptedAwaiter { IsCompleted = true, Result = "done" };

            object result = await AwaitableHelper.AwaitIfNeeded(new ScriptedAwaitable(awaiter), CancellationToken.None);

            Assert.That(result, Is.EqualTo("done"));
            Assert.That(awaiter.OnCompletedCalls, Is.EqualTo(0));
            Assert.That(awaiter.UnsafeOnCompletedCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a pending custom awaiter is registered through UnsafeOnCompleted when it has one, and its
        /// result is returned once the continuation runs.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithAPendingCustomAwaiter_PrefersUnsafeOnCompleted()
        {
            ScriptedAwaiter awaiter = new ScriptedAwaiter { Result = "later" };

            object result = await AwaitableHelper.AwaitIfNeeded(new ScriptedAwaitable(awaiter), CancellationToken.None);

            Assert.That(result, Is.EqualTo("later"));
            Assert.That(awaiter.UnsafeOnCompletedCalls, Is.EqualTo(1));
            Assert.That(awaiter.OnCompletedCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a pending custom awaiter without UnsafeOnCompleted is registered through OnCompleted.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithoutUnsafeOnCompleted_RegistersThroughOnCompleted()
        {
            SafeOnlyAwaiter awaiter = new SafeOnlyAwaiter();

            object result = await AwaitableHelper.AwaitIfNeeded(new SafeOnlyAwaitable(awaiter), CancellationToken.None);

            Assert.That(result, Is.EqualTo("safe"));
            Assert.That(awaiter.OnCompletedCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an awaiter with neither IsCompleted nor a way to register a continuation is read directly.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithoutCompletionRegistration_ReadsTheResultDirectly()
        {
            object result = await AwaitableHelper.AwaitIfNeeded(new ResultOnlyAwaitable(), CancellationToken.None);

            Assert.That(result, Is.EqualTo("direct"));
        }

        /// <summary>
        /// Verifies a failure thrown from GetResult inside the continuation surfaces as the original exception
        /// rather than the reflection wrapper.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WhenGetResultThrowsInTheContinuation_SurfacesTheOriginalException()
        {
            ScriptedAwaiter awaiter = new ScriptedAwaiter { Failure = new InvalidOperationException("user failure") };
            Exception caught = null;

            try
            {
                await AwaitableHelper.AwaitIfNeeded(new ScriptedAwaitable(awaiter), CancellationToken.None);
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
            Assert.That(caught.Message, Is.EqualTo("user failure"));
        }

        // Why await a completed task: the method finishes synchronously, so nothing is left pending, while the
        // compiler still builds the task through the async method builder.
        private static async Task CompleteWithoutResultAsync()
        {
            await Task.CompletedTask;
        }

        private static async Task<int> CompleteWithResultAsync()
        {
            await Task.CompletedTask;
            return 7;
        }

        private sealed class NullAwaiterAwaitable
        {
            public ScriptedAwaiter GetAwaiter()
            {
                return null;
            }
        }

        private sealed class ScriptedAwaitable
        {
            private readonly ScriptedAwaiter _awaiter;

            public ScriptedAwaitable(ScriptedAwaiter awaiter)
            {
                _awaiter = awaiter;
            }

            public ScriptedAwaiter GetAwaiter()
            {
                return _awaiter;
            }
        }

        /// <summary>
        /// Awaiter that runs any registered continuation immediately, so nothing stays pending.
        /// </summary>
        private sealed class ScriptedAwaiter
        {
            public bool IsCompleted { get; set; }

            public object Result { get; set; }

            public Exception Failure { get; set; }

            public int OnCompletedCalls { get; private set; }

            public int UnsafeOnCompletedCalls { get; private set; }

            public object GetResult()
            {
                if (Failure != null)
                {
                    throw Failure;
                }

                return Result;
            }

            public void OnCompleted(Action continuation)
            {
                OnCompletedCalls++;
                continuation();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                UnsafeOnCompletedCalls++;
                continuation();
            }
        }

        private sealed class SafeOnlyAwaitable
        {
            private readonly SafeOnlyAwaiter _awaiter;

            public SafeOnlyAwaitable(SafeOnlyAwaiter awaiter)
            {
                _awaiter = awaiter;
            }

            public SafeOnlyAwaiter GetAwaiter()
            {
                return _awaiter;
            }
        }

        private sealed class SafeOnlyAwaiter
        {
            public bool IsCompleted => false;

            public int OnCompletedCalls { get; private set; }

            public object GetResult()
            {
                return "safe";
            }

            public void OnCompleted(Action continuation)
            {
                OnCompletedCalls++;
                continuation();
            }
        }

        private sealed class ResultOnlyAwaitable
        {
            public ResultOnlyAwaiter GetAwaiter()
            {
                return new ResultOnlyAwaiter();
            }
        }

        private sealed class ResultOnlyAwaiter
        {
            public object GetResult()
            {
                return "direct";
            }
        }
    }
}
