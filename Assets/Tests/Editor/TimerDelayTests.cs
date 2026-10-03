using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the timer delay paths that complete without starting a timer.
    /// </summary>
    public sealed class TimerDelayTests
    {
        /// <summary>
        /// Verifies that a non-positive delay completes synchronously without waiting.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        public void Wait_WhenDelayIsNotPositive_CompletesSynchronously(int milliseconds)
        {
            Task wait = TimerDelay.Wait(milliseconds, CancellationToken.None);

            Assert.That(wait.IsCompleted, Is.True);
            Assert.That(wait.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// Verifies that a non-positive delay still honors an already cancelled token by throwing at the call.
        /// </summary>
        [Test]
        public void Wait_WhenDelayIsZeroAndTokenIsCanceled_ThrowsOperationCanceledException()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            try
            {
                Assert.Throws(
                    Is.InstanceOf<OperationCanceledException>(),
                    () => TimerDelay.Wait(0, cancellation.Token));
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        /// <summary>
        /// Verifies that a missing action faults the returned task with ArgumentNullException before any wait starts.
        /// </summary>
        [Test]
        public void WaitThenExecuteOnMainThread_WhenActionIsNull_FaultsWithArgumentNullException()
        {
            Task wait = TimerDelay.WaitThenExecuteOnMainThread(0, null, CancellationToken.None);

            Assert.That(wait.IsCompleted, Is.True);
            Assert.That(wait.IsFaulted, Is.True);
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => wait.GetAwaiter().GetResult());
            Assert.That(exception.ParamName, Is.EqualTo("action"));
        }
    }
}
