using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the frame waiter service paths that complete without waiting for Editor frames.
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
        /// Verifies that waiting for zero frames completes synchronously with true and registers no pending wait.
        /// </summary>
        [Test]
        public void WaitFramesOrTimeoutAsync_WhenFrameCountIsZero_CompletesSynchronouslyWithTrue()
        {
            EditorFrameWaiterService service = new EditorFrameWaiterService();

            Task<bool> wait = service.WaitFramesOrTimeoutAsync(0, 1000, CancellationToken.None);

            Assert.That(wait.IsCompleted, Is.True);
            Assert.That(wait.GetAwaiter().GetResult(), Is.True);
            Assert.That(service.PendingWaitCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that an already cancelled token cancels the wait synchronously, even when no frames are requested.
        /// </summary>
        [Test]
        public void WaitFramesOrTimeoutAsync_WhenTokenIsAlreadyCanceled_CompletesSynchronouslyAsCanceled()
        {
            EditorFrameWaiterService service = new EditorFrameWaiterService();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                Task<bool> wait = service.WaitFramesOrTimeoutAsync(0, 1000, cancellation.Token);

                Assert.That(wait.IsCompleted, Is.True);
                Assert.That(wait.IsCanceled, Is.True);
                Assert.Throws(
                    Is.InstanceOf<OperationCanceledException>(),
                    () => wait.GetAwaiter().GetResult());
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
