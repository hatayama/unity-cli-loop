using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class CompilationReadinessStatePublisherTests
    {
        private ServerReadinessStateStore _stateStore;
        private CompilationReadinessStatePublisher _service;

        [SetUp]
        public void SetUp()
        {
            _stateStore = CreateTestStateStore();
            _service = new CompilationReadinessStatePublisher(_stateStore);
        }

        [TearDown]
        public void TearDown()
        {
            _stateStore.Delete();
        }

        [Test]
        public void MarkCompilationFinished_WhenPreviousStateWasReady_ShouldRestoreReadyState()
        {
            // Verifies compile failures return CLI waiters to the already-probed ready state.
            _stateStore.Write(
                ServerReadinessPhase.Ready,
                "previous-ready",
                "server-ready",
                "project-ipc-endpoint",
                null);

            _service.MarkCompilationStarted();

            _service.MarkCompilationFinished();

            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("ready"));
            Assert.That(state.Endpoint, Is.EqualTo("project-ipc-endpoint"));
        }

        [Test]
        public void MarkCompilationFinished_WhenPreviousStateWasStarting_ShouldRestoreStartingState()
        {
            // Verifies startup recovery is not marked ready by a compile-finished callback.
            _stateStore.Write(
                ServerReadinessPhase.Starting,
                "previous-starting",
                "manual-start",
                null,
                null);

            _service.MarkCompilationStarted();

            _service.MarkCompilationFinished();

            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("starting"));
        }

        private static ServerReadinessStateStore CreateTestStateStore()
        {
            string projectRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unity-cli-loop-tests",
                System.Guid.NewGuid().ToString("N"));
            return new ServerReadinessStateStore(projectRoot);
        }
    }
}
