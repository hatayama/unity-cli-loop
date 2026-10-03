using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the per-flow wait observer holder.
    /// </summary>
    public sealed class MainThreadWaitObservationServiceTests
    {
        /// <summary>
        /// Verifies that the observer set on a service is returned as current, and clearing it returns null.
        /// </summary>
        [Test]
        public void SetCurrent_WhenObserverIsSetThenCleared_ReturnsSetObserverThenNull()
        {
            MainThreadWaitObservationService service = new MainThreadWaitObservationService();
            NoOpWaitObserver observer = new NoOpWaitObserver();

            service.SetCurrent(observer);
            IMainThreadWaitObserver afterSet = service.Current;
            service.SetCurrent(null);

            Assert.That(afterSet, Is.SameAs(observer));
            Assert.That(service.Current, Is.Null);
        }

        private sealed class NoOpWaitObserver : IMainThreadWaitObserver
        {
            public void OnWaitStarted()
            {
            }

            public void OnWaitEnded()
            {
            }
        }
    }
}
