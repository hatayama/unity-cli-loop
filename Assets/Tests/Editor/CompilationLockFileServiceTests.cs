using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class CompilationLockFileServiceTests
    {
        private ServerReadinessStateStore _stateStore;
        private CompilationLockFileService _service;

        [SetUp]
        public void SetUp()
        {
            _stateStore = CreateTestStateStore();
            _service = new CompilationLockFileService(_stateStore);
        }

        [TearDown]
        public void TearDown()
        {
            _service.DeleteLockFile();
            _stateStore.Delete();
        }

        [Test]
        public void MarkCompilationFinished_ShouldNotPublishReadyState()
        {
            // Verifies that only the server lifecycle readiness probe can publish ready state.
            _service.MarkCompilationStarted();

            _service.MarkCompilationFinished();

            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("compiling"));
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
