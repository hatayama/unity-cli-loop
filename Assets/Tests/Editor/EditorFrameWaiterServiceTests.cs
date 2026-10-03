using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the frame waiter service state that the static facade tests do not observe.
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
    }
}
