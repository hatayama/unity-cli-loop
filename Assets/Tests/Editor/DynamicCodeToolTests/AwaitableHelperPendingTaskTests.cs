using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies AwaitableHelper with a task that is still pending when it is handed over, and with a completed custom
    /// awaiter that has no GetResult. Every pending task is completed by the test itself before anything awaits it,
    /// so nothing is left waiting.
    /// </summary>
    public sealed class AwaitableHelperPendingTaskTests
    {
        /// <summary>
        /// Verifies a pending task with an already cancelled request is abandoned with a cancellation.
        /// </summary>
        [Test]
        public void AwaitIfNeeded_WithAPendingTaskAndACancelledRequest_ThrowsACancellation()
        {
            TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                Task<object> awaited = AwaitableHelper.AwaitIfNeeded(pending.Task, cancellation.Token);

                Assert.That(awaited.IsCanceled, Is.True);
            }
            finally
            {
                pending.TrySetResult(0);
            }
        }

        /// <summary>
        /// Verifies a pending task with a request that can never be cancelled finishes with the task's result once
        /// the task completes.
        /// </summary>
        [Test]
        public async Task AwaitIfNeeded_WithAPendingTaskAndAnUncancellableRequest_ReturnsTheResultOnceItCompletes()
        {
            TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            Task<object> awaited;
            bool completedBeforeTheTask;
            try
            {
                awaited = AwaitableHelper.AwaitIfNeeded(pending.Task, CancellationToken.None);
                completedBeforeTheTask = awaited.IsCompleted;
            }
            finally
            {
                pending.TrySetResult(5);
            }

            // The helper resumes with ConfigureAwait(false), so its remaining steps may run on a pool thread after the
            // task completes; the task is already complete here, so this await always finishes.
            object result = await awaited;

            Assert.That(completedBeforeTheTask, Is.False);
            Assert.That(result, Is.EqualTo(5));
        }

        /// <summary>
        /// Verifies a custom awaiter that is already complete but has no GetResult yields null.
        /// </summary>
        [Test]
        public void AwaitIfNeeded_WithACompletedAwaiterWithoutGetResult_ReturnsNull()
        {
            Task<object> awaited = AwaitableHelper.AwaitIfNeeded(new ResultlessAwaitable(), CancellationToken.None);

            Assert.That(awaited.IsCompleted, Is.True);
            Assert.That(awaited.Result, Is.Null);
        }

        private sealed class ResultlessAwaitable
        {
            public ResultlessAwaiter GetAwaiter() => new ResultlessAwaiter();
        }

        private sealed class ResultlessAwaiter
        {
            public bool IsCompleted => true;
        }
    }
}
