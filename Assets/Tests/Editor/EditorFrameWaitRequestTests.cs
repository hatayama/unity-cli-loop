using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the completion and cancellation lifecycle of a single Editor frame wait request.
    /// </summary>
    public sealed class EditorFrameWaitRequestTests
    {
        /// <summary>
        /// Verifies that a request is ready only once the current frame reaches its target frame.
        /// </summary>
        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        public void IsReady_WhenComparedWithTargetFrame_ReturnsWhetherTargetIsReached(int currentFrameCount, bool expected)
        {
            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            EditorFrameWaitRequest request = new EditorFrameWaitRequest(completionSource, 5, _ => { });

            Assert.That(request.IsReady(currentFrameCount), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that completing a request without a cancellable token resolves its task with true.
        /// </summary>
        [Test]
        public void Complete_WhenTokenCannotBeCanceled_ResolvesTaskWithTrue()
        {
            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            EditorFrameWaitRequest request = new EditorFrameWaitRequest(completionSource, 1, _ => { });
            request.RegisterCancellation(CancellationToken.None);

            request.Complete();

            Assert.That(completionSource.Task.IsCompleted, Is.True);
            Assert.That(completionSource.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(completionSource.Task.GetAwaiter().GetResult(), Is.True);
        }

        /// <summary>
        /// Verifies that cancelling the registered token asks the owner to remove the request, and the removal cancels the task with that token.
        /// </summary>
        [Test]
        public void RegisterCancellation_WhenTokenIsCanceled_RemovesRequestAndCancelsTaskWithToken()
        {
            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            RecordingRemover remover = new RecordingRemover(true);
            EditorFrameWaitRequest request = new EditorFrameWaitRequest(completionSource, 1, remover.Remove);
            CancellationTokenSource cancellation = new CancellationTokenSource();

            try
            {
                request.RegisterCancellation(cancellation.Token);
                cancellation.Cancel();

                Assert.That(remover.RemovedCount, Is.EqualTo(1));
                Assert.That(remover.LastRemoved, Is.SameAs(request));
                Assert.That(completionSource.Task.IsCanceled, Is.True);
                OperationCanceledException exception = (OperationCanceledException)Assert.Throws(
                    Is.InstanceOf<OperationCanceledException>(),
                    () => completionSource.Task.GetAwaiter().GetResult());
                Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// Verifies that a completed request stops listening to its token, so a later cancellation does not ask for removal.
        /// </summary>
        [Test]
        public void Complete_WhenTokenIsCanceledAfterwards_DoesNotRequestRemoval()
        {
            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            RecordingRemover remover = new RecordingRemover(false);
            EditorFrameWaitRequest request = new EditorFrameWaitRequest(completionSource, 1, remover.Remove);
            CancellationTokenSource cancellation = new CancellationTokenSource();

            try
            {
                request.RegisterCancellation(cancellation.Token);
                request.Complete();
                cancellation.Cancel();

                Assert.That(remover.RemovedCount, Is.EqualTo(0));
                Assert.That(completionSource.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// Verifies that a token registered after the request already completed is released at once, so its cancellation does not ask for removal.
        /// </summary>
        [Test]
        public void RegisterCancellation_WhenRequestAlreadyCompleted_DoesNotRequestRemovalOnCancel()
        {
            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            RecordingRemover remover = new RecordingRemover(false);
            EditorFrameWaitRequest request = new EditorFrameWaitRequest(completionSource, 1, remover.Remove);
            CancellationTokenSource cancellation = new CancellationTokenSource();

            try
            {
                request.Complete();
                request.RegisterCancellation(cancellation.Token);
                cancellation.Cancel();

                Assert.That(remover.RemovedCount, Is.EqualTo(0));
                Assert.That(completionSource.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// Verifies that a test-driven cancellation cancels the task without attaching a cancellation token.
        /// </summary>
        [Test]
        public void CancelFromTest_WhenPending_CancelsTaskWithoutToken()
        {
            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            EditorFrameWaitRequest request = new EditorFrameWaitRequest(completionSource, 1, _ => { });
            request.RegisterCancellation(CancellationToken.None);

            request.CancelFromTest();

            Assert.That(completionSource.Task.IsCanceled, Is.True);
            OperationCanceledException exception = (OperationCanceledException)Assert.Throws(
                Is.InstanceOf<OperationCanceledException>(),
                () => completionSource.Task.GetAwaiter().GetResult());
            Assert.That(exception.CancellationToken, Is.EqualTo(CancellationToken.None));
        }

        // Plays the owning service: records removal requests and, when asked to, cancels the request
        // the way the service does after removing it from its pending list.
        private sealed class RecordingRemover
        {
            private readonly bool _cancelOnRemove;

            public RecordingRemover(bool cancelOnRemove)
            {
                _cancelOnRemove = cancelOnRemove;
            }

            public int RemovedCount { get; private set; }

            public EditorFrameWaitRequest LastRemoved { get; private set; }

            public void Remove(EditorFrameWaitRequest request)
            {
                RemovedCount++;
                LastRemoved = request;
                if (_cancelOnRemove)
                {
                    request.CancelFromCancellation();
                }
            }
        }
    }
}
