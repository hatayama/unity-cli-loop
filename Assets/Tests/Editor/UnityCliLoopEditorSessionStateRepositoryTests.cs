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
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
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
        public void GetPendingCompileRequest_WhenServiceIsRecreated_ReadsExistingSessionValue()
        {
            // Verifies pending compile recovery data survives Domain Reload service recreation.
            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: true);

            UnityCliLoopEditorSessionStateService recreatedService =
                UnityCliLoopEditorSessionStateTestFactory.CreateService();

            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                recreatedService.GetPendingCompileRequest();
            Assert.That(pendingCompileRequest.HasRequest, Is.True);
            Assert.That(pendingCompileRequest.RequestId, Is.EqualTo("compile_test_request"));
            Assert.That(pendingCompileRequest.ForceRecompile, Is.True);
            Assert.That(pendingCompileRequest.ExpiresAtUtcTicks, Is.GreaterThan(DateTime.UtcNow.Ticks));
        }

        [Test]
        public void ClearExpiredPendingCompileRequest_WhenRequestIsExpired_ClearsSessionValue()
        {
            // Verifies stale compile recovery data cannot survive indefinitely across commands.
            DateTime now = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            _sessionStateService.MarkPendingCompileRequestWithExpiration(
                "compile_test_request",
                forceRecompile: false,
                expiresAtUtcTicks: now.AddSeconds(-1).Ticks);

            bool cleared = _sessionStateService.ClearExpiredPendingCompileRequest(now);

            Assert.That(cleared, Is.True);
            Assert.That(_sessionStateService.GetPendingCompileRequest().HasRequest, Is.False);
        }

        [Test]
        public void ClearExpiredPendingCompileRequest_WhenRequestIsFresh_KeepsSessionValue()
        {
            // Verifies active compile recovery data is not cleared before its deadline.
            DateTime now = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc);
            _sessionStateService.MarkPendingCompileRequestWithExpiration(
                "compile_test_request",
                forceRecompile: false,
                expiresAtUtcTicks: now.AddSeconds(1).Ticks);

            bool cleared = _sessionStateService.ClearExpiredPendingCompileRequest(now);

            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            Assert.That(cleared, Is.False);
            Assert.That(pendingCompileRequest.HasRequest, Is.True);
            Assert.That(pendingCompileRequest.RequestId, Is.EqualTo("compile_test_request"));
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
            _sessionStateService.MarkPendingCompileRequest("compile_test_request", forceRecompile: false);

            _sessionStateService.ClearAll();

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.False);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.False);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
            Assert.That(_sessionStateService.GetIsServerManuallyStopped(), Is.False);
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
