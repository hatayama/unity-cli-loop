using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests run-tests status bridge responses without starting Unity Test Runner.
    /// </summary>
    [TestFixture]
    public sealed class RunTestsStatusBridgeCommandTests
    {
        private UnityCliLoopRunTestsSessionRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _repository = new UnityCliLoopRunTestsSessionRepository();
            _repository.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _repository.ClearAll();
        }

        /// <summary>
        /// Verifies a stored result is returned as HasResult with parsed JSON.
        /// </summary>
        [Test]
        public void BuildResponse_WhenResultExists_ReturnsHasResultAndJson()
        {
            _repository.StoreRunResult(
                "run_tests_status_one",
                "{\"Success\":true}",
                System.DateTime.UtcNow);

            GetRunTestsStatusResponse response = RunTestsStatusBridgeCommand.BuildResponse(
                "run_tests_status_one",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _repository);

            Assert.That(response.HasResult, Is.True);
            Assert.That(response.Result, Is.Not.Null);
            Assert.That(response.Result["Success"]?.Value<bool>(), Is.True);
        }

        /// <summary>
        /// Verifies a busy editor without a stored result is not Ready.
        /// </summary>
        [Test]
        public void BuildResponse_WhenBusyAndNoResult_ReturnsNotReady()
        {
            GetRunTestsStatusResponse response = RunTestsStatusBridgeCommand.BuildResponse(
                "run_tests_status_busy",
                isCompiling: true,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _repository);

            Assert.That(response.Ready, Is.False);
            Assert.That(response.HasResult, Is.False);
        }

        /// <summary>
        /// Verifies an idle editor without a stored result is Ready with HasResult false.
        /// </summary>
        [Test]
        public void BuildResponse_WhenIdleAndNoResult_ReturnsReadyWithoutResult()
        {
            GetRunTestsStatusResponse response = RunTestsStatusBridgeCommand.BuildResponse(
                "run_tests_status_idle",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _repository);

            Assert.That(response.Ready, Is.True);
            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
        }

        /// <summary>
        /// Verifies an empty request id never returns a stored result.
        /// </summary>
        [Test]
        public void BuildResponse_WhenRequestIdIsEmpty_ReturnsNoResult()
        {
            _repository.StoreRunResult(
                "run_tests_status_other",
                "{\"Success\":true}",
                System.DateTime.UtcNow);

            GetRunTestsStatusResponse response = RunTestsStatusBridgeCommand.BuildResponse(
                "",
                isCompiling: false,
                isUpdating: false,
                isDomainReloadInProgress: false,
                _repository);

            Assert.That(response.HasResult, Is.False);
            Assert.That(response.Result, Is.Null);
        }
    }
}
