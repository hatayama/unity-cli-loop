using NUnit.Framework;
using System;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Editor SessionState behavior.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateRepositoryTests
    {
        private UnityCliLoopEditorSessionStateSnapshot _originalSnapshot;
        private UnityCliLoopEditorSessionStateService _sessionStateService;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSnapshot = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSnapshot.Restore(_sessionStateService);
        }

        [Test]
        public void GetFlags_WhenSessionStateIsEmpty_ReturnsFalseDefaults()
        {
            // Verifies that transient runtime flags do not opt into stale recovery by default.
            Assert.That(_sessionStateService.GetIsServerRunning(), Is.False);
            Assert.That(_sessionStateService.GetIsServerManuallyStopped(), Is.False);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.False);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
        }

        [Test]
        public void GetFlags_WhenServiceAndRepositoryAreRecreated_ReadsExistingSessionValues()
        {
            // Verifies that SessionState survives service/repository recreation within the same Editor session.
            _sessionStateService.MarkDomainReloadStarted(serverIsRunning: true);

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            Assert.That(recreatedService.GetIsServerRunning(), Is.True);
            Assert.That(recreatedService.GetIsServerManuallyStopped(), Is.False);
            Assert.That(recreatedService.GetIsAfterCompile(), Is.True);
            Assert.That(recreatedService.GetIsDomainReloadInProgress(), Is.True);
            Assert.That(recreatedService.GetIsReconnecting(), Is.True);
            Assert.That(recreatedService.GetShowReconnectingUI(), Is.True);
            Assert.That(recreatedService.GetShowPostCompileReconnectingUI(), Is.True);
        }

        [Test]
        public void GetShouldAutoScanThirdPartyToolMigration_WhenServiceIsRecreated_ReadsExistingSessionValue()
        {
            // Verifies that the one-session migration scan request survives Domain Reload service recreation.
            _sessionStateService.SetShouldAutoScanThirdPartyToolMigration(true);

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            Assert.That(recreatedService.GetShouldAutoScanThirdPartyToolMigration(), Is.True);
        }

        [Test]
        public void GetCompileResult_WhenRequestIdMatches_ReturnsStoredJson()
        {
            // Verifies compile results survive service recreation and are keyed by request id.
            DateTime completedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            _sessionStateService.StoreCompileResult(
                "compile_test_request",
                forceRecompile: true,
                resultJson: "{\"Success\":null}",
                completedAtUtc: completedAtUtc);

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();
            UnityCliLoopStoredCompileResult storedResult =
                recreatedService.GetCompileResult("compile_test_request");

            Assert.That(storedResult.HasResult, Is.True);
            Assert.That(storedResult.RequestId, Is.EqualTo("compile_test_request"));
            Assert.That(storedResult.ForceRecompile, Is.True);
            Assert.That(storedResult.ResultJson, Is.EqualTo("{\"Success\":null}"));
            Assert.That(storedResult.CompletedAtUtcTicks, Is.EqualTo(completedAtUtc.Ticks));
        }

        [Test]
        public void GetCompileResult_WhenRequestIdDiffers_ReturnsEmptyResult()
        {
            // Verifies stale results from older compile commands are never returned to a new request.
            _sessionStateService.StoreCompileResult(
                "compile_old_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: DateTime.UtcNow);

            UnityCliLoopStoredCompileResult storedResult =
                _sessionStateService.GetCompileResult("compile_new_request");

            Assert.That(storedResult.HasResult, Is.False);
        }

        [Test]
        public void GetCompileResult_WhenAnotherRequestStoresLater_ReturnsOriginalResult()
        {
            // Verifies compile results are keyed by request id instead of a single global slot.
            _sessionStateService.StoreCompileResult(
                "compile_first_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: DateTime.UtcNow);
            _sessionStateService.StoreCompileResult(
                "compile_second_request",
                forceRecompile: true,
                resultJson: "{\"Success\":null}",
                completedAtUtc: DateTime.UtcNow);

            UnityCliLoopStoredCompileResult firstResult =
                _sessionStateService.GetCompileResult("compile_first_request");
            UnityCliLoopStoredCompileResult secondResult =
                _sessionStateService.GetCompileResult("compile_second_request");

            Assert.That(firstResult.HasResult, Is.True);
            Assert.That(firstResult.ResultJson, Is.EqualTo("{\"Success\":true}"));
            Assert.That(secondResult.HasResult, Is.True);
            Assert.That(secondResult.ResultJson, Is.EqualTo("{\"Success\":null}"));
        }

        [Test]
        public void GetCompileResult_WhenLegacySingleSlotExists_ReturnsLegacyResult()
        {
            // Verifies an in-flight compile that started on the old storage key can finish after this assembly reloads.
            DateTime completedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultRequestId("compile_legacy_request");
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultForceRecompile(false);
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultJson("{\"Success\":true}");
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultCompletedAtUtcTicks(
                completedAtUtc.Ticks.ToString());
            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            UnityCliLoopStoredCompileResult storedResult =
                recreatedService.GetCompileResult("compile_legacy_request");

            Assert.That(storedResult.HasResult, Is.True);
            Assert.That(storedResult.ResultJson, Is.EqualTo("{\"Success\":true}"));
            Assert.That(UnityCliLoopCompileResultSessionRepository.GetLegacyCompileResultRequestId(), Is.Empty);
        }

        [Test]
        public void GetPendingCompileRequest_WhenServiceIsRecreated_ReturnsPendingRequest()
        {
            // Verifies pending compile recovery state survives Domain Reload service recreation.
            DateTime markedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: true,
                markedAtUtc: markedAtUtc);

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();
            UnityCliLoopPendingCompileRequest pendingRequest =
                recreatedService.GetPendingCompileRequest();

            Assert.That(pendingRequest.HasRequest, Is.True);
            Assert.That(pendingRequest.RequestId, Is.EqualTo("compile_test_request"));
            Assert.That(pendingRequest.ForceRecompile, Is.True);
            Assert.That(pendingRequest.ExpiresAtUtcTicks, Is.GreaterThan(markedAtUtc.Ticks));
            Assert.That(pendingRequest.ReloadObserved, Is.False);
        }

        [Test]
        public void GetPendingCompileRequestForRequestId_WhenAnotherRequestStoresLater_ReturnsOriginalRequest()
        {
            // Verifies pending compile requests are keyed by request id instead of a single global slot.
            DateTime markedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            _sessionStateService.MarkPendingCompileRequest(
                "compile_first_request",
                forceRecompile: false,
                markedAtUtc: markedAtUtc);
            _sessionStateService.MarkPendingCompileRequest(
                "compile_second_request",
                forceRecompile: true,
                markedAtUtc: markedAtUtc);

            UnityCliLoopPendingCompileRequest firstRequest =
                _sessionStateService.GetPendingCompileRequestForRequestId("compile_first_request");
            UnityCliLoopPendingCompileRequest secondRequest =
                _sessionStateService.GetPendingCompileRequestForRequestId("compile_second_request");

            Assert.That(firstRequest.HasRequest, Is.True);
            Assert.That(firstRequest.ForceRecompile, Is.False);
            Assert.That(secondRequest.HasRequest, Is.True);
            Assert.That(secondRequest.ForceRecompile, Is.True);
        }

        [Test]
        public void GetPendingCompileRequestForRequestId_WhenLegacySingleSlotExists_ReturnsLegacyRequest()
        {
            // Verifies pending compile recovery can see a request marked before this assembly reloads.
            UnityCliLoopEditorSessionStateRepository repository = new UnityCliLoopEditorSessionStateRepository();
            DateTime expiresAtUtc = new DateTime(2026, 5, 30, 0, 32, 0, DateTimeKind.Utc);
            repository.SetLegacyPendingCompileRequestId("compile_legacy_request");
            repository.SetLegacyPendingCompileForceRecompile(true);
            repository.SetLegacyPendingCompileExpiresAtUtcTicks(expiresAtUtc.Ticks.ToString());
            repository.SetLegacyPendingCompileReloadObserved(true);
            UnityCliLoopEditorSessionStateService recreatedService =
                new UnityCliLoopEditorSessionStateService(
                    repository,
                    new UnityCliLoopCompileResultSessionRepository());

            UnityCliLoopPendingCompileRequest pendingRequest =
                recreatedService.GetPendingCompileRequestForRequestId("compile_legacy_request");

            Assert.That(pendingRequest.HasRequest, Is.True);
            Assert.That(pendingRequest.ForceRecompile, Is.True);
            Assert.That(pendingRequest.ReloadObserved, Is.True);
            Assert.That(repository.GetLegacyPendingCompileRequestId(), Is.Empty);
        }

        [Test]
        public void MarkDomainReloadStarted_WhenPendingCompileRequestExists_MarksReloadObserved()
        {
            // Verifies pending compile recovery only becomes eligible after Unity starts Domain Reload.
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: DateTime.UtcNow);

            _sessionStateService.MarkDomainReloadStarted(serverIsRunning: true);

            UnityCliLoopPendingCompileRequest pendingRequest =
                _sessionStateService.GetPendingCompileRequest();
            Assert.That(pendingRequest.HasRequest, Is.True);
            Assert.That(pendingRequest.ReloadObserved, Is.True);
        }

        [Test]
        public void ClearExpiredPendingCompileRequest_WhenPendingRequestIsStale_ClearsSessionValue()
        {
            // Verifies stale pending compile recovery state cannot satisfy later CLI polling.
            DateTime markedAtUtc = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            DateTime now = new DateTime(2026, 5, 30, 0, 32, 1, DateTimeKind.Utc);
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: markedAtUtc);

            bool cleared = _sessionStateService.ClearExpiredPendingCompileRequest(now);

            Assert.That(cleared, Is.True);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void ClearPendingCompileRequestIfMatches_WhenRequestIdMatches_ClearsSessionValue()
        {
            // Verifies stored compile results can clear their matching pending recovery state.
            _sessionStateService.MarkPendingCompileRequest(
                "compile_test_request",
                forceRecompile: false,
                markedAtUtc: DateTime.UtcNow);

            bool cleared = _sessionStateService.ClearPendingCompileRequestIfMatches("compile_test_request");

            Assert.That(cleared, Is.True);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void GetStoredCompileResult_WhenCompletedTicksAreMalformed_ClearsSessionValue()
        {
            // Verifies malformed stored compile results self-heal instead of breaking status polling.
            UnityCliLoopCompileResultSessionRepository.SetCompileResultRequestIds("compile_test_request");
            UnityCliLoopCompileResultSessionRepository.SetCompileResultForceRecompile("compile_test_request", false);
            UnityCliLoopCompileResultSessionRepository.SetCompileResultJson("compile_test_request", "{\"Success\":true}");
            UnityCliLoopCompileResultSessionRepository.SetCompileResultCompletedAtUtcTicks(
                "compile_test_request",
                "not_ticks");
            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            UnityCliLoopStoredCompileResult storedResult =
                recreatedService.GetStoredCompileResult();

            Assert.That(storedResult.HasResult, Is.False);
            Assert.That(recreatedService.GetStoredCompileResult().HasResult, Is.False);
        }

        [Test]
        public void GetCompileResult_WhenLegacyCompletedTicksAreOutOfRange_ClearsLegacySessionValue()
        {
            // Verifies legacy compile result migration rejects ticks that cannot become a UTC DateTime.
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultRequestId("compile_legacy_request");
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultForceRecompile(false);
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultJson("{\"Success\":true}");
            UnityCliLoopCompileResultSessionRepository.SetLegacyCompileResultCompletedAtUtcTicks(long.MaxValue.ToString());
            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            UnityCliLoopStoredCompileResult storedResult =
                recreatedService.GetCompileResult("compile_legacy_request");

            Assert.That(storedResult.HasResult, Is.False);
            Assert.That(UnityCliLoopCompileResultSessionRepository.GetLegacyCompileResultRequestId(), Is.Empty);
        }

        [Test]
        public void ClearExpiredCompileResult_WhenResultIsStale_ClearsSessionValue()
        {
            // Verifies stale compile results do not survive indefinitely across commands.
            DateTime now = new DateTime(2026, 5, 30, 0, 32, 1, DateTimeKind.Utc);
            _sessionStateService.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc));

            bool cleared = _sessionStateService.ClearExpiredCompileResult(now);

            Assert.That(cleared, Is.True);
            Assert.That(_sessionStateService.GetStoredCompileResult().HasResult, Is.False);
        }

        [Test]
        public void ConsumeShouldAutoScanThirdPartyToolMigration_WhenFlagIsSet_ReturnsTrueOnce()
        {
            // Verifies that the startup migration scan request is consumed exactly once.
            _sessionStateService.SetShouldAutoScanThirdPartyToolMigration(true);

            bool firstConsume = _sessionStateService.ConsumeShouldAutoScanThirdPartyToolMigration();
            bool secondConsume = _sessionStateService.ConsumeShouldAutoScanThirdPartyToolMigration();

            Assert.That(firstConsume, Is.True);
            Assert.That(secondConsume, Is.False);
            Assert.That(_sessionStateService.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
        }

        [Test]
        public void ClearAll_WhenFlagsAreSet_ClearsEveryTransientFlag()
        {
            // Verifies that test and shutdown cleanup can reset all runtime SessionState flags together.
            _sessionStateService.MarkDomainReloadStarted(serverIsRunning: true);
            _sessionStateService.SetShouldAutoScanThirdPartyToolMigration(true);
            _sessionStateService.SetIsServerManuallyStopped(true);
            _sessionStateService.StoreCompileResult(
                "compile_test_request",
                forceRecompile: false,
                resultJson: "{\"Success\":true}",
                completedAtUtc: DateTime.UtcNow);

            _sessionStateService.ClearAll();

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.False);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.False);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
            Assert.That(_sessionStateService.GetIsServerManuallyStopped(), Is.False);
            Assert.That(_sessionStateService.GetStoredCompileResult().HasResult, Is.False);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void MarkServerManuallyStopped_WhenServiceIsRecreated_PreservesManualStop()
        {
            // Verifies that explicit Stop Server survives Domain Reload service recreation.
            _sessionStateService.MarkServerManuallyStopped();

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            Assert.That(recreatedService.GetIsServerRunning(), Is.False);
            Assert.That(recreatedService.GetIsServerManuallyStopped(), Is.True);
        }
    }
}
