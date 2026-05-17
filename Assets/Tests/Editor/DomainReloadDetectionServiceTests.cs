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
        private UnityCliLoopEditorSettingsData _originalSettings;
        private UnityCliLoopEditorSettingsService _editorSettingsService;
        private IDomainReloadDetectionService _domainReloadDetectionService;
        private ServerReadinessStateStore _stateStore;

        [SetUp]
        public void SetUp()
        {
            _editorSettingsService = UnityCliLoopEditorSettingsTestFactory.CreateService();
            _originalSettings = CloneSettings(_editorSettingsService.GetSettings());
            _stateStore = CreateTestStateStore();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(_editorSettingsService, _stateStore);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
        }

        [TearDown]
        public void TearDown()
        {
            _editorSettingsService.SaveSettings(_originalSettings);
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

            UnityCliLoopEditorSettingsData startedSettings = _editorSettingsService.GetSettings();
            Assert.That(startedSettings.isServerRunning, Is.True);
            Assert.That(startedSettings.isAfterCompile, Is.True);
            Assert.That(startedSettings.isDomainReloadInProgress, Is.True);
            Assert.That(startedSettings.isReconnecting, Is.True);
            Assert.That(startedSettings.showReconnectingUI, Is.True);
            Assert.That(startedSettings.showPostCompileReconnectingUI, Is.True);
            Assert.That(provider.IsDomainReloadInProgress(), Is.True);

            _domainReloadDetectionService.RollbackDomainReloadStart(correlationId);

            UnityCliLoopEditorSettingsData rolledBackSettings = _editorSettingsService.GetSettings();
            Assert.That(rolledBackSettings.isServerRunning, Is.True);
            Assert.That(rolledBackSettings.isAfterCompile, Is.False);
            Assert.That(rolledBackSettings.isDomainReloadInProgress, Is.False);
            Assert.That(rolledBackSettings.isReconnecting, Is.False);
            Assert.That(rolledBackSettings.showReconnectingUI, Is.False);
            Assert.That(rolledBackSettings.showPostCompileReconnectingUI, Is.False);
            Assert.That(provider.IsDomainReloadInProgress(), Is.False);
            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("failed"));
            Assert.That(state.LastError, Is.Not.Empty);
        }

        private static UnityCliLoopEditorSettingsData CloneSettings(UnityCliLoopEditorSettingsData settings)
        {
            string json = UnityEngine.JsonUtility.ToJson(settings);
            return UnityEngine.JsonUtility.FromJson<UnityCliLoopEditorSettingsData>(json);
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
