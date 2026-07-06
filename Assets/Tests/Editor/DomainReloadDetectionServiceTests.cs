using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Domain Reload Detection Service behavior.
    /// </summary>
    public class DomainReloadDetectionServiceTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.SETTINGS_FILE_NAME);

        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopEditorSessionStateService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;
        private IDomainReloadDetectionService _domainReloadDetectionService;
        private bool _settingsFileExisted;
        private string _settingsFileContent;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;
            if (!Directory.Exists(UnityCliLoopConstants.USER_SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(UnityCliLoopConstants.USER_SETTINGS_FOLDER);
            }

            DeleteIfExists(SettingsFilePath);
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore(_sessionStateService);
            UnityCliLoopEditorDomainReloadStateProvider.SetDomainReloadInProgressFromMainThread(false);
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
        }

        [Test]
        public void RollbackDomainReloadStart_ClearsTemporaryFlagsAndProviderState()
        {
            // Verifies rollback clears transient reload state.
            const string correlationId = "test-correlation";
            UnityCliLoopEditorDomainReloadStateProvider provider = new();

            _domainReloadDetectionService.StartDomainReload(correlationId, true);

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsAfterCompile(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsDomainReloadInProgress(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsReconnecting(), Is.True);
            Assert.That(_sessionFlagsRepository.GetShowReconnectingUI(), Is.True);
            Assert.That(_sessionFlagsRepository.GetShowPostCompileReconnectingUI(), Is.True);
            Assert.That(provider.IsDomainReloadInProgress(), Is.True);

            _domainReloadDetectionService.RollbackDomainReloadStart(correlationId);

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsReconnecting(), Is.False);
            Assert.That(_sessionFlagsRepository.GetShowReconnectingUI(), Is.False);
            Assert.That(_sessionFlagsRepository.GetShowPostCompileReconnectingUI(), Is.False);
            Assert.That(provider.IsDomainReloadInProgress(), Is.False);
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
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService,
                new TestLegacySessionStateReader(legacySessionState));

            _domainReloadDetectionService.CompleteDomainReload("test-correlation");

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsAfterCompile(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsDomainReloadInProgress(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsReconnecting(), Is.True);
            Assert.That(_sessionFlagsRepository.GetShowReconnectingUI(), Is.True);
            Assert.That(_sessionFlagsRepository.GetShowPostCompileReconnectingUI(), Is.True);
        }

        [Test]
        public void CompleteDomainReload_WhenLegacyStateOnlySaysRunning_DoesNotRestoreRunningSession()
        {
            // Verifies that stale running-only JSON is not restored into SessionState.
            UnityCliLoopEditorLegacySessionState legacySessionState = new(
                isServerRunning: true,
                isAfterCompile: false,
                isDomainReloadInProgress: false,
                isReconnecting: false,
                showReconnectingUI: false,
                showPostCompileReconnectingUI: false);
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService,
                new TestLegacySessionStateReader(legacySessionState));

            _domainReloadDetectionService.CompleteDomainReload("test-correlation");

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
        }

        [Test]
        public void CompleteDomainReload_WhenLegacyReloadStateWasMigrated_DoesNotReapplyLegacyJson()
        {
            // Verifies that legacy JSON recovery state is consumed after the first migration reload.
            File.WriteAllText(
                SettingsFilePath,
                "{" +
                "\"isServerRunning\":true," +
                "\"isAfterCompile\":true," +
                "\"isDomainReloadInProgress\":true," +
                "\"isReconnecting\":true," +
                "\"showReconnectingUI\":true," +
                "\"showPostCompileReconnectingUI\":true" +
                "}");
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService,
                new UnityCliLoopEditorLegacySessionStateReader());

            _domainReloadDetectionService.CompleteDomainReload("first-correlation");
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();

            _domainReloadDetectionService.CompleteDomainReload("second-correlation");

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsAfterCompile(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsReconnecting(), Is.False);
        }

        private static void RestoreFile(string path, bool existed, string content)
        {
            if (existed)
            {
                File.WriteAllText(path, content);
                return;
            }

            DeleteIfExists(path);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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

            public void Clear()
            {
            }
        }
    }
}
