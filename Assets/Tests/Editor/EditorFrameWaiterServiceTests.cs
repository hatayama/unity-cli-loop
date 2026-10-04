using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the frame waiter service state that the static facade tests do not observe.
    /// Frames are advanced by calling UpdateRequests directly and the timeout is a fake the test completes,
    /// so no test registers on the real Editor update loop or waits on wall-clock time.
    /// </summary>
    public sealed class EditorFrameWaiterServiceTests
    {
        /// <summary>
        /// Verifies that a new service has no pending frame waits.
        /// </summary>
        [Test]
        public void PendingWaitCount_WhenServiceIsNew_ReturnsZero()
        {
            EditorFrameWaiterService service = new EditorFrameWaiterService();

            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a zero-frame wait succeeds immediately without starting a timeout.
        /// </summary>
        [Test]
        public async Task WaitFramesOrTimeoutAsync_WhenFrameCountIsZero_ReturnsTrueWithoutTimeout()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);

            bool completed = await UncanceledAwaits.AwaitValueAsync(
                service.WaitFramesOrTimeoutAsync(0, 100, CancellationToken.None));

            Assert.That(completed, Is.True);
            Assert.That(timeout.Requests, Is.Empty);
        }

        /// <summary>
        /// Verifies that the wait completes only once the requested number of frames has elapsed,
        /// and that reaching the frames cancels the pending timeout.
        /// </summary>
        [Test]
        public async Task WaitFramesOrTimeoutAsync_WhenFramesElapse_ReturnsTrueAndCancelsTimeout()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);

            Task<bool> wait = service.WaitFramesOrTimeoutAsync(2, 100, CancellationToken.None);
            service.UpdateRequests();

            Assert.That(wait.IsCompleted, Is.False);
            Assert.That(service.PendingWaitCount, Is.EqualTo(1));

            service.UpdateRequests();
            // Why before the await: a frame that never completes would otherwise hang until the test timeout.
            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
            bool completed = await UncanceledAwaits.AwaitValueAsync(wait);

            Assert.That(completed, Is.True);
            Assert.That(timeout.Requests, Is.EqualTo(new[] { 100 }));
            Assert.That(timeout.LastTask.IsCanceled, Is.True);
        }

        /// <summary>
        /// Verifies that one frame tick completes only the requests whose target frame was reached.
        /// </summary>
        [Test]
        public async Task UpdateRequests_WhenRequestsTargetDifferentFrames_CompletesOnlyReadyRequests()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);

            Task<bool> shortWait = service.WaitFramesOrTimeoutAsync(1, 100, CancellationToken.None);
            Task<bool> longWait = service.WaitFramesOrTimeoutAsync(3, 100, CancellationToken.None);
            service.UpdateRequests();
            Assert.That(service.PendingWaitCount, Is.EqualTo(1));
            bool shortCompleted = await UncanceledAwaits.AwaitValueAsync(shortWait);

            Assert.That(shortCompleted, Is.True);
            Assert.That(longWait.IsCompleted, Is.False);

            service.UpdateRequests();
            service.UpdateRequests();
            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
            bool longCompleted = await UncanceledAwaits.AwaitValueAsync(longWait);

            Assert.That(longCompleted, Is.True);
        }

        /// <summary>
        /// Verifies that a timeout before the frames arrive returns false and removes the frame request.
        /// </summary>
        [Test]
        public async Task WaitFramesOrTimeoutAsync_WhenTimeoutElapsesFirst_ReturnsFalseAndRemovesRequest()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);

            Task<bool> wait = service.WaitFramesOrTimeoutAsync(2, 100, CancellationToken.None);
            timeout.CompleteLast();
            bool completed = await UncanceledAwaits.AwaitValueAsync(wait);

            Assert.That(completed, Is.False);
            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that canceling the caller token cancels the wait and removes the frame request.
        /// </summary>
        [Test]
        public async Task WaitFramesOrTimeoutAsync_WhenCallerCancels_ThrowsAndRemovesRequest()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);
            using CancellationTokenSource cancellationSource = new CancellationTokenSource();

            Task<bool> wait = service.WaitFramesOrTimeoutAsync(2, 100, cancellationSource.Token);
            cancellationSource.Cancel();

            // Why not Assert.ThrowsAsync: it blocks the main thread synchronously in this NUnit version.
            try
            {
                await wait;
                Assert.Fail("The wait should be canceled with the caller token.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that an already-canceled caller token is rejected before any frame request is queued.
        /// </summary>
        [Test]
        public async Task WaitFramesOrTimeoutAsync_WhenTokenIsAlreadyCanceled_ThrowsWithoutQueueing()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);
            using CancellationTokenSource cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            try
            {
                await service.WaitFramesOrTimeoutAsync(2, 100, cancellationSource.Token);
                Assert.Fail("The wait should reject an already-canceled token.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
            Assert.That(timeout.Requests, Is.Empty);
        }

        /// <summary>
        /// Verifies that ClearAllForTests cancels a pending wait and cancels its timeout.
        /// </summary>
        [Test]
        public async Task ClearAllForTests_WhenWaitIsPending_CancelsWaitAndTimeout()
        {
            FakeTimeout timeout = new FakeTimeout();
            EditorFrameWaiterService service = new EditorFrameWaiterService(timeout.Wait);

            Task<bool> wait = service.WaitFramesOrTimeoutAsync(2, 100, CancellationToken.None);
            service.ClearAllForTests();

            try
            {
                await wait;
                Assert.Fail("ClearAllForTests should cancel the pending wait.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
            Assert.That(timeout.LastTask.IsCanceled, Is.True);
        }

        /// <summary>
        /// Timeout fake built like TimerDelay.Wait: continuations run asynchronously and the token cancels it,
        /// so canceling a token never runs the service's continuation inline on the test thread.
        /// </summary>
        private sealed class FakeTimeout
        {
            private TaskCompletionSource<bool> _lastSource;

            public List<int> Requests { get; } = new List<int>();

            public Task LastTask => _lastSource.Task;

            public Task Wait(int milliseconds, CancellationToken ct)
            {
                Requests.Add(milliseconds);
                TaskCompletionSource<bool> source =
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                ct.Register(() => source.TrySetCanceled(ct));
                _lastSource = source;
                return source.Task;
            }

            public void CompleteLast()
            {
                _lastSource.TrySetResult(true);
            }
        }
    }
}
