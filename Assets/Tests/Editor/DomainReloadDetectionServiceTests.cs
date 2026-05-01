using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
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
            _originalIsServerRunning = McpEditorSettings.GetIsServerRunning();
            _originalIsAfterCompile = McpEditorSettings.GetIsAfterCompile();
            _originalIsDomainReloadInProgress = McpEditorSettings.GetIsDomainReloadInProgress();
            _originalIsReconnecting = McpEditorSettings.GetIsReconnecting();
            _originalShowReconnectingUI = McpEditorSettings.GetShowReconnectingUI();
            _originalShowPostCompileReconnectingUI = McpEditorSettings.GetShowPostCompileReconnectingUI();
            McpEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            DomainReloadDetectionService.DeleteLockFile();
        }

        [TearDown]
        public void TearDown()
        {
            McpEditorSettings.SetIsServerRunning(_originalIsServerRunning);
            McpEditorSettings.SetIsAfterCompile(_originalIsAfterCompile);
            McpEditorSettings.SetIsDomainReloadInProgress(_originalIsDomainReloadInProgress);
            McpEditorSettings.SetIsReconnecting(_originalIsReconnecting);
            McpEditorSettings.SetShowReconnectingUI(_originalShowReconnectingUI);
            McpEditorSettings.SetShowPostCompileReconnectingUI(_originalShowPostCompileReconnectingUI);
            McpEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            DomainReloadDetectionService.DeleteLockFile();
        }

        [Test]
        public void RollbackDomainReloadStart_ClearsTemporaryFlagsProviderStateAndLockFile()
        {
            const string correlationId = "test-correlation";
            McpEditorDomainReloadStateProvider provider = new McpEditorDomainReloadStateProvider();

            DomainReloadDetectionService.StartDomainReload(correlationId, true);

            Assert.That(McpEditorSettings.GetIsServerRunning(), Is.True);
            Assert.That(McpEditorSettings.GetIsAfterCompile(), Is.True);
            Assert.That(McpEditorSettings.GetIsDomainReloadInProgress(), Is.True);
            Assert.That(McpEditorSettings.GetIsReconnecting(), Is.True);
            Assert.That(McpEditorSettings.GetShowReconnectingUI(), Is.True);
            Assert.That(McpEditorSettings.GetShowPostCompileReconnectingUI(), Is.True);
            Assert.That(provider.IsDomainReloadInProgress(), Is.True);
            Assert.That(DomainReloadDetectionService.IsLockFilePresent(), Is.True);

            DomainReloadDetectionService.RollbackDomainReloadStart(correlationId);

            Assert.That(McpEditorSettings.GetIsServerRunning(), Is.True);
            Assert.That(McpEditorSettings.GetIsAfterCompile(), Is.False);
            Assert.That(McpEditorSettings.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(McpEditorSettings.GetIsReconnecting(), Is.False);
            Assert.That(McpEditorSettings.GetShowReconnectingUI(), Is.False);
            Assert.That(McpEditorSettings.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(provider.IsDomainReloadInProgress(), Is.False);
            Assert.That(DomainReloadDetectionService.IsLockFilePresent(), Is.False);
        }
    }
}
