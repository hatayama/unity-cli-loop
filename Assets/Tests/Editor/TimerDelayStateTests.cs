using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the timer delay state releases a cancellation registration handed to it after it already completed.
    /// </summary>
    public sealed class TimerDelayStateTests
    {
        /// <summary>
        /// Verifies that a cancellation registration assigned after completion is disposed, so its callback never runs.
        /// </summary>
        [Test]
        public void AssignRegistration_WhenAlreadyCompleted_DisposesRegistration()
        {
            TimerDelayState state = new TimerDelayState(CancellationToken.None);
            CancellationTokenSource cancellation = new CancellationTokenSource();
            int callbackCount = 0;

            try
            {
                state.CompleteFromTimer(null);
                CancellationTokenRegistration registration = cancellation.Token.Register(() => callbackCount++);
                state.AssignRegistration(registration);
                cancellation.Cancel();

                Assert.That(callbackCount, Is.EqualTo(0));
                Assert.That(state.Task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
