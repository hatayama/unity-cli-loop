using NUnit.Framework;
using System;

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
        private DateTime _testUtcNow;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
            _testUtcNow = DateTime.UtcNow;
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
                () => "<PROJECT_ROOT>",
                () => _testUtcNow);

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: false);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(savedRequestId, Is.EqualTo("compile_test_request"));
            Assert.That(savedResult, Is.Not.Null);
            Assert.That(savedResult.Success, Is.Null);
            Assert.That(savedResult.ErrorCount, Is.Null);
            Assert.That(savedResult.WarningCount, Is.Null);
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
                () => "<PROJECT_ROOT>",
                () => _testUtcNow);

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: false);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(saveCallCount, Is.EqualTo(0));
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void Recover_WhenPendingRequestIsExpired_ClearsSessionWithoutSaving()
        {
            // Verifies stale compile recovery data does not create a result for a canceled command later.
            _sessionStateService.MarkPendingCompileRequestWithExpiration(
                "compile_test_request",
                forceRecompile: false,
                expiresAtUtcTicks: _testUtcNow.AddSeconds(-1).Ticks);
            int saveCallCount = 0;
            PendingCompileResultRecoveryService recoveryService = new PendingCompileResultRecoveryService(
                _sessionStateService,
                () => false,
                _ => false,
                (_, _) => saveCallCount++,
                () => "<PROJECT_ROOT>",
                () => _testUtcNow);

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: false);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(saveCallCount, Is.EqualTo(0));
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void MarkPendingCompileRequest_WhenCreated_OutlivesAcceptedCompileWaitBudget()
        {
            // Verifies long accepted compiles keep recovery state until the CLI wait budget has elapsed.
            DateTime markedAtUtc = DateTime.UtcNow;

            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: true);
            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            DateTime expiresAtUtc = new DateTime(
                pendingCompileRequest.ExpiresAtUtcTicks,
                DateTimeKind.Utc);
            TimeSpan lifetime = expiresAtUtc - markedAtUtc;

            Assert.That(pendingCompileRequest.HasRequest, Is.True);
            Assert.That(lifetime, Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(31)));
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
                () => "<PROJECT_ROOT>",
                () => _testUtcNow);

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
                () => "<PROJECT_ROOT>",
                () => _testUtcNow);

            PendingCompileRecoveryStatus status = recoveryService.Recover(recoverWhileEditorCompiling: true);

            Assert.That(status, Is.EqualTo(PendingCompileRecoveryStatus.Completed));
            Assert.That(savedResult, Is.Not.Null);
            Assert.That(savedResult.Message, Does.Contain("Force compilation"));
            Assert.That(savedResult.ErrorCount, Is.Null);
            Assert.That(savedResult.WarningCount, Is.Null);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void ShouldClearPendingCompileRequestAfterCancellation_WhenCallerCancelsBeforeReload_ReturnsTrue()
        {
            // Verifies caller cancellation clears pending recovery before it can become stale.
            UnityCliLoopCompileRequest request = CreateCompileRequest(waitForDomainReload: true);

            bool shouldClear = CompileUseCase.ShouldClearPendingCompileRequestAfterCancellation(
                request,
                isCancellationRequested: true,
                isDomainReloadInProgress: false);

            Assert.That(shouldClear, Is.True);
        }

        [Test]
        public void ShouldClearPendingCompileRequestAfterCancellation_WhenDomainReloadCancels_ReturnsFalse()
        {
            // Verifies Domain Reload cancellation keeps pending recovery available after reload.
            UnityCliLoopCompileRequest request = CreateCompileRequest(waitForDomainReload: true);

            bool shouldClear = CompileUseCase.ShouldClearPendingCompileRequestAfterCancellation(
                request,
                isCancellationRequested: true,
                isDomainReloadInProgress: true);

            Assert.That(shouldClear, Is.False);
        }

        [Test]
        public void ShouldClearPendingCompileRequestAfterCancellation_WhenNoReloadWait_ReturnsFalse()
        {
            // Verifies fire-and-forget compile requests do not touch pending reload recovery state.
            UnityCliLoopCompileRequest request = CreateCompileRequest(waitForDomainReload: false);

            bool shouldClear = CompileUseCase.ShouldClearPendingCompileRequestAfterCancellation(
                request,
                isCancellationRequested: true,
                isDomainReloadInProgress: false);

            Assert.That(shouldClear, Is.False);
        }

        [Test]
        public void ShouldClearPendingCompileRequestAfterInterruptedCompile_WhenCompileFailsBeforeReload_ReturnsTrue()
        {
            // Verifies non-cancellation failures clear pending recovery before it can become stale.
            UnityCliLoopCompileRequest request = CreateCompileRequest(waitForDomainReload: true);

            bool shouldClear = CompileUseCase.ShouldClearPendingCompileRequestAfterInterruptedCompile(
                request,
                resultPersistenceCompleted: false,
                isCancellationRequested: false,
                isDomainReloadInProgress: false);

            Assert.That(shouldClear, Is.True);
        }

        [Test]
        public void ShouldClearPendingCompileRequestAfterInterruptedCompile_WhenResultPersistenceCompleted_ReturnsFalse()
        {
            // Verifies successful result persistence owns pending recovery cleanup.
            UnityCliLoopCompileRequest request = CreateCompileRequest(waitForDomainReload: true);

            bool shouldClear = CompileUseCase.ShouldClearPendingCompileRequestAfterInterruptedCompile(
                request,
                resultPersistenceCompleted: true,
                isCancellationRequested: false,
                isDomainReloadInProgress: false);

            Assert.That(shouldClear, Is.False);
        }

        [Test]
        public void ShouldClearPendingCompileRequestAfterInterruptedCompile_WhenDomainReloadStarted_ReturnsFalse()
        {
            // Verifies Domain Reload recovery keeps ownership once Unity has begun reloading scripts.
            UnityCliLoopCompileRequest request = CreateCompileRequest(waitForDomainReload: true);

            bool shouldClear = CompileUseCase.ShouldClearPendingCompileRequestAfterInterruptedCompile(
                request,
                resultPersistenceCompleted: false,
                isCancellationRequested: false,
                isDomainReloadInProgress: true);

            Assert.That(shouldClear, Is.False);
        }

        [Test]
        public void ShouldRecoverWhileEditorCompiling_WhenElapsedTimeIsBelowLimit_ReturnsFalse()
        {
            // Verifies recovery polling uses real elapsed time instead of editor frame count.
            DateTime startedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            DateTime utcNow = startedAtUtc.AddMilliseconds(4999);

            bool shouldRecover =
                CompileDomainReloadRecoveryStartup.ShouldRecoverWhileEditorCompiling(startedAtUtc, utcNow);

            Assert.That(shouldRecover, Is.False);
        }

        [Test]
        public void ShouldRecoverWhileEditorCompiling_WhenElapsedTimeReachesLimit_ReturnsTrue()
        {
            // Verifies recovery can synthesize an indeterminate result after the real timeout elapses.
            DateTime startedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            DateTime utcNow = startedAtUtc.AddMilliseconds(5000);

            bool shouldRecover =
                CompileDomainReloadRecoveryStartup.ShouldRecoverWhileEditorCompiling(startedAtUtc, utcNow);

            Assert.That(shouldRecover, Is.True);
        }

        private static UnityCliLoopCompileRequest CreateCompileRequest(bool waitForDomainReload)
        {
            return new UnityCliLoopCompileRequest
            {
                ForceRecompile = false,
                WaitForDomainReload = waitForDomainReload,
                RequestId = "compile_test_request"
            };
        }
    }
}
