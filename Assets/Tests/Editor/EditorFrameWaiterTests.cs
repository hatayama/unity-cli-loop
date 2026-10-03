using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the static Editor frame waiter facade forwards its arguments to the shared service.
    /// </summary>
    public sealed class EditorFrameWaiterTests
    {
        /// <summary>
        /// Verifies that the facade completes a zero-frame wait synchronously with true.
        /// </summary>
        [Test]
        public void WaitFramesOrTimeoutAsync_WhenFrameCountIsZero_CompletesSynchronouslyWithTrue()
        {
            Task<bool> wait = EditorFrameWaiter.WaitFramesOrTimeoutAsync(0, 1000, CancellationToken.None);

            Assert.That(wait.IsCompleted, Is.True);
            Assert.That(wait.GetAwaiter().GetResult(), Is.True);
        }

        /// <summary>
        /// Verifies that the facade forwards the caller's token, so an already cancelled token cancels the wait.
        /// </summary>
        [Test]
        public void WaitFramesOrTimeoutAsync_WhenTokenIsAlreadyCanceled_CompletesSynchronouslyAsCanceled()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                Task<bool> wait = EditorFrameWaiter.WaitFramesOrTimeoutAsync(0, 1000, cancellation.Token);

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
