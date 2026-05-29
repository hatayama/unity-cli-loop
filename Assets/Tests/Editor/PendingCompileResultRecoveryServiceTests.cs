using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests pending compile result recovery without invoking Unity's real compiler.
    /// </summary>
    [TestFixture]
    public sealed class PendingCompileResultRecoveryServiceTests
    {
        private UnityCliLoopEditorSessionStateService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore(_sessionStateService);
        }

        [Test]
        public void Recover_WhenPendingResultIsMissing_PersistsIndeterminateResultAndClearsSession()
        {
            // Verifies Domain Reload recovery creates the result file that the CLI is polling.
            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: false);
            string savedRequestId = "";
            UnityCliLoopCompileResult savedResult = null;
            PendingCompileResultRecoveryService recoveryService = new PendingCompileResultRecoveryService(
                _sessionStateService,
                () => false,
                _ => false,
                (requestId, result) =>
                {
                    savedRequestId = requestId;
                    savedResult = result;
                },
                () => "<PROJECT_ROOT>");

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: false);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(savedRequestId, Is.EqualTo("compile_test_request"));
            Assert.That(savedResult, Is.Not.Null);
            Assert.That(savedResult.Success, Is.Null);
            Assert.That(savedResult.ProjectRoot, Is.EqualTo("<PROJECT_ROOT>"));
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void Recover_WhenResultAlreadyExists_ClearsSessionWithoutSaving()
        {
            // Verifies recovery does not overwrite a result that normal compile persistence already wrote.
            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: false);
            int saveCallCount = 0;
            PendingCompileResultRecoveryService recoveryService = new PendingCompileResultRecoveryService(
                _sessionStateService,
                () => false,
                _ => true,
                (_, _) => saveCallCount++,
                () => "<PROJECT_ROOT>");

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: false);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(saveCallCount, Is.EqualTo(0));
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void Recover_WhenEditorIsStillCompiling_KeepsPendingRequestForRetry()
        {
            // Verifies recovery waits briefly while Unity still reports an active compile.
            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: true);
            int saveCallCount = 0;
            PendingCompileResultRecoveryService recoveryService = new PendingCompileResultRecoveryService(
                _sessionStateService,
                () => true,
                _ => false,
                (_, _) => saveCallCount++,
                () => "<PROJECT_ROOT>");

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: false);

            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Retry));
            Assert.That(saveCallCount, Is.EqualTo(0));
            Assert.That(pendingCompileRequest.HasRequest, Is.True);
            Assert.That(pendingCompileRequest.RequestId, Is.EqualTo("compile_test_request"));
        }

        [Test]
        public void Recover_WhenForcedAfterWait_PersistsForcedCompileMessage()
        {
            // Verifies timeout recovery still completes forced compile requests that never become idle.
            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: true);
            UnityCliLoopCompileResult savedResult = null;
            PendingCompileResultRecoveryService recoveryService = new PendingCompileResultRecoveryService(
                _sessionStateService,
                () => true,
                _ => false,
                (_, result) => savedResult = result,
                () => "<PROJECT_ROOT>");

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: true);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(savedResult, Is.Not.Null);
            Assert.That(savedResult.Message, Does.Contain("Force compilation"));
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }
    }
}
