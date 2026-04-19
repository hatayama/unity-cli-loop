using System.IO;

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
            McpEditorSettingsData backupData = new() { customPort = 18443 };
            File.WriteAllText(BackupFilePath, JsonUtility.ToJson(backupData, true));

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary settings file should be restored from backup");
            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should be consumed after recovery");

            McpEditorSettingsData restored = JsonUtility.FromJson<McpEditorSettingsData>(
                File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(backupData.customPort, restored.customPort);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryMissingAndTempExists_ShouldPromoteTemp()
        {
            McpEditorSettingsData oldData = new() { customPort = 17400 };
            McpEditorSettingsData newData = new() { customPort = 18444 };
            File.WriteAllText(BackupFilePath, JsonUtility.ToJson(oldData, true));
            File.WriteAllText(TempFilePath, JsonUtility.ToJson(newData, true));

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary settings file should be restored from temp");
            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should be removed after temp recovery");
            Assert.IsFalse(File.Exists(TempFilePath), "Temp file should be consumed after recovery");

            McpEditorSettingsData restored = JsonUtility.FromJson<McpEditorSettingsData>(
                File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(newData.customPort, restored.customPort);
        }

        [Test]
        public void RecoverSettingsFileIfNeeded_WhenPrimaryExists_ShouldCleanStaleSidecars()
        {
            McpEditorSettingsData primaryData = new() { customPort = 18445 };
            File.WriteAllText(SettingsFilePath, JsonUtility.ToJson(primaryData, true));
            File.WriteAllText(BackupFilePath, JsonUtility.ToJson(new McpEditorSettingsData { customPort = 17401 }, true));
            File.WriteAllText(TempFilePath, JsonUtility.ToJson(new McpEditorSettingsData { customPort = 19401 }, true));

            McpEditorSettings.RecoverSettingsFileIfNeeded();

            Assert.IsFalse(File.Exists(BackupFilePath), "Backup should not linger once primary exists");
            Assert.IsFalse(File.Exists(TempFilePath), "Temp should not linger once primary exists");

            McpEditorSettingsData restored = JsonUtility.FromJson<McpEditorSettingsData>(
                File.ReadAllText(SettingsFilePath));
            Assert.AreEqual(primaryData.customPort, restored.customPort);
        }

        [Test]
        public void GetInstallSkillsFlat_WhenMissingFromSettings_DefaultsToFalse()
        {
            File.WriteAllText(SettingsFilePath, "{\"customPort\":18446}");
            McpEditorSettings.InvalidateCache();

            bool installSkillsFlat = McpEditorSettings.GetInstallSkillsFlat();

            Assert.IsFalse(installSkillsFlat);
        }

        [Test]
        public void SetInstallSkillsFlat_PersistsValue()
        {
            McpEditorSettings.SetInstallSkillsFlat(true);
            McpEditorSettings.InvalidateCache();

            bool installSkillsFlat = McpEditorSettings.GetInstallSkillsFlat();

            Assert.IsTrue(installSkillsFlat);
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
