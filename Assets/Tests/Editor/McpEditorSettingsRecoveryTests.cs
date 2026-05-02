using System.IO;
using System.Security;

using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    [TestFixture]
    public class McpEditorSettingsRecoveryTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(McpConstants.USER_SETTINGS_FOLDER, McpConstants.SETTINGS_FILE_NAME);
        private static readonly string BackupFilePath = SettingsFilePath + ".bak";
        private static readonly string TempFilePath = SettingsFilePath + ".tmp";

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private bool _backupFileExisted;
        private string _backupFileContent;
        private bool _tempFileExisted;
        private string _tempFileContent;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            _backupFileExisted = File.Exists(BackupFilePath);
            _backupFileContent = _backupFileExisted ? File.ReadAllText(BackupFilePath) : null;

            _tempFileExisted = File.Exists(TempFilePath);
            _tempFileContent = _tempFileExisted ? File.ReadAllText(TempFilePath) : null;

            if (!Directory.Exists(McpConstants.USER_SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(McpConstants.USER_SETTINGS_FOLDER);
            }

            DeleteIfExists(SettingsFilePath);
            DeleteIfExists(BackupFilePath);
            DeleteIfExists(TempFilePath);
            McpEditorSettings.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            RestoreFile(BackupFilePath, _backupFileExisted, _backupFileContent);
            RestoreFile(TempFilePath, _tempFileExisted, _tempFileContent);
            McpEditorSettings.InvalidateCache();
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryMissingAndBackupExists_ShouldRestoreBackup()
        {
            McpEditorSettingsData backupData = new() { showDeveloperTools = true };
            File.WriteAllText(BackupFilePath, JsonUtility.ToJson(backupData, true));

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary settings file should be restored from backup");
            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should be consumed after recovery");

            McpEditorSettingsData restored = JsonUtility.FromJson<McpEditorSettingsData>(
                File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(backupData.showDeveloperTools, restored.showDeveloperTools);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryMissingAndTempExists_ShouldPromoteTemp()
        {
            McpEditorSettingsData oldData = new() { showDeveloperTools = false };
            McpEditorSettingsData newData = new() { showDeveloperTools = true };
            File.WriteAllText(BackupFilePath, JsonUtility.ToJson(oldData, true));
            File.WriteAllText(TempFilePath, JsonUtility.ToJson(newData, true));

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary settings file should be restored from temp");
            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should be removed after temp recovery");
            Assert.IsFalse(File.Exists(TempFilePath), "Temp file should be consumed after recovery");

            McpEditorSettingsData restored = JsonUtility.FromJson<McpEditorSettingsData>(
                File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(newData.showDeveloperTools, restored.showDeveloperTools);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryExists_ShouldCleanStaleSidecars()
        {
            McpEditorSettingsData primaryData = new() { showDeveloperTools = true };
            File.WriteAllText(SettingsFilePath, JsonUtility.ToJson(primaryData, true));
            File.WriteAllText(BackupFilePath, JsonUtility.ToJson(new McpEditorSettingsData { showDeveloperTools = false }, true));
            File.WriteAllText(TempFilePath, JsonUtility.ToJson(new McpEditorSettingsData { showDeveloperTools = false }, true));

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should not linger once primary exists");
            Assert.IsFalse(File.Exists(TempFilePath), "Temp should not linger once primary exists");

            McpEditorSettingsData restored = JsonUtility.FromJson<McpEditorSettingsData>(
                File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(primaryData.showDeveloperTools, restored.showDeveloperTools);
        }

        [Test]
        public void GetInstallSkillsFlat_WhenMissingFromSettings_DefaultsToTrue()
        {
            File.WriteAllText(SettingsFilePath, "{\"showDeveloperTools\":true}");
            McpEditorSettings.InvalidateCache();

            bool installSkillsFlat = McpEditorSettings.GetInstallSkillsFlat();

            Assert.IsTrue(installSkillsFlat);
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
                "\"connectedLLMTools\":[{\"Name\":\"codex\",\"Endpoint\":\"/tmp/uloop/test.sock#1\",\"Port\":18449}]" +
                "}");

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            string recoveredJson = File.ReadAllText(SettingsFilePath);
            StringAssert.DoesNotContain("customPort", recoveredJson);
            StringAssert.DoesNotContain("serverPort", recoveredJson);
            StringAssert.DoesNotContain("serverTransportKind", recoveredJson);
            StringAssert.DoesNotContain("projectRootPath", recoveredJson);
            StringAssert.DoesNotContain("serverSessionId", recoveredJson);
            StringAssert.DoesNotContain("\"Port\"", recoveredJson);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenSettingsFileExceedsSizeLimit_ShouldThrowSecurityException()
        {
            File.WriteAllText(SettingsFilePath, new string(' ', McpConstants.MAX_SETTINGS_SIZE_BYTES + 1));

            Assert.Throws<SecurityException>(() => McpEditorSettings.RecoverSettingsFileIfNeeded());
        }

        [Test]
        public void SetInstallSkillsFlat_PersistsValue()
        {
            McpEditorSettings.SetInstallSkillsFlat(true);
            McpEditorSettings.InvalidateCache();

            bool installSkillsFlat = McpEditorSettings.GetInstallSkillsFlat();

            Assert.IsTrue(installSkillsFlat);
        }

        [Test]
        public void UpdateSessionState_WhenStartingServer_ShouldNotPersistRuntimeIdentity()
        {
            McpServerStartupService service = new();

            ServiceResult<bool> result = service.UpdateSessionState(true);

            Assert.IsTrue(result.Success, "Session update should succeed");
            Assert.IsTrue(McpEditorSettings.GetIsServerRunning(), "Server running state should be persisted");
            string savedJson = File.ReadAllText(SettingsFilePath);
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
    }
}
