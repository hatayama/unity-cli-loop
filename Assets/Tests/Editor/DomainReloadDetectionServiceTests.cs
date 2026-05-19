using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Domain Reload Detection Service behavior.
    /// </summary>
    public class DomainReloadDetectionServiceTests
    {
        private UnityCliLoopEditorSessionStateService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;
        private IDomainReloadDetectionService _domainReloadDetectionService;
        private ServerReadinessStateStore _stateStore;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
            _stateStore = CreateTestStateStore();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(_sessionStateService, _stateStore);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore(_sessionStateService);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            _stateStore.Delete();
        }

        [Test]
        public void RollbackDomainReloadStart_ClearsTemporaryFlagsProviderStateAndPublishesFailureState()
        {
            // Verifies rollback clears transient reload state and records a failed readiness phase.
            const string correlationId = "test-correlation";
            UnityCliLoopEditorDomainReloadStateProvider provider = new();

            _domainReloadDetectionService.StartDomainReload(correlationId, true);

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.True);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.True);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.True);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.True);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.True);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.True);
            Assert.That(provider.IsDomainReloadInProgress(), Is.True);

            _domainReloadDetectionService.RollbackDomainReloadStart(correlationId);

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.True);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.False);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(provider.IsDomainReloadInProgress(), Is.False);
            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("failed"));
            Assert.That(state.LastError, Is.Not.Empty);
        }

        [Test]
        public void CompleteDomainReload_WhenLegacyReloadStateExists_MigratesRecoveryFlagsToSessionState()
        {
            // Verifies that the first reload after migration preserves old JSON recovery state.
            UnityCliLoopEditorLegacySessionState legacySessionState = new(
                isServerRunning: true,
                isAfterCompile: true,
                isDomainReloadInProgress: true,
                isReconnecting: true,
                showReconnectingUI: true,
                showPostCompileReconnectingUI: true);
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionStateService,
                _stateStore,
                new TestLegacySessionStateReader(legacySessionState));

            _domainReloadDetectionService.CompleteDomainReload("test-correlation");

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.True);
            Assert.That(_sessionStateService.GetIsAfterCompile(), Is.True);
            Assert.That(_sessionStateService.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionStateService.GetIsReconnecting(), Is.True);
            Assert.That(_sessionStateService.GetShowReconnectingUI(), Is.True);
            Assert.That(_sessionStateService.GetShowPostCompileReconnectingUI(), Is.True);
            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("recovering"));
        }

        [Test]
        public void CompleteDomainReload_WhenLegacyStateOnlySaysRunning_IgnoresStaleRunningFlag()
        {
            // Verifies that stale running-only JSON does not opt into recovery after the migration.
            UnityCliLoopEditorLegacySessionState legacySessionState = new(
                isServerRunning: true,
                isAfterCompile: false,
                isDomainReloadInProgress: false,
                isReconnecting: false,
                showReconnectingUI: false,
                showPostCompileReconnectingUI: false);
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionStateService,
                _stateStore,
                new TestLegacySessionStateReader(legacySessionState));

            _domainReloadDetectionService.CompleteDomainReload("test-correlation");

            Assert.That(_sessionStateService.GetIsServerRunning(), Is.False);
            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("stopped"));
        }

        private static ServerReadinessStateStore CreateTestStateStore()
        {
            string projectRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unity-cli-loop-tests",
                System.Guid.NewGuid().ToString("N"));
            return new ServerReadinessStateStore(projectRoot);
        }

        private sealed class TestLegacySessionStateReader : IUnityCliLoopEditorLegacySessionStateReader
        {
            private readonly UnityCliLoopEditorLegacySessionState _legacySessionState;

            internal TestLegacySessionStateReader(UnityCliLoopEditorLegacySessionState legacySessionState)
            {
                _legacySessionState = legacySessionState;
            }

            public UnityCliLoopEditorLegacySessionState Read()
            {
                return _legacySessionState;
            }
        }
    }
}
