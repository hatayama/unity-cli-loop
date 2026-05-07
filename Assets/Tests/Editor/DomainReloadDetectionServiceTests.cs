using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Domain Reload Detection Service behavior.
    /// </summary>
    public class DomainReloadDetectionServiceTests
    {
        private bool _originalIsServerRunning;
        private bool _originalIsAfterCompile;
        private bool _originalIsDomainReloadInProgress;
        private bool _originalIsReconnecting;
        private bool _originalShowReconnectingUI;
        private bool _originalShowPostCompileReconnectingUI;

        [SetUp]
        public void SetUp()
        {
            _originalIsServerRunning = UnityCliLoopEditorSettings.GetIsServerRunning();
            _originalIsAfterCompile = UnityCliLoopEditorSettings.GetIsAfterCompile();
            _originalIsDomainReloadInProgress = UnityCliLoopEditorSettings.GetIsDomainReloadInProgress();
            _originalIsReconnecting = UnityCliLoopEditorSettings.GetIsReconnecting();
            _originalShowReconnectingUI = UnityCliLoopEditorSettings.GetShowReconnectingUI();
            _originalShowPostCompileReconnectingUI = UnityCliLoopEditorSettings.GetShowPostCompileReconnectingUI();
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            DomainReloadDetectionService.DeleteLockFile();
        }

        [TearDown]
        public void TearDown()
        {
            UnityCliLoopEditorSettings.SetIsServerRunning(_originalIsServerRunning);
            UnityCliLoopEditorSettings.SetIsAfterCompile(_originalIsAfterCompile);
            UnityCliLoopEditorSettings.SetIsDomainReloadInProgress(_originalIsDomainReloadInProgress);
            UnityCliLoopEditorSettings.SetIsReconnecting(_originalIsReconnecting);
            UnityCliLoopEditorSettings.SetShowReconnectingUI(_originalShowReconnectingUI);
            UnityCliLoopEditorSettings.SetShowPostCompileReconnectingUI(_originalShowPostCompileReconnectingUI);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            DomainReloadDetectionService.DeleteLockFile();
        }

        [Test]
        public void RollbackDomainReloadStart_ClearsTemporaryFlagsProviderStateAndLockFile()
        {
            const string correlationId = "test-correlation";
            UnityCliLoopEditorDomainReloadStateProvider provider = new();

            DomainReloadDetectionService.StartDomainReload(correlationId, true);

            Assert.That(UnityCliLoopEditorSettings.GetIsServerRunning(), Is.True);
            Assert.That(UnityCliLoopEditorSettings.GetIsAfterCompile(), Is.True);
            Assert.That(UnityCliLoopEditorSettings.GetIsDomainReloadInProgress(), Is.True);
            Assert.That(UnityCliLoopEditorSettings.GetIsReconnecting(), Is.True);
            Assert.That(UnityCliLoopEditorSettings.GetShowReconnectingUI(), Is.True);
            Assert.That(UnityCliLoopEditorSettings.GetShowPostCompileReconnectingUI(), Is.True);
            Assert.That(provider.IsDomainReloadInProgress(), Is.True);
            Assert.That(DomainReloadDetectionService.IsLockFilePresent(), Is.True);

            DomainReloadDetectionService.RollbackDomainReloadStart(correlationId);

            Assert.That(UnityCliLoopEditorSettings.GetIsServerRunning(), Is.True);
            Assert.That(UnityCliLoopEditorSettings.GetIsAfterCompile(), Is.False);
            Assert.That(UnityCliLoopEditorSettings.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(UnityCliLoopEditorSettings.GetIsReconnecting(), Is.False);
            Assert.That(UnityCliLoopEditorSettings.GetShowReconnectingUI(), Is.False);
            Assert.That(UnityCliLoopEditorSettings.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(provider.IsDomainReloadInProgress(), Is.False);
            Assert.That(DomainReloadDetectionService.IsLockFilePresent(), Is.False);
        }
    }
}
