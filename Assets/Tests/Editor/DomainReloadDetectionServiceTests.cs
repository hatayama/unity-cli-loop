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
        private IDomainReloadDetectionService _domainReloadDetectionService;

        [SetUp]
        public void SetUp()
        {
            _originalSettings = CloneSettings(UnityCliLoopEditorSettings.GetSettings());
            _domainReloadDetectionService = new DomainReloadDetectionFileService();
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            _domainReloadDetectionService.DeleteLockFile();
        }

        [TearDown]
        public void TearDown()
        {
            UnityCliLoopEditorSettings.SaveSettings(_originalSettings);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            _domainReloadDetectionService.DeleteLockFile();
        }

        [Test]
        public void RollbackDomainReloadStart_ClearsTemporaryFlagsProviderStateAndLockFile()
        {
            const string correlationId = "test-correlation";
            UnityCliLoopEditorDomainReloadStateProvider provider = new();

            _domainReloadDetectionService.StartDomainReload(correlationId, true);

            UnityCliLoopEditorSettingsData startedSettings = UnityCliLoopEditorSettings.GetSettings();
            Assert.That(startedSettings.isServerRunning, Is.True);
            Assert.That(startedSettings.isAfterCompile, Is.True);
            Assert.That(startedSettings.isDomainReloadInProgress, Is.True);
            Assert.That(startedSettings.isReconnecting, Is.True);
            Assert.That(startedSettings.showReconnectingUI, Is.True);
            Assert.That(startedSettings.showPostCompileReconnectingUI, Is.True);
            Assert.That(provider.IsDomainReloadInProgress(), Is.True);
            Assert.That(_domainReloadDetectionService.IsLockFilePresent(), Is.True);

            _domainReloadDetectionService.RollbackDomainReloadStart(correlationId);

            UnityCliLoopEditorSettingsData rolledBackSettings = UnityCliLoopEditorSettings.GetSettings();
            Assert.That(rolledBackSettings.isServerRunning, Is.True);
            Assert.That(rolledBackSettings.isAfterCompile, Is.False);
            Assert.That(rolledBackSettings.isDomainReloadInProgress, Is.False);
            Assert.That(rolledBackSettings.isReconnecting, Is.False);
            Assert.That(rolledBackSettings.showReconnectingUI, Is.False);
            Assert.That(rolledBackSettings.showPostCompileReconnectingUI, Is.False);
            Assert.That(provider.IsDomainReloadInProgress(), Is.False);
            Assert.That(_domainReloadDetectionService.IsLockFilePresent(), Is.False);
        }

        private static UnityCliLoopEditorSettingsData CloneSettings(UnityCliLoopEditorSettingsData settings)
        {
            string json = UnityEngine.JsonUtility.ToJson(settings);
            return UnityEngine.JsonUtility.FromJson<UnityCliLoopEditorSettingsData>(json);
        }
    }
}
