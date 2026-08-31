using System.IO;
using System.Security;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Editor Settings Recovery behavior.
    /// </summary>
    [TestFixture]
    public class UnityCliLoopEditorSettingsRecoveryTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.SETTINGS_FILE_NAME);
        private static readonly string LegacySettingsFilePath =
            Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.LEGACY_SETTINGS_FILE_NAME);
        private static readonly string BackupFilePath = SettingsFilePath + AtomicFileWriter.BackupFileSuffix;
        private static readonly string TempFilePath = SettingsFilePath + AtomicFileWriter.CompletedTempFileSuffix;

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private bool _legacySettingsFileExisted;
        private string _legacySettingsFileContent;
        private bool _backupFileExisted;
        private string _backupFileContent;
        private bool _tempFileExisted;
        private string _tempFileContent;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private UnityCliLoopEditorSettingsRepository _editorSettingsRepository;
        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            _legacySettingsFileExisted = File.Exists(LegacySettingsFilePath);
            _legacySettingsFileContent = _legacySettingsFileExisted ? File.ReadAllText(LegacySettingsFilePath) : null;

            _backupFileExisted = File.Exists(BackupFilePath);
            _backupFileContent = _backupFileExisted ? File.ReadAllText(BackupFilePath) : null;

            _tempFileExisted = File.Exists(TempFilePath);
            _tempFileContent = _tempFileExisted ? File.ReadAllText(TempFilePath) : null;

            if (!Directory.Exists(UnityCliLoopConstants.USER_SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(UnityCliLoopConstants.USER_SETTINGS_FOLDER);
            }

            DeleteIfExists(SettingsFilePath);
            DeleteIfExists(LegacySettingsFilePath);
            DeleteIfExists(BackupFilePath);
            DeleteIfExists(TempFilePath);
            _editorSettingsPort =
                UnityCliLoopEditorSettingsTestFactory.CreatePortWithRepository(out _editorSettingsRepository);
            _editorSettingsRepository.InvalidateCache();
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            RestoreFile(LegacySettingsFilePath, _legacySettingsFileExisted, _legacySettingsFileContent);
            RestoreFile(BackupFilePath, _backupFileExisted, _backupFileContent);
            RestoreFile(TempFilePath, _tempFileExisted, _tempFileContent);
            _originalSessionState.Restore();
            _editorSettingsRepository.InvalidateCache();
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryMissingAndBackupExists_ShouldRestoreBackup()
        {
            UnityCliLoopEditorSettingsData backupData = new() { showDeveloperTools = true };
            File.WriteAllText(BackupFilePath, SerializeSettingsData(backupData));

            _editorSettingsPort.RecoverSettingsFileIfNeeded();

            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary settings file should be restored from backup");
            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should be consumed after recovery");

            UnityCliLoopEditorSettingsJsonData restored = DeserializeSettingsJson(File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(backupData.showDeveloperTools, restored.showDeveloperTools);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryMissingAndTempExists_ShouldPromoteTemp()
        {
            UnityCliLoopEditorSettingsData oldData = new() { showDeveloperTools = false };
            UnityCliLoopEditorSettingsData newData = new() { showDeveloperTools = true };
            File.WriteAllText(BackupFilePath, SerializeSettingsData(oldData));
            File.WriteAllText(TempFilePath, SerializeSettingsData(newData));

            _editorSettingsPort.RecoverSettingsFileIfNeeded();

            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary settings file should be restored from temp");
            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should be removed after temp recovery");
            Assert.IsFalse(File.Exists(TempFilePath), "Temp file should be consumed after recovery");

            UnityCliLoopEditorSettingsJsonData restored = DeserializeSettingsJson(File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(newData.showDeveloperTools, restored.showDeveloperTools);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryExists_ShouldCleanStaleSidecars()
        {
            UnityCliLoopEditorSettingsData primaryData = new() { showDeveloperTools = true };
            File.WriteAllText(SettingsFilePath, SerializeSettingsData(primaryData));
            File.WriteAllText(BackupFilePath, SerializeSettingsData(new UnityCliLoopEditorSettingsData { showDeveloperTools = false }));
            File.WriteAllText(TempFilePath, SerializeSettingsData(new UnityCliLoopEditorSettingsData { showDeveloperTools = false }));

            _editorSettingsPort.RecoverSettingsFileIfNeeded();

            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should not linger once primary exists");
            Assert.IsFalse(File.Exists(TempFilePath), "Temp should not linger once primary exists");

            UnityCliLoopEditorSettingsJsonData restored = DeserializeSettingsJson(File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(primaryData.showDeveloperTools, restored.showDeveloperTools);
        }

        [Test]
        public void GetSettings_WhenLegacySetupWizardStateExists_ShouldMigrateVersionState()
        {
            // Verifies that v2 upgrade users still get the update wizard instead of a first-install state.
            UnityCliLoopEditorSettingsData legacyData = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "2.1.1",
                suppressSetupWizardAutoShow = false
            };
            File.WriteAllText(LegacySettingsFilePath, SerializeSettingsData(legacyData));

            string lastSeenVersion = _editorSettingsPort.GetLastSeenSetupWizardVersion();
            bool suppressAutoShow = _editorSettingsPort.GetSuppressSetupWizardAutoShow();

            Assert.That(lastSeenVersion, Is.EqualTo("2.1.1"));
            Assert.That(suppressAutoShow, Is.False);
            Assert.That(File.Exists(SettingsFilePath), Is.True);
            Assert.That(File.Exists(LegacySettingsFilePath), Is.False);
        }

        [Test]
        public void GetSettings_WhenLegacySetupWizardAutoShowWasSuppressed_ShouldMigrateSuppressChoice()
        {
            // Verifies that the legacy "don't show after updates" choice is preserved.
            UnityCliLoopEditorSettingsData legacyData = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "2.1.1",
                suppressSetupWizardAutoShow = true
            };
            File.WriteAllText(LegacySettingsFilePath, SerializeSettingsData(legacyData));

            bool suppressAutoShow = _editorSettingsPort.GetSuppressSetupWizardAutoShow();

            Assert.That(suppressAutoShow, Is.True);
            Assert.That(File.Exists(LegacySettingsFilePath), Is.False);
        }

        [Test]
        public void GetSettings_WhenCurrentSetupWizardVersionExistsBeforeMigration_ShouldRestoreLegacyVersionState()
        {
            // Verifies that a failed pre-migration auto-show can retry from the legacy upgrade version.
            UnityCliLoopEditorSettingsData currentData = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "3.0.0-beta.3",
                suppressSetupWizardAutoShow = false
            };
            UnityCliLoopEditorSettingsData legacyData = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "2.1.1",
                suppressSetupWizardAutoShow = false
            };
            File.WriteAllText(SettingsFilePath, SerializeSettingsData(currentData));
            File.WriteAllText(LegacySettingsFilePath, SerializeSettingsData(legacyData));

            string lastSeenVersion = _editorSettingsPort.GetLastSeenSetupWizardVersion();
            bool suppressAutoShow = _editorSettingsPort.GetSuppressSetupWizardAutoShow();

            Assert.That(lastSeenVersion, Is.EqualTo("2.1.1"));
            Assert.That(suppressAutoShow, Is.False);
            Assert.That(File.Exists(LegacySettingsFilePath), Is.False);
        }

        [Test]
        public void GetSettings_WhenLegacySetupWizardStateAlreadyMigrated_ShouldKeepCurrentVersionState()
        {
            // Verifies that legacy setup state is applied only once.
            UnityCliLoopEditorSettingsData currentData = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "3.0.0-beta.3",
                suppressSetupWizardAutoShow = false,
                legacySetupWizardStateMigrated = true
            };
            UnityCliLoopEditorSettingsData legacyData = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "2.1.1",
                suppressSetupWizardAutoShow = true
            };
            File.WriteAllText(SettingsFilePath, SerializeSettingsData(currentData));
            File.WriteAllText(LegacySettingsFilePath, SerializeSettingsData(legacyData));

            string lastSeenVersion = _editorSettingsPort.GetLastSeenSetupWizardVersion();
            bool suppressAutoShow = _editorSettingsPort.GetSuppressSetupWizardAutoShow();

            Assert.That(lastSeenVersion, Is.EqualTo("3.0.0-beta.3"));
            Assert.That(suppressAutoShow, Is.False);
            Assert.That(File.Exists(LegacySettingsFilePath), Is.False);
        }

        [Test]
        public void GetInstallSkillsFlat_WhenMissingFromSettings_DefaultsToTrue()
        {
            File.WriteAllText(SettingsFilePath, "{\"showDeveloperTools\":true}");
            _editorSettingsRepository.InvalidateCache();

            bool installSkillsFlat = _editorSettingsPort.GetSettings().installSkillsFlat;

            Assert.IsTrue(installSkillsFlat);
        }

        [Test]
        public void GetSettings_WhenStringFieldIsExplicitNull_ShouldPreserveJsonUtilityEmptyStringMaterializationAndApplyMissingDefaults()
        {
            // Verifies that DTO-to-VO conversion preserves JsonUtility's empty-string materialization.
            File.WriteAllText(SettingsFilePath, "{\"lastSeenSetupWizardVersion\":null}");
            _editorSettingsRepository.InvalidateCache();

            UnityCliLoopEditorSettingsData settings = _editorSettingsPort.GetSettings();

            Assert.That(settings.lastSeenSetupWizardVersion, Is.Empty);
            Assert.That(settings.lastSeenSetupWizardMinimumDispatcherVersion, Is.Empty);
            Assert.That(settings.showUnityCliLoopSecuritySetting, Is.True);
            Assert.That(settings.showToolSettings, Is.True);
            Assert.That(settings.installSkillsFlat, Is.True);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenLegacyPortFieldsExist_ShouldRemoveThem()
        {
            File.WriteAllText(
                SettingsFilePath,
                "{" +
                "\"customPort\":18447," +
                "\"serverPort\":18448," +
                "\"serverTransportKind\":\"tcp\"," +
                "\"projectRootPath\":\"/stale/project\"," +
                "\"serverSessionId\":\"stale-session\"," +
                "\"isServerRunning\":true," +
                "\"isAfterCompile\":true," +
                "\"isDomainReloadInProgress\":true," +
                "\"isReconnecting\":true," +
                "\"showReconnectingUI\":true," +
                "\"showPostCompileReconnectingUI\":true," +
                "\"connectedLLMTools\":[{\"Name\":\"codex\",\"Endpoint\":\"/tmp/uloop/test.sock#1\",\"Port\":18449}]" +
                "}");

            _editorSettingsPort.RecoverSettingsFileIfNeeded();

            string recoveredJson = File.ReadAllText(SettingsFilePath);
            StringAssert.DoesNotContain("customPort", recoveredJson);
            StringAssert.DoesNotContain("serverPort", recoveredJson);
            StringAssert.DoesNotContain("serverTransportKind", recoveredJson);
            StringAssert.DoesNotContain("projectRootPath", recoveredJson);
            StringAssert.DoesNotContain("serverSessionId", recoveredJson);
            StringAssert.DoesNotContain("isServerRunning", recoveredJson);
            StringAssert.DoesNotContain("isAfterCompile", recoveredJson);
            StringAssert.DoesNotContain("isDomainReloadInProgress", recoveredJson);
            StringAssert.DoesNotContain("isReconnecting", recoveredJson);
            StringAssert.DoesNotContain("showReconnectingUI", recoveredJson);
            StringAssert.DoesNotContain("showPostCompileReconnectingUI", recoveredJson);
            StringAssert.DoesNotContain("connectedLLMTools", recoveredJson);
            StringAssert.DoesNotContain("\"Port\"", recoveredJson);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenSettingsFileExceedsSizeLimit_ShouldThrowSecurityException()
        {
            File.WriteAllText(SettingsFilePath, new string(' ', UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES + 1));

            Assert.Throws<SecurityException>(() => _editorSettingsPort.RecoverSettingsFileIfNeeded());
        }

        [Test]
        public void SetInstallSkillsFlat_PersistsValue()
        {
            _editorSettingsPort.SetInstallSkillsFlat(true);
            _editorSettingsRepository.InvalidateCache();

            bool installSkillsFlat = _editorSettingsPort.GetSettings().installSkillsFlat;

            Assert.IsTrue(installSkillsFlat);
        }

        [Test]
        public void UpdateSessionState_WhenStartingServer_ShouldNotPersistRuntimeIdentity()
        {
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData { showDeveloperTools = true });
            UnityCliLoopServerStartupService service =
                new UnityCliLoopServerStartupService(
                    new TestServerInstanceFactory(),
                    _sessionFlagsRepository);

            ServiceResult<bool> result = service.UpdateSessionState(true);

            Assert.IsTrue(result.Success, "Session update should succeed");
            Assert.IsTrue(_sessionFlagsRepository.GetIsServerRunning(), "Server running state should be kept for this Editor session");
            string savedJson = File.ReadAllText(SettingsFilePath);
            StringAssert.DoesNotContain("isServerRunning", savedJson);
            StringAssert.DoesNotContain("projectRootPath", savedJson);
            StringAssert.DoesNotContain("serverSessionId", savedJson);
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

        private static string SerializeSettingsData(UnityCliLoopEditorSettingsData settings)
        {
            return JsonUtility.ToJson(UnityCliLoopEditorSettingsJsonData.FromDomain(settings), true);
        }

        private static UnityCliLoopEditorSettingsJsonData DeserializeSettingsJson(string json)
        {
            return JsonUtility.FromJson<UnityCliLoopEditorSettingsJsonData>(json);
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstanceFactory : IUnityCliLoopServerInstanceFactory
        {
            public IUnityCliLoopServerInstance Create()
            {
                return new TestServerInstance();
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning => false;

            public string Endpoint => "test";

            public void StartServer()
            {
            }

            public void StopServer()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
